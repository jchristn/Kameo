namespace Kameo.Core
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Reads Claude subscription limits with the profile's saved access token. Never refreshes or writes credentials.</summary>
    internal static partial class ClaudeUsage
    {
        internal static readonly Uri DefaultEndpoint = new Uri("https://api.anthropic.com/api/oauth/usage");
        private const string _OAuthBeta = "oauth-2025-04-20";
        private const int _MaximumResponseBytes = 1024 * 1024;
        private static readonly string[] _KnownWindows = { "five_hour", "seven_day", "seven_day_opus", "seven_day_sonnet", "seven_day_oauth_apps" };

        internal static async Task<UsageReport> ReadAsync(ProfileStore store, Profile profile, HttpClient client, Uri endpoint, DateTimeOffset now, bool offline, CancellationToken cancellationToken)
        {
            string directory = store.ProfileDirectory(profile.Id);
            // Offline checks never read the saved login or the Keychain.
            if (offline) return UsageReport.For(profile, UsageStatus.NotChecked, "Claude Code usage requires a live check.");
            string? credential = await ReadCredentialAsync(directory, cancellationToken).ConfigureAwait(false);
            if (credential == null) return UsageReport.For(profile, UsageStatus.SignedOut, "Sign in to see plan usage.");
            string token;
            string plan;
            DateTimeOffset? expires;
            try
            {
                using JsonDocument document = JsonDocument.Parse(credential);
                if (!document.RootElement.TryGetProperty("claudeAiOauth", out JsonElement oauth) || oauth.ValueKind != JsonValueKind.Object
                    || !oauth.TryGetProperty("accessToken", out JsonElement access) || access.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(access.GetString()))
                    return UsageReport.For(profile, UsageStatus.SignedOut, "The saved login has no access token. Sign in again.");
                token = access.GetString()!;
                plan = Plan(oauth);
                expires = oauth.TryGetProperty("expiresAt", out JsonElement expiry) && expiry.ValueKind == JsonValueKind.Number && expiry.TryGetInt64(out long milliseconds)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) : null;
            }
            catch (JsonException) { return UsageReport.For(profile, UsageStatus.Unavailable, "The saved login is not valid JSON. Sign in again or re-import it."); }
            if (expires != null && expires.Value <= now.AddMinutes(1))
                return Annotate(UsageReport.For(profile, UsageStatus.Expired, "The saved sign-in has expired. Launch this profile once so Claude Code can refresh it, then check again."), plan);

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("anthropic-beta", _OAuthBeta);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Kameo", "0.1.1"));
            string body;
            try
            {
                using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                switch (response.StatusCode)
                {
                    case HttpStatusCode.Unauthorized:
                        return Annotate(UsageReport.For(profile, UsageStatus.Expired, "Claude rejected the saved sign-in. Launch this profile to refresh it, or sign in again."), plan);
                    case HttpStatusCode.Forbidden:
                        return Annotate(UsageReport.For(profile, UsageStatus.Unavailable, "This login cannot read plan usage. Sign in again to grant access."), plan);
                    case HttpStatusCode.TooManyRequests:
                        return Annotate(UsageReport.For(profile, UsageStatus.Unavailable, "Claude is limiting usage checks. Try again in a minute."), plan);
                }
                if (!response.IsSuccessStatusCode)
                    return Annotate(UsageReport.For(profile, UsageStatus.Unavailable, "Claude usage check failed (HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + ")."), plan);
                body = await ReadBoundedAsync(response.Content, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { return Annotate(UsageReport.For(profile, UsageStatus.Unavailable, "Claude did not respond in time. Try again."), plan); }
            catch (HttpRequestException)
            { return Annotate(UsageReport.For(profile, UsageStatus.Unavailable, "Unable to reach Claude. Check your network connection."), plan); }
            catch (InvalidDataException)
            { return Annotate(UsageReport.For(profile, UsageStatus.Unavailable, "Claude returned usage in an unexpected format."), plan); }

            List<UsageWindow> windows;
            string detail;
            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
                windows = ParseWindows(document.RootElement);
                detail = ParseDetail(document.RootElement);
            }
            catch (JsonException) { return Annotate(UsageReport.For(profile, UsageStatus.Unavailable, "Claude returned usage in an unexpected format."), plan); }
            return new UsageReport
            {
                ProfileId = profile.Id, ProfileName = profile.Name, Harness = profile.Harness, Status = UsageStatus.Available, Source = UsageSource.Live,
                Plan = plan, Detail = detail, ObservedUtc = now, Windows = windows,
                Message = windows.Count == 0 ? "Claude reported no plan limits for this login." : ""
            };
        }

        /// <summary>Returns the fresher usable login of the scoped macOS Keychain entry and the profile file.</summary>
        internal static async Task<string?> ReadCredentialAsync(string directory, CancellationToken cancellationToken)
        {
            string path = Path.Combine(directory, HarnessCatalog.CredentialFile(HarnessKind.Claude));
            string? file = File.Exists(path) ? PrivateFiles.Read(path) : null;
            if (!OperatingSystem.IsMacOS()) return file;
            string? live = null;
            try { live = await MacCredentialBridge.ReadAsync(MacCredentialBridge.ServiceName(directory), cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            // Prefer the fresher usable login. Claude clears the Keychain item's tokens when a refresh fails.
            long? liveFreshness = MacCredentialBridge.Freshness(live);
            long? fileFreshness = MacCredentialBridge.Freshness(file);
            if (liveFreshness != null && (fileFreshness == null || liveFreshness.Value >= fileFreshness.Value)) return live;
            return fileFreshness != null ? file : (string.IsNullOrWhiteSpace(live) ? file : live);
        }

        /// <summary>Parses the usage endpoint's windows. Utilization is a 0 to 100 percentage.</summary>
        internal static List<UsageWindow> ParseWindows(JsonElement root)
        {
            List<UsageWindow> windows = new List<UsageWindow>();
            foreach (string key in _KnownWindows)
            {
                if (!root.TryGetProperty(key, out JsonElement window) || !TryWindow(window, out double used, out DateTimeOffset? resets)) continue;
                windows.Add(new UsageWindow(Label(key), used, resets, key == "five_hour" ? 300 : 10080));
            }
            if (root.TryGetProperty("limits", out JsonElement limits) && limits.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement limit in limits.EnumerateArray())
                {
                    if (!TryWindow(limit, out double used, out DateTimeOffset? resets)) continue;
                    string? name = Text(limit, "display_name") ?? Text(limit, "name") ?? Text(limit, "model");
                    if (name == null) continue;
                    string label = "Weekly " + name;
                    if (windows.Exists(existing => string.Equals(existing.Label, label, StringComparison.OrdinalIgnoreCase))) continue;
                    windows.Add(new UsageWindow(label, used, resets, 10080));
                }
            }
            return windows;
        }

        internal static string ParseDetail(JsonElement root) => root.TryGetProperty("extra_usage", out JsonElement extra) && extra.ValueKind == JsonValueKind.Object
            && extra.TryGetProperty("is_enabled", out JsonElement enabled) && enabled.ValueKind == JsonValueKind.True ? "Extra usage enabled" : "";

        /// <summary>Formats the saved login's plan, such as "Max 20x" or "Pro".</summary>
        internal static string Plan(JsonElement oauth)
        {
            string? subscription = Text(oauth, "subscriptionType");
            if (subscription == null) return "";
            string plan = char.ToUpperInvariant(subscription[0]) + subscription[1..].ToLowerInvariant();
            string? tier = Text(oauth, "rateLimitTier");
            Match multiplier = tier == null ? Match.Empty : Multiplier().Match(tier);
            return multiplier.Success ? plan + " " + multiplier.Groups[1].Value : plan;
        }

        private static UsageReport Annotate(UsageReport report, string plan) => plan.Length == 0 ? report : new UsageReport
        {
            ProfileId = report.ProfileId, ProfileName = report.ProfileName, Harness = report.Harness, Status = report.Status, Message = report.Message, Plan = plan
        };

        private static bool TryWindow(JsonElement window, out double used, out DateTimeOffset? resets)
        {
            used = 0; resets = null;
            if (window.ValueKind != JsonValueKind.Object || !window.TryGetProperty("utilization", out JsonElement utilization) || utilization.ValueKind != JsonValueKind.Number) return false;
            used = utilization.GetDouble();
            string? reset = Text(window, "resets_at");
            if (reset != null && DateTimeOffset.TryParse(reset, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)) resets = parsed;
            return true;
        }

        private static string Label(string key) => key switch
        {
            "five_hour" => "5-hour", "seven_day" => "Weekly", "seven_day_opus" => "Weekly Opus",
            "seven_day_sonnet" => "Weekly Sonnet", "seven_day_oauth_apps" => "Weekly apps", _ => key
        };

        private static string? Text(JsonElement element, string property) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;

        private static async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
        {
            using Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using MemoryStream buffer = new MemoryStream();
            byte[] chunk = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > _MaximumResponseBytes) throw new InvalidDataException("The usage response is too large.");
                buffer.Write(chunk, 0, read);
            }
            return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }

        [GeneratedRegex("_(\\d+x)$", RegexOptions.CultureInvariant)]
        private static partial Regex Multiplier();
    }
}
