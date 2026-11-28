namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Kameo.Core;
    using Touchstone.Core;

    /// <summary>Plan usage and Claude sign-in renewal using an in-process HTTP handler, an in-memory Keychain and a synthetic app-server. No provider is contacted.</summary>
    internal static class UsageCases
    {
        private static readonly Uri _Endpoint = new Uri("https://usage.invalid/api/oauth/usage");
        private static readonly Uri _TokenEndpoint = new Uri("https://token.invalid/v1/oauth/token");
        private const string _Renewed = "{\"access_token\":\"renewed-access\",\"refresh_token\":\"renewed-refresh\",\"expires_in\":28800,\"refresh_token_expires_in\":2592000,\"scope\":\"user:inference user:profile\",\"token_type\":\"Bearer\"}";

        internal static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string name, Func<Fixture, CancellationToken, Task> execute) => cases.Add(KameoSuites.Case("Usage", name, execute));
            void AddSync(string name, Action<Fixture> execute) => cases.Add(KameoSuites.Case("Usage", name, execute));

            Add("ClaudeLiveWindowsParsed", async (fixture, cancellationToken) =>
            {
                Profile profile = ClaudeProfile(fixture, DateTimeOffset.UtcNow.AddHours(4));
                DateTimeOffset reset = DateTimeOffset.UtcNow.AddHours(2);
                string body = "{\"five_hour\":{\"utilization\":42.4,\"resets_at\":\"" + reset.ToString("O", CultureInfo.InvariantCulture) + "\"},"
                    + "\"seven_day\":{\"utilization\":18,\"resets_at\":\"2030-01-06T09:00:00+00:00\"},\"seven_day_opus\":null,"
                    + "\"seven_day_sonnet\":{\"utilization\":3,\"resets_at\":null},\"cinder_cove\":{\"utilization\":99,\"resets_at\":null},"
                    + "\"limits\":[{\"display_name\":\"Fable\",\"utilization\":12,\"resets_at\":\"2030-01-06T09:00:00Z\"},{\"display_name\":\"Sonnet\",\"utilization\":3}],"
                    + "\"extra_usage\":{\"is_enabled\":true,\"monthly_limit\":5000}}";
                FakeHttpHandler handler = new FakeHttpHandler(_ => Json(HttpStatusCode.OK, body));
                UsageReport report = await ReadAsync(Service(fixture, handler), profile.Id, false, cancellationToken).ConfigureAwait(false);
                Fixture.Check(report.Status == UsageStatus.Available && report.Source == UsageSource.Live, "Claude usage was not available: " + report.Message);
                Fixture.Check(handler.Calls == 1 && handler.LastMethod == HttpMethod.Get && handler.LastUri == _Endpoint, "Claude usage used the wrong request.");
                Fixture.Check(handler.LastHeaders["Authorization"] == "Bearer synthetic-access" && handler.LastHeaders["anthropic-beta"] == "oauth-2025-04-20", "Claude usage headers are wrong.");
                Fixture.Check(string.Join("|", report.Windows.Select(window => window.Label)) == "5-hour|Weekly|Weekly Sonnet|Weekly Fable", "Unexpected Claude windows: " + string.Join("|", report.Windows.Select(window => window.Label)));
                Fixture.Check(Math.Abs(report.Windows[0].UsedPercent - 42.4) < 0.001 && report.Windows[0].ResetsAt == reset && report.Windows[0].WindowMinutes == 300, "Five-hour window parsed incorrectly.");
                Fixture.Check(report.Windows[2].ResetsAt == null && report.Windows[3].WindowMinutes == 10080, "Null resets or model windows parsed incorrectly.");
                Fixture.Check(report.Plan == "Max 20x" && report.Detail == "Extra usage enabled", "Plan or detail missing: " + report.Plan + " / " + report.Detail);
            });
            Add("ClaudeExpiredLoginRenewalFailureSkipsUsage", async (fixture, cancellationToken) =>
            {
                Profile profile = ClaudeProfile(fixture, DateTimeOffset.UtcNow.AddMinutes(-5));
                string before = Saved(fixture, profile);
                FakeHttpHandler handler = new FakeHttpHandler(request => request.RequestUri == _TokenEndpoint ? Json(HttpStatusCode.ServiceUnavailable, "synthetic-refresh") : throw new InvalidOperationException("Usage must not be checked."));
                UsageReport report = await ReadAsync(Service(fixture, handler), profile.Id, false, cancellationToken).ConfigureAwait(false);
                Fixture.Check(report.Status == UsageStatus.Expired && handler.Calls == 1 && report.Plan == "Max 20x", "An expired login that could not be renewed must not be sent: " + report.Message);
                Fixture.Check(report.Message.Contains("could not renew", StringComparison.Ordinal) && !report.Message.Contains("synthetic", StringComparison.Ordinal), "The failure was not explained safely: " + report.Message);
                Fixture.Check(Saved(fixture, profile) == before, "A failed renewal changed the saved login.");
            });
            Add("ClaudeExpiringLoginIsRenewed", async (fixture, cancellationToken) =>
            {
                Profile profile = ClaudeProfile(fixture, DateTimeOffset.UtcNow.AddMinutes(10));
                FakeKeychain keychain = new FakeKeychain();
                List<string> requests = new List<string>();
                FakeHttpHandler handler = new FakeHttpHandler(request =>
                {
                    if (request.RequestUri == _TokenEndpoint)
                    {
                        requests.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                        return Json(HttpStatusCode.OK, _Renewed);
                    }
                    return request.Headers.Authorization?.Parameter == "renewed-access" ? Json(HttpStatusCode.OK, "{\"five_hour\":{\"utilization\":10,\"resets_at\":null}}") : Json(HttpStatusCode.Unauthorized, "{}");
                });
                DateTimeOffset started = DateTimeOffset.UtcNow;
                UsageReport report = await ReadAsync(Service(fixture, handler, keychain), profile.Id, false, cancellationToken).ConfigureAwait(false);
                Fixture.Check(report.Status == UsageStatus.Available && report.Windows.Count == 1, "Usage was not read with the renewed login: " + report.Message);
                JsonNode sent = JsonNode.Parse(requests.Single())!;
                Fixture.Check(sent["grant_type"]!.GetValue<string>() == "refresh_token" && sent["refresh_token"]!.GetValue<string>() == "synthetic-refresh"
                    && sent["client_id"]!.GetValue<string>() == "9d1c250a-e61b-44d9-88ed-5944d1962f5e" && sent["scope"]!.GetValue<string>() == "user:inference user:profile", "The refresh request is wrong.");
                JsonNode oauth = JsonNode.Parse(Saved(fixture, profile))!["claudeAiOauth"]!;
                long expires = oauth["expiresAt"]!.GetValue<long>();
                Fixture.Check(oauth["accessToken"]!.GetValue<string>() == "renewed-access" && oauth["refreshToken"]!.GetValue<string>() == "renewed-refresh", "The rotated tokens were not saved.");
                Fixture.Check(expires >= started.AddHours(8).ToUnixTimeMilliseconds() && expires <= DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeMilliseconds() && oauth["refreshTokenExpiresAt"] != null, "The new expiry was not saved.");
                Fixture.Check(oauth["subscriptionType"]!.GetValue<string>() == "max" && oauth["rateLimitTier"]!.GetValue<string>() == "default_claude_max_20x", "Renewal dropped other login fields.");
                string service = MacCredentialBridge.ServiceName(fixture.Store.ProfileDirectory(profile.Id));
                Fixture.Check(keychain.Writes == 1 && keychain.Items[service] == Saved(fixture, profile), "The renewed login was not saved to the Keychain.");
                await ReadAsync(Service(fixture, handler, keychain), profile.Id, false, cancellationToken).ConfigureAwait(false);
                Fixture.Check(requests.Count == 1, "A fresh login was renewed again.");
            });
            Add("ClaudeRenewalLeavesProfileInUseAlone", async (fixture, cancellationToken) =>
            {
                Profile profile = ClaudeProfile(fixture, DateTimeOffset.UtcNow.AddMinutes(-5));
                string before = Saved(fixture, profile);
                FakeHttpHandler handler = new FakeHttpHandler(_ => throw new InvalidOperationException("Network must not be used."));
                using (FileStream session = fixture.Store.AcquireSessionLease(profile.Id))
                {
                    UsageReport report = await ReadAsync(Service(fixture, handler), profile.Id, false, cancellationToken).ConfigureAwait(false);
                    Fixture.Check(report.Status == UsageStatus.Expired && report.Message.Contains("session", StringComparison.Ordinal), "A profile in use should be left to its session: " + report.Message);
                }
                Fixture.Check(handler.Calls == 0 && Saved(fixture, profile) == before, "Kameo renewed a login while a session was using it.");
            });
            Add("ClaudeRejectedRenewalAsksToSignIn", async (fixture, cancellationToken) =>
            {
                Profile profile = ClaudeProfile(fixture, DateTimeOffset.UtcNow.AddMinutes(-5));
                string before = Saved(fixture, profile);
                FakeHttpHandler handler = new FakeHttpHandler(_ => Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\",\"error_description\":\"synthetic-refresh revoked\"}"));
                UsageReport report = await ReadAsync(Service(fixture, handler), profile.Id, false, cancellationToken).ConfigureAwait(false);
                Fixture.Check(report.Status == UsageStatus.Expired && report.Message.Contains("Sign in again", StringComparison.Ordinal) && !report.Message.Contains("synthetic", StringComparison.Ordinal), "A revoked login should ask to sign in: " + report.Message);
                Fixture.Check(handler.Calls == 1 && Saved(fixture, profile) == before, "A rejected renewal changed the login or checked usage.");
            });
            Add("ClaudeMalformedRenewalKeepsLogin", async (fixture, cancellationToken) =>
            {
                Profile profile = ClaudeProfile(fixture, DateTimeOffset.UtcNow.AddMinutes(-5));
                string before = Saved(fixture, profile);
                foreach (string body in new[] { "{\"access_token\":\"\",\"expires_in\":28800}", "{\"access_token\":\"renewed-access\"}", "not json", "[]" })
                {
                    FakeKeychain keychain = new FakeKeychain();
                    UsageReport report = await ReadAsync(Service(fixture, new FakeHttpHandler(_ => Json(HttpStatusCode.OK, body)), keychain), profile.Id, false, cancellationToken).ConfigureAwait(false);
                    Fixture.Check(report.Status == UsageStatus.Expired && Saved(fixture, profile) == before && keychain.Writes == 0, "A malformed renewal response changed the login: " + body);
                }
            });
            Add("ClaudeUnauthorizedLoginIsRenewedOnce", async (fixture, cancellationToken) =>
            {
                Profile profile = ClaudeProfile(fixture, DateTimeOffset.UtcNow.AddHours(4));
                int renewals = 0;
                int checks = 0;
                bool acceptRenewed = true;
                FakeHttpHandler handler = new FakeHttpHandler(request =>
                {
                    // Each renewal issues a new access token, so a rejected renewal is distinguishable from a stale one.
                    if (request.RequestUri == _TokenEndpoint) { renewals++; return Json(HttpStatusCode.OK, renewals == 1 ? _Renewed : _Renewed.Replace("renewed-access", "second-access", StringComparison.Ordinal)); }
                    checks++;
                    return acceptRenewed && request.Headers.Authorization?.Parameter == "renewed-access" ? Json(HttpStatusCode.OK, "{\"seven_day\":{\"utilization\":5,\"resets_at\":null}}") : Json(HttpStatusCode.Unauthorized, "{}");
                });
                UsageReport report = await ReadAsync(Service(fixture, handler), profile.Id, false, cancellationToken).ConfigureAwait(false);
                Fixture.Check(report.Status == UsageStatus.Available && renewals == 1 && checks == 2, "A rejected login should be renewed and checked again once: " + report.Message);
                acceptRenewed = false;
                UsageReport rejected = await ReadAsync(Service(fixture, handler), profile.Id, false, cancellationToken).ConfigureAwait(false);
                Fixture.Check(rejected.Status == UsageStatus.Expired && renewals == 2 && checks == 4, "A login rejected after renewal must stop after one retry.");
            });
            Add("ClaudeRenewalUsesNewestKeychainLogin", async (fixture, cancellationToken) =>
            {
                Profile profile = ClaudeProfile(fixture, DateTimeOffset.UtcNow.AddHours(-1));
                string directory = fixture.Store.ProfileDirectory(profile.Id);
                FakeKeychain keychain = new FakeKeychain();
                keychain.Items[MacCredentialBridge.ServiceName(directory)] = "{\"claudeAiOauth\":{\"accessToken\":\"keychain-access\",\"refreshToken\":\"keychain-refresh\",\"expiresAt\":"
                    + DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds() + ",\"clientId\":\"synthetic-client\"}}";
                List<string> requests = new List<string>();
                FakeHttpHandler handler = new FakeHttpHandler(request =>
                {
                    if (request.RequestUri != _TokenEndpoint) return Json(HttpStatusCode.OK, "{}");
                    requests.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                    return Json(HttpStatusCode.OK, _Renewed);
                });
                await ReadAsync(Service(fixture, handler, keychain), profile.Id, false, cancellationToken).ConfigureAwait(false);
                JsonNode sent = JsonNode.Parse(requests.Single())!;
                Fixture.Check(sent["refresh_token"]!.GetValue<string>() == "keychain-refresh" && sent["client_id"]!.GetValue<string>() == "synthetic-client" && sent["scope"] == null, "Renewal did not use the newest Keychain login and its client.");
                Fixture.Check(keychain.Items[MacCredentialBridge.ServiceName(directory)] == Saved(fixture, profile) && Saved(fixture, profile).Contains("renewed-refresh", StringComparison.Ordinal), "The Keychain and file disagree after renewal.");
            });
            Add("ClaudeRenewalWithLockedKeychainChangesNothing", async (fixture, cancellationToken) =>
            {
                Profile profile = ClaudeProfile(fixture, DateTimeOffset.UtcNow.AddMinutes(-5));
                string before = Saved(fixture, profile);
                FakeKeychain keychain = new FakeKeychain { Locked = true };
                FakeHttpHandler handler = new FakeHttpHandler(_ => throw new InvalidOperationException("Network must not be used."));
                UsageReport report = await ReadAsync(Service(fixture, handler, keychain), profile.Id, false, cancellationToken).ConfigureAwait(false);
                Fixture.Check(report.Status == UsageStatus.Expired && handler.Calls == 0 && Saved(fixture, profile) == before && keychain.Writes == 0, "An unreadable Keychain must stop renewal, since it may hold a newer login.");
            });
            Add("ClaudeConcurrentChecksRenewOnce", async (fixture, cancellationToken) =>
            {
                Profile profile = ClaudeProfile(fixture, DateTimeOffset.UtcNow.AddMinutes(10));
                int renewals = 0;
                FakeHttpHandler handler = new FakeHttpHandler(request =>
                {
                    if (request.RequestUri != _TokenEndpoint) return Json(HttpStatusCode.OK, "{\"five_hour\":{\"utilization\":1,\"resets_at\":null}}");
                    Interlocked.Increment(ref renewals);
                    Thread.Sleep(200);
                    return Json(HttpStatusCode.OK, _Renewed);
                });
                UsageReport[] reports = await Task.WhenAll(ReadAsync(Service(fixture, handler), profile.Id, false, cancellationToken), ReadAsync(Service(fixture, handler), profile.Id, false, cancellationToken)).ConfigureAwait(false);
                Fixture.Check(renewals == 1 && reports.All(report => report.Status == UsageStatus.Available), "Concurrent checks renewed twice, which would invalidate the first rotated token.");
            });
            Add("ClaudeRejectedLoginIsExpired", async (fixture, cancellationToken) =>
            {
                Profile profile = ClaudeProfile(fixture, DateTimeOffset.UtcNow.AddHours(4));
                UsageReport report = await ReadAsync(Service(fixture, new FakeHttpHandler(_ => Json(HttpStatusCode.Unauthorized, "{\"error\":\"synthetic-access\"}"))), profile.Id, false, cancellationToken).ConfigureAwait(false);
                Fixture.Check(report.Status == UsageStatus.Expired && !report.Message.Contains("synthetic", StringComparison.Ordinal), "A 401 should ask for a refresh without echoing the response.");
            });
            Add("ClaudeFailuresAreReportedSafely", async (fixture, cancellationToken) =>
            {
                Profile profile = ClaudeProfile(fixture, DateTimeOffset.UtcNow.AddHours(4));
                foreach (Func<HttpRequestMessage, HttpResponseMessage> respond in new Func<HttpRequestMessage, HttpResponseMessage>[]
                {
                    _ => Json(HttpStatusCode.InternalServerError, "synthetic-access"),
                    _ => Json(HttpStatusCode.OK, "{not json synthetic-access"),
                    _ => Json(HttpStatusCode.OK, "[1,2]"),
                    _ => Json(HttpStatusCode.TooManyRequests, "{}"),
                    _ => Json(HttpStatusCode.Forbidden, "{}"),
                    _ => Json(HttpStatusCode.OK, new string(' ', 1024 * 1024 + 1)),
                    _ => throw new HttpRequestException("synthetic-access network failure")
                })
                {
                    UsageReport report = await ReadAsync(Service(fixture, new FakeHttpHandler(respond)), profile.Id, false, cancellationToken).ConfigureAwait(false);
                    Fixture.Check(report.Status == UsageStatus.Unavailable && report.Windows.Count == 0 && report.Message.Length > 0, "A failed check was not reported as unavailable.");
                    Fixture.Check(!report.Message.Contains("synthetic", StringComparison.Ordinal), "A failure message leaked response or credential text.");
                }
            });
            Add("ClaudeOfflineAndSignedOut", async (fixture, cancellationToken) =>
            {
                FakeHttpHandler handler = new FakeHttpHandler(_ => throw new InvalidOperationException("Network must not be used."));
                Profile signedOut = fixture.Add(HarnessKind.Claude, "Signed out", CredentialKind.Native);
                Fixture.Check((await ReadAsync(Service(fixture, handler), signedOut.Id, false, cancellationToken).ConfigureAwait(false)).Status == UsageStatus.SignedOut, "Missing logins should be signed out.");
                Profile profile = ClaudeProfile(fixture, DateTimeOffset.UtcNow.AddHours(4));
                UsageReport offline = await ReadAsync(Service(fixture, handler), profile.Id, true, cancellationToken).ConfigureAwait(false);
                Fixture.Check(offline.Status == UsageStatus.NotChecked && handler.Calls == 0, "Offline checks must not use the network.");
            });
            Add("ApiKeyAndOtherHarnessesNotApplicable", async (fixture, cancellationToken) =>
            {
                FakeHttpHandler handler = new FakeHttpHandler(_ => throw new InvalidOperationException("Network must not be used."));
                using UsageService service = Service(fixture, handler);
                foreach (Profile profile in new[] { fixture.Add(HarnessKind.Claude, "Claude key"), fixture.Add(HarnessKind.Codex, "Codex key"), fixture.Add(HarnessKind.Antigravity, "Google", CredentialKind.Native), fixture.Add(HarnessKind.Mux, "Mux", CredentialKind.Native) })
                {
                    UsageReport report = await service.ReadAsync(profile.Id, false, cancellationToken).ConfigureAwait(false);
                    Fixture.Check(report.Status == UsageStatus.NotApplicable && !UsageService.Supports(profile), profile.Name + " should not report plan usage.");
                }
                Fixture.Check(handler.Calls == 0, "Not-applicable profiles used the network.");
                await Fixture.ThrowsAsync<KeyNotFoundException>(() => service.ReadAsync("prf_missing0000", false, cancellationToken)).ConfigureAwait(false);
            });
            Add("CodexSessionLogUsesNewestSnapshot", async (fixture, cancellationToken) =>
            {
                Profile profile = CodexProfile(fixture);
                string directory = fixture.Store.ProfileDirectory(profile.Id);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                long future = now.AddHours(3).ToUnixTimeSeconds();
                long weekly = now.AddDays(3).ToUnixTimeSeconds();
                WriteLog(directory, "2026/11/01/rollout-old.jsonl", now.AddDays(-2), LogLine(now.AddDays(-2), "{\"used_percent\":80.0,\"window_minutes\":300,\"resets_at\":" + future + "}", "null", "plus"));
                WriteLog(directory, "2026/11/02/rollout-new.jsonl", now.AddMinutes(-10),
                    LogLine(now.AddMinutes(-20), "{\"used_percent\":5.0,\"window_minutes\":300,\"resets_at\":" + future + "}", "null", "pro"),
                    LogLine(now.AddMinutes(-15), "{\"used_percent\":12.0,\"window_minutes\":300,\"resets_at\":" + future + "}", "{\"used_percent\":30.0,\"window_minutes\":10080,\"resets_at\":" + weekly + "}", "pro"),
                    "{\"timestamp\":\"" + now.AddMinutes(-11).ToString("O", CultureInfo.InvariantCulture) + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":null,\"rate_limits\":null}}",
                    "{\"timestamp\":\"" + now.AddMinutes(-10).ToString("O", CultureInfo.InvariantCulture) + "\",\"type\":\"response_item\",\"payload\":{\"text\":\"partial");
                UsageReport report = await ReadAsync(new UsageService(fixture.Store), profile.Id, true, cancellationToken).ConfigureAwait(false);
                Fixture.Check(report.Status == UsageStatus.Available && report.Source == UsageSource.SessionLog, "Session log usage missing: " + report.Message);
                Fixture.Check(string.Join("|", report.Windows.Select(window => window.Label + "=" + window.UsedPercent.ToString(CultureInfo.InvariantCulture))) == "5-hour=12|Weekly=30", "Wrong snapshot: " + string.Join("|", report.Windows.Select(window => window.Label + "=" + window.UsedPercent)));
                Fixture.Check(report.Plan == "Pro" && report.Detail == "Credits 2,500" && report.ObservedUtc != null && Math.Abs((report.ObservedUtc.Value - now.AddMinutes(-15)).TotalSeconds) < 1, "Plan, credits or observation time missing.");
                Fixture.Check(report.Windows[1].ResetsAt == DateTimeOffset.FromUnixTimeSeconds(weekly), "Reset time missing.");
            });
            Add("CodexSessionLogResetWindowsAndLegacyFields", async (fixture, cancellationToken) =>
            {
                Profile profile = CodexProfile(fixture);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                DateTimeOffset observed = now.AddHours(-1);
                WriteLog(fixture.Store.ProfileDirectory(profile.Id), "rollout-legacy.jsonl", observed,
                    LogLine(observed, "{\"used_percent\":90.0,\"window_minutes\":300,\"resets_at\":" + now.AddMinutes(-5).ToUnixTimeSeconds() + "}", "{\"used_percent\":40.0,\"window_minutes\":10080,\"resets_in_seconds\":7200}", "plus"));
                UsageReport report = await ReadAsync(new UsageService(fixture.Store), profile.Id, true, cancellationToken).ConfigureAwait(false);
                Fixture.Check(report.Windows.Count == 2 && report.Windows[0].UsedPercent == 0 && report.Windows[0].ResetsAt == null, "A window that reset after the session should show no use.");
                Fixture.Check(report.Windows[1].UsedPercent == 40 && report.Windows[1].ResetsAt == observed.AddSeconds(7200), "Relative reset times should be anchored to the log timestamp.");
            });
            Add("CodexOfflineWithoutLogsAndSignedOut", async (fixture, cancellationToken) =>
            {
                Profile signedOut = fixture.Add(HarnessKind.Codex, "Signed out", CredentialKind.Native);
                Fixture.Check((await ReadAsync(new UsageService(fixture.Store), signedOut.Id, true, cancellationToken).ConfigureAwait(false)).Status == UsageStatus.SignedOut, "Missing Codex login should be signed out.");
                Profile profile = CodexProfile(fixture);
                Fixture.Check((await ReadAsync(new UsageService(fixture.Store), profile.Id, true, cancellationToken).ConfigureAwait(false)).Status == UsageStatus.NotChecked, "No logs offline should be not checked.");
            });
            Add("CodexLiveUsesIsolatedAppServer", async (fixture, cancellationToken) =>
            {
                Profile profile = CodexProfile(fixture, Fixture.Probe());
                string directory = fixture.Store.ProfileDirectory(profile.Id);
                long reset = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
                string codex = "{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":42,\"windowDurationMins\":300,\"resetsAt\":" + reset + "},\"secondary\":{\"usedPercent\":18,\"windowDurationMins\":10080,\"resetsAt\":" + reset + "},\"credits\":{\"hasCredits\":true,\"unlimited\":false,\"balance\":\"1250\"},\"planType\":\"plus\"}";
                File.WriteAllText(Path.Combine(directory, "fixture-rate-limits.json"), "{\"rateLimits\":" + codex + ",\"rateLimitsByLimitId\":{\"codex_spark\":{\"limitId\":\"codex_spark\",\"limitName\":\"Spark\",\"primary\":{\"usedPercent\":5,\"windowDurationMins\":10080,\"resetsAt\":" + reset + "}},\"codex\":" + codex + "}}");
                UsageReport report = await ReadAsync(new UsageService(fixture.Store), profile.Id, false, cancellationToken).ConfigureAwait(false);
                Fixture.Check(report.Status == UsageStatus.Available && report.Source == UsageSource.Live, "Live Codex usage failed: " + report.Message);
                Fixture.Check(string.Join("|", report.Windows.Select(window => window.Label + "=" + window.UsedPercent.ToString(CultureInfo.InvariantCulture))) == "5-hour=42|Weekly=18|Spark Weekly=5", "Wrong live windows: " + string.Join("|", report.Windows.Select(window => window.Label)));
                Fixture.Check(report.Plan == "Plus" && report.Detail == "Credits 1,250" && report.Windows[0].ResetsAt == DateTimeOffset.FromUnixTimeSeconds(reset), "Live plan, credits or reset missing.");
                JsonNode capture = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "app-server-capture.json")))!;
                string arguments = string.Join(" ", capture["arguments"]!.AsArray().Select(node => node!.GetValue<string>()));
                Fixture.Check(arguments == "-c cli_auth_credentials_store=\"file\" app-server", "App-server arguments are wrong: " + arguments);
                Fixture.Check(string.Join(",", capture["methods"]!.AsArray().Select(node => node!.GetValue<string>())) == "initialize,initialized,account/rateLimits/read", "Protocol order is wrong.");
                Fixture.Check(Same(capture["codexHome"]!.GetValue<string>(), directory) && Same(capture["cwd"]!.GetValue<string>(), directory), "App-server was not isolated to the profile directory.");
                Fixture.Check(capture["params"]?["excludeResetCreditDetails"]?.GetValue<bool>() == true && capture["openAiKey"] == null, "Usage request parameters or environment are wrong.");
                using FileStream exclusive = fixture.Store.AcquireLease(profile.Id);
            });
            Add("CodexLiveErrorFallsBackToSessionLog", async (fixture, cancellationToken) =>
            {
                Profile profile = CodexProfile(fixture, Fixture.Probe());
                string directory = fixture.Store.ProfileDirectory(profile.Id);
                File.WriteAllText(Path.Combine(directory, "fixture-mode.txt"), "error");
                UsageReport failed = await ReadAsync(new UsageService(fixture.Store), profile.Id, false, cancellationToken).ConfigureAwait(false);
                Fixture.Check(failed.Status == UsageStatus.Unavailable && failed.Message.Contains("sign in", StringComparison.OrdinalIgnoreCase), "App-server errors should explain sign-in: " + failed.Message);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                WriteLog(directory, "rollout-a.jsonl", now, LogLine(now.AddMinutes(-1), "{\"used_percent\":7.0,\"window_minutes\":300,\"resets_at\":" + now.AddHours(2).ToUnixTimeSeconds() + "}", "null", "pro"));
                UsageReport fallback = await ReadAsync(new UsageService(fixture.Store), profile.Id, false, cancellationToken).ConfigureAwait(false);
                Fixture.Check(fallback.Status == UsageStatus.Available && fallback.Source == UsageSource.SessionLog && fallback.Message.StartsWith("Live check failed", StringComparison.Ordinal), "Live failures should fall back to the last session.");
                File.WriteAllText(Path.Combine(directory, "fixture-mode.txt"), "exit");
                UsageReport exited = await ReadAsync(new UsageService(fixture.Store), profile.Id, false, cancellationToken).ConfigureAwait(false);
                Fixture.Check(exited.Source == UsageSource.SessionLog && exited.Message.Contains("exited", StringComparison.Ordinal), "An app-server exit should fall back: " + exited.Message);
            });
            Add("CodexLiveTimeoutStopsAppServer", async (fixture, cancellationToken) =>
            {
                Profile profile = CodexProfile(fixture, Fixture.Probe());
                File.WriteAllText(Path.Combine(fixture.Store.ProfileDirectory(profile.Id), "fixture-mode.txt"), "hang");
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                UsageReport report = await ReadAsync(new UsageService(fixture.Store, null, null, null, TimeSpan.FromSeconds(2)), profile.Id, false, cancellationToken).ConfigureAwait(false);
                Fixture.Check(report.Status == UsageStatus.Unavailable && report.Message.Contains("in time", StringComparison.Ordinal) && watch.Elapsed < TimeSpan.FromSeconds(15), "A hung app-server was not stopped: " + report.Message);
                using FileStream exclusive = fixture.Store.AcquireLease(profile.Id);
            });
            Add("CodexBusyAndMissingExecutable", async (fixture, cancellationToken) =>
            {
                Profile profile = CodexProfile(fixture, Path.Combine(fixture.Root, "missing-codex"));
                UsageReport missing = await ReadAsync(new UsageService(fixture.Store), profile.Id, false, cancellationToken).ConfigureAwait(false);
                Fixture.Check(missing.Status == UsageStatus.Unavailable && missing.Message.Contains("not found", StringComparison.Ordinal), "A missing executable should be explained: " + missing.Message);
                using FileStream exclusive = fixture.Store.AcquireLease(profile.Id);
                UsageReport busy = await ReadAsync(new UsageService(fixture.Store), profile.Id, false, cancellationToken).ConfigureAwait(false);
                Fixture.Check(busy.Status == UsageStatus.InUse, "An exclusive sign-in or edit should block live checks.");
            });
            Add("ReadAllFiltersAndKeepsOrder", async (fixture, cancellationToken) =>
            {
                fixture.Add(HarnessKind.Codex, "B codex", CredentialKind.Native);
                fixture.Add(HarnessKind.Claude, "A claude", CredentialKind.Native);
                fixture.Add(HarnessKind.Codex, "A codex key");
                fixture.Add(HarnessKind.Mux, "Mux", CredentialKind.Native);
                using UsageService service = new UsageService(fixture.Store);
                IReadOnlyList<UsageReport> codex = await service.ReadAllAsync(HarnessKind.Codex, true, cancellationToken).ConfigureAwait(false);
                Fixture.Check(string.Join("|", codex.Select(report => report.ProfileName)) == "A codex key|B codex", "Harness filter or order is wrong.");
                IReadOnlyList<UsageReport> all = await service.ReadAllAsync(null, true, cancellationToken).ConfigureAwait(false);
                Fixture.Check(string.Join("|", all.Select(report => report.ProfileName)) == "A claude|A codex key|B codex", "All-harness usage should exclude unsupported harnesses.");
            });
            AddSync("LeastUsedPicksLowestTightestLimit", fixture =>
            {
                UsageReport Make(string id, UsageStatus status, params double[] used) => new UsageReport
                {
                    ProfileId = id, Status = status, Windows = used.Select((value, index) => new UsageWindow("W" + index, value, null, null)).ToArray()
                };
                UsageReport busy = Make("busy", UsageStatus.Available, 5, 90);
                UsageReport roomy = Make("roomy", UsageStatus.Available, 70, 20);
                UsageReport tie = Make("tie", UsageStatus.Available, 70);
                Fixture.Check(busy.PeakPercent == 90 && Make("none", UsageStatus.Available).PeakPercent == 0, "Peak should be the tightest window.");
                Fixture.Check(UsageService.LeastUsed(new[] { busy, roomy, tie })?.ProfileId == "roomy", "The login with the lowest tightest limit should win, ties keep the first.");
                Fixture.Check(UsageService.LeastUsed(new[] { Make("expired", UsageStatus.Expired), Make("empty", UsageStatus.Available), busy })?.ProfileId == "busy", "Unavailable or empty reports should be ignored.");
                Fixture.Check(UsageService.LeastUsed(new[] { Make("expired", UsageStatus.Expired) }) == null && UsageService.LeastUsed(Array.Empty<UsageReport>()) == null, "No usable report should return null.");
            });
            AddSync("FormattingIsStable", fixture =>
            {
                Fixture.Check(UsageFormat.WindowLabel(300) == "5-hour" && UsageFormat.WindowLabel(10080) == "Weekly" && UsageFormat.WindowLabel(1440) == "Daily"
                    && UsageFormat.WindowLabel(43200) == "Monthly" && UsageFormat.WindowLabel(2880) == "2-day" && UsageFormat.WindowLabel(120) == "2-hour"
                    && UsageFormat.WindowLabel(45) == "45-minute" && UsageFormat.WindowLabel(null) == "Limit", "Window labels changed.");
                Fixture.Check(UsageFormat.Percent(42.5) == "43%" && UsageFormat.Percent(0) == "0%" && new UsageWindow("x", double.NaN, null, null).UsedPercent == 0, "Percent formatting changed.");
                DateTimeOffset now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
                Fixture.Check(UsageFormat.Reset(null, now) == "" && UsageFormat.Reset(now.AddMinutes(-1), now) == "reset" && UsageFormat.Reset(now.AddSeconds(30), now) == "resets in 1m"
                    && UsageFormat.Reset(now.AddMinutes(135), now) == "resets in 2h 15m" && UsageFormat.Reset(now.AddDays(3), now).StartsWith("resets ", StringComparison.Ordinal)
                    && !UsageFormat.Reset(now.AddDays(3), now).Contains(" in ", StringComparison.Ordinal), "Reset text changed.");
                UsageReport live = new UsageReport { Source = UsageSource.Live, ObservedUtc = now };
                UsageReport logged = new UsageReport { Source = UsageSource.SessionLog, ObservedUtc = now.AddDays(-3) };
                Fixture.Check(UsageFormat.Observed(live, now).StartsWith("Live, ", StringComparison.Ordinal) && UsageFormat.Observed(logged, now).StartsWith("Last session, ", StringComparison.Ordinal)
                    && UsageFormat.Observed(new UsageReport(), now) == "", "Observation text changed.");
            });
            Add("CliReportsUsage", async (fixture, cancellationToken) =>
            {
                Profile profile = CodexProfile(fixture);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                WriteLog(fixture.Store.ProfileDirectory(profile.Id), "rollout-cli.jsonl", now, LogLine(now.AddMinutes(-1), "{\"used_percent\":64.0,\"window_minutes\":300,\"resets_at\":" + now.AddHours(2).ToUnixTimeSeconds() + "}", "null", "pro"));
                fixture.Add(HarnessKind.Codex, "Key profile");
                CliResult text = await CliCases.RunAsync(fixture, new[] { "usage", "codex", "--offline" }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(text.ExitCode == 0 && text.Output.Contains("Codex  Work  (Pro)", StringComparison.Ordinal) && text.Output.Contains("64%", StringComparison.Ordinal)
                    && !text.Output.Contains("Key profile", StringComparison.Ordinal) && !text.Output.Contains("synthetic", StringComparison.Ordinal), "CLI text usage is wrong: " + text.Output + text.Error);
                CliResult json = await CliCases.RunAsync(fixture, new[] { "usage", "--offline", "--json" }, null, cancellationToken).ConfigureAwait(false);
                JsonArray reports = JsonNode.Parse(json.Output)!.AsArray();
                Fixture.Check(json.ExitCode == 0 && reports.Count == 2 && reports.Any(report => report!["status"]!.GetValue<string>() == "notApplicable")
                    && reports.Any(report => report!["source"]!.GetValue<string>() == "sessionLog" && report["windows"]![0]!["usedPercent"]!.GetValue<double>() == 64), "CLI JSON usage is wrong: " + json.Output);
                CliResult single = await CliCases.RunAsync(fixture, new[] { "usage", profile.Id, "--offline" }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(single.ExitCode == 0 && single.Output.Contains("64%", StringComparison.Ordinal), "CLI single-profile usage failed.");
                CliResult mux = await CliCases.RunAsync(fixture, new[] { "usage", "mux" }, null, cancellationToken).ConfigureAwait(false);
                CliResult flag = await CliCases.RunAsync(fixture, new[] { "usage", "--bogus" }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(mux.ExitCode != 0 && flag.ExitCode != 0, "Unsupported usage targets should fail.");
            });
            return new TestSuiteDescriptor("Usage", "Claude Code and Codex plan usage without contacting providers", cases);
        }

        private static async Task<UsageReport> ReadAsync(UsageService service, string id, bool offline, CancellationToken cancellationToken)
        {
            using (service) return await service.ReadAsync(id, offline, cancellationToken).ConfigureAwait(false);
        }

        private static UsageService Service(Fixture fixture, FakeHttpHandler handler, FakeKeychain? keychain = null) =>
            new UsageService(fixture.Store, handler, _Endpoint, null, null, _TokenEndpoint, keychain ?? new FakeKeychain());

        private static string Saved(Fixture fixture, Profile profile) => File.ReadAllText(Path.Combine(fixture.Store.ProfileDirectory(profile.Id), ".credentials.json"));

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private static Profile ClaudeProfile(Fixture fixture, DateTimeOffset expires)
        {
            Profile profile = fixture.Add(HarnessKind.Claude, "Claude " + Guid.NewGuid().ToString("N")[..6], CredentialKind.Native);
            PrivateFiles.Write(Path.Combine(fixture.Store.ProfileDirectory(profile.Id), ".credentials.json"),
                "{\"claudeAiOauth\":{\"accessToken\":\"synthetic-access\",\"refreshToken\":\"synthetic-refresh\",\"expiresAt\":" + expires.ToUnixTimeMilliseconds()
                + ",\"scopes\":[\"user:inference\",\"user:profile\"],\"subscriptionType\":\"max\",\"rateLimitTier\":\"default_claude_max_20x\"}}");
            return profile;
        }

        private static Profile CodexProfile(Fixture fixture, string executable = "")
        {
            Profile profile = fixture.Store.Create(new Profile { Harness = HarnessKind.Codex, Name = "Work", Kind = CredentialKind.Native, Executable = executable });
            new CredentialFiles(fixture.Store).ImportText(profile.Id, CredentialCases.Document(HarnessKind.Codex));
            return profile;
        }

        private static string LogLine(DateTimeOffset timestamp, string primary, string secondary, string plan) =>
            "{\"timestamp\":\"" + timestamp.ToString("O", CultureInfo.InvariantCulture) + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":1}},"
            + "\"rate_limits\":{\"limit_id\":\"codex\",\"limit_name\":null,\"primary\":" + primary + ",\"secondary\":" + secondary
            + ",\"credits\":{\"has_credits\":true,\"unlimited\":false,\"balance\":\"2500.0000000000\"},\"plan_type\":\"" + plan + "\",\"rate_limit_reached_type\":null}}}";

        private static void WriteLog(string directory, string relative, DateTimeOffset modified, params string[] lines)
        {
            string path = Path.Combine(directory, "sessions", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Join("\n", lines) + "\n");
            File.SetLastWriteTimeUtc(path, modified.UtcDateTime);
        }

        private static bool Same(string first, string second) => string.Equals(Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
