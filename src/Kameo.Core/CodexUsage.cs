namespace Kameo.Core
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Reads Codex plan limits by asking the profile's own <c>codex app-server</c>, which owns token refresh,
    /// and falls back to the limits Codex recorded in the profile's session logs.
    /// </summary>
    internal static class CodexUsage
    {
        internal static readonly TimeSpan LiveTimeout = TimeSpan.FromSeconds(20);
        private const int _MaximumLines = 2000;

        internal static async Task<UsageReport> ReadAsync(ProfileStore store, Profile profile, DateTimeOffset now, bool offline, TimeSpan liveTimeout, CancellationToken cancellationToken)
        {
            string directory = store.ProfileDirectory(profile.Id);
            if (!File.Exists(Path.Combine(directory, HarnessCatalog.CredentialFile(HarnessKind.Codex))))
                return UsageReport.For(profile, UsageStatus.SignedOut, "Sign in to see plan usage.");
            string failure = "";
            UsageStatus failureStatus = UsageStatus.Unavailable;
            if (!offline)
            {
                try { return await ReadLiveAsync(store, profile, now, liveTimeout, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { failure = "Codex did not respond in time."; }
                catch (FileNotFoundException) { failure = "The Codex executable was not found. Install it on PATH or set the profile's executable."; }
                catch (InvalidOperationException error) when (error.Message == ProfileInUse) { failure = error.Message; failureStatus = UsageStatus.InUse; }
                catch (CodexProtocolException error) { failure = error.Message; }
                catch (Exception error) when (error is IOException or InvalidOperationException or JsonException or System.ComponentModel.Win32Exception)
                { failure = "Codex could not report usage."; }
            }
            UsageReport? recorded = CodexSessionLog.Read(profile, directory, now);
            if (recorded != null)
            {
                if (failure.Length == 0) return recorded;
                return new UsageReport
                {
                    ProfileId = recorded.ProfileId, ProfileName = recorded.ProfileName, Harness = recorded.Harness, Status = recorded.Status,
                    Source = recorded.Source, Plan = recorded.Plan, Detail = recorded.Detail, ObservedUtc = recorded.ObservedUtc, Windows = recorded.Windows,
                    Message = "Live check failed: " + failure + " Showing the last session."
                };
            }
            if (failure.Length > 0) return UsageReport.For(profile, failureStatus, failure);
            return UsageReport.For(profile, UsageStatus.NotChecked, "No Codex session has been recorded for this profile yet. Run a live check or launch the profile.");
        }

        internal const string ProfileInUse = "This profile is in use by a sign-in or edit. Try again when it finishes.";

        private static async Task<UsageReport> ReadLiveAsync(ProfileStore store, Profile profile, DateTimeOffset now, TimeSpan liveTimeout, CancellationToken cancellationToken)
        {
            string directory = store.ProfileDirectory(profile.Id);
            FileStream lease;
            try { lease = store.AcquireSessionLease(profile.Id); }
            catch (InvalidOperationException) { throw new InvalidOperationException(ProfileInUse); }
            using (lease)
            {
                // Run from the profile directory so a project's local Codex configuration is not loaded.
                LaunchPlan plan = new LaunchService(store).Prepare(profile, new[] { "app-server" }, false, directory);
                ProcessStartInfo start = ExecutableResolver.Resolve(plan.StartInfo);
                start.RedirectStandardInput = true;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                start.StandardInputEncoding = new UTF8Encoding(false);
                start.StandardOutputEncoding = Encoding.UTF8;
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(liveTimeout);
                using Process process = new Process { StartInfo = start };
                process.Start();
                Task errors = DrainAsync(process.StandardError);
                try
                {
                    JsonObject initialize = new JsonObject
                    {
                        ["id"] = 1, ["method"] = "initialize",
                        ["params"] = new JsonObject { ["clientInfo"] = new JsonObject { ["name"] = "kameo", ["title"] = "Kameo", ["version"] = "0.1.2" } }
                    };
                    await SendAsync(process, initialize, timeout.Token).ConfigureAwait(false);
                    using (JsonDocument ignored = await ResponseAsync(process, 1, timeout.Token).ConfigureAwait(false)) { }
                    await SendAsync(process, new JsonObject { ["method"] = "initialized" }, timeout.Token).ConfigureAwait(false);
                    JsonObject read = new JsonObject
                    {
                        ["id"] = 2, ["method"] = "account/rateLimits/read",
                        ["params"] = new JsonObject { ["excludeResetCreditDetails"] = true }
                    };
                    await SendAsync(process, read, timeout.Token).ConfigureAwait(false);
                    using JsonDocument response = await ResponseAsync(process, 2, timeout.Token).ConfigureAwait(false);
                    return Report(profile, response.RootElement.GetProperty("result"), UsageSource.Live, now, now);
                }
                finally
                {
                    try { process.StandardInput.Close(); } catch (IOException) { }
                    if (!process.HasExited)
                    {
                        try { process.Kill(true); } catch (InvalidOperationException) { }
                    }
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    await errors.ConfigureAwait(false);
                }
            }
        }

        /// <summary>Builds a report from an app-server result or a session-log rate-limit snapshot. Accepts camelCase and snake_case.</summary>
        internal static UsageReport Report(Profile profile, JsonElement result, UsageSource source, DateTimeOffset observed, DateTimeOffset now)
        {
            List<JsonElement> snapshots = new List<JsonElement>();
            JsonElement byId = Property(result, "rateLimitsByLimitId", "rate_limits_by_limit_id");
            if (byId.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty bucket in byId.EnumerateObject())
                    if (bucket.Value.ValueKind == JsonValueKind.Object)
                    {
                        if (bucket.Name == "codex") snapshots.Insert(0, bucket.Value);
                        else snapshots.Add(bucket.Value);
                    }
            }
            if (snapshots.Count == 0)
            {
                JsonElement single = Property(result, "rateLimits", "rate_limits");
                snapshots.Add(single.ValueKind == JsonValueKind.Object ? single : result);
            }
            List<UsageWindow> windows = new List<UsageWindow>();
            List<string> details = new List<string>();
            string plan = "";
            foreach (JsonElement snapshot in snapshots)
            {
                string? limitId = Text(snapshot, "limitId", "limit_id");
                string prefix = limitId == null || limitId == "codex" ? "" : (Text(snapshot, "limitName", "limit_name") ?? limitId) + " ";
                foreach (string slot in new[] { "primary", "secondary" })
                {
                    JsonElement window = Property(snapshot, slot, slot);
                    if (window.ValueKind != JsonValueKind.Object) continue;
                    JsonElement usedElement = Property(window, "usedPercent", "used_percent");
                    if (usedElement.ValueKind != JsonValueKind.Number) continue;
                    double used = usedElement.GetDouble();
                    int? minutes = Integer(Property(window, "windowDurationMins", "window_minutes"));
                    DateTimeOffset? resets = null;
                    long? resetsAt = Long(Property(window, "resetsAt", "resets_at"));
                    long? resetsIn = Long(Property(window, "resetsInSeconds", "resets_in_seconds"));
                    if (resetsAt != null) resets = DateTimeOffset.FromUnixTimeSeconds(resetsAt.Value);
                    else if (resetsIn != null) resets = observed.AddSeconds(resetsIn.Value);
                    if (source == UsageSource.SessionLog && resets != null && resets.Value <= now) { used = 0; resets = null; }
                    windows.Add(new UsageWindow(prefix + UsageFormat.WindowLabel(minutes), used, resets, minutes));
                }
                if (plan.Length == 0) plan = PlanName(Text(snapshot, "planType", "plan_type"));
                if (prefix.Length > 0) continue;
                JsonElement credits = Property(snapshot, "credits", "credits");
                if (credits.ValueKind == JsonValueKind.Object)
                {
                    if (Property(credits, "unlimited", "unlimited").ValueKind == JsonValueKind.True) details.Add("Unlimited credits");
                    else if (Property(credits, "hasCredits", "has_credits").ValueKind == JsonValueKind.True
                        && decimal.TryParse(Text(credits, "balance", "balance"), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal balance) && balance > 0)
                        details.Add("Credits " + balance.ToString("N0", CultureInfo.InvariantCulture));
                }
                if (Text(snapshot, "rateLimitReachedType", "rate_limit_reached_type") != null) details.Add("Limit reached");
            }
            return new UsageReport
            {
                ProfileId = profile.Id, ProfileName = profile.Name, Harness = profile.Harness, Status = UsageStatus.Available, Source = source,
                Plan = plan, Detail = string.Join(" · ", details), ObservedUtc = observed, Windows = windows,
                Message = windows.Count == 0 ? "Codex reported no plan limits for this login." : ""
            };
        }

        private static async Task SendAsync(Process process, JsonObject message, CancellationToken cancellationToken)
        {
            await process.StandardInput.WriteLineAsync(message.ToJsonString().AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task<JsonDocument> ResponseAsync(Process process, int id, CancellationToken cancellationToken)
        {
            for (int count = 0; count < _MaximumLines; count++)
            {
                string? line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null) throw new CodexProtocolException("Codex exited before reporting usage.");
                if (line.Length == 0 || line[0] != '{') continue;
                JsonDocument document;
                try { document = JsonDocument.Parse(line); }
                catch (JsonException) { continue; }
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("method", out _)
                    || !root.TryGetProperty("id", out JsonElement responseId) || responseId.ValueKind != JsonValueKind.Number || responseId.GetInt32() != id)
                {
                    document.Dispose();
                    continue;
                }
                if (root.TryGetProperty("error", out JsonElement error))
                {
                    string message = Text(error, "message", "message") ?? "Codex could not report usage.";
                    document.Dispose();
                    throw new CodexProtocolException(Clean(message));
                }
                if (!root.TryGetProperty("result", out JsonElement result) || result.ValueKind != JsonValueKind.Object)
                {
                    document.Dispose();
                    throw new CodexProtocolException("Codex returned usage in an unexpected format.");
                }
                return document;
            }
            throw new CodexProtocolException("Codex did not answer the usage request.");
        }

        private static string Clean(string message)
        {
            StringBuilder builder = new StringBuilder();
            foreach (char character in message)
            {
                if (builder.Length >= 200) break;
                builder.Append(char.IsControl(character) ? ' ' : character);
            }
            string text = builder.ToString().Trim();
            if (text.Contains("authentication", StringComparison.OrdinalIgnoreCase) || text.Contains("sign in", StringComparison.OrdinalIgnoreCase) || text.Contains("login", StringComparison.OrdinalIgnoreCase))
                return "Codex needs this profile to sign in with ChatGPT (" + text + ").";
            return text.Length == 0 ? "Codex could not report usage." : char.ToUpperInvariant(text[0]) + text[1..] + (text.EndsWith('.') ? "" : ".");
        }

        private static async Task DrainAsync(StreamReader reader)
        {
            char[] buffer = new char[4096];
            try { while (await reader.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false) > 0) { } }
            catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException) { }
        }

        internal static string PlanName(string? planType)
        {
            if (string.IsNullOrWhiteSpace(planType) || planType == "unknown") return "";
            string[] words = planType.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < words.Length; index++) words[index] = char.ToUpperInvariant(words[index][0]) + words[index][1..];
            return string.Join(' ', words);
        }

        private static JsonElement Property(JsonElement element, string camel, string snake)
        {
            if (element.ValueKind != JsonValueKind.Object) return default;
            if (element.TryGetProperty(camel, out JsonElement value)) return value;
            return element.TryGetProperty(snake, out value) ? value : default;
        }

        private static string? Text(JsonElement element, string camel, string snake)
        {
            JsonElement value = Property(element, camel, snake);
            return value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;
        }

        private static long? Long(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : null;

        private static int? Integer(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;
    }
}
