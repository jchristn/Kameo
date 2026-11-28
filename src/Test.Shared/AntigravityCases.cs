namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Kameo.Core;
    using Touchstone.Core;

    internal static class AntigravityCases
    {
        internal static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string name, Action<Fixture> execute) => cases.Add(KameoSuites.Case("Antigravity", name, execute));
            void AddAsync(string name, Func<Fixture, CancellationToken, Task> execute) => cases.Add(KameoSuites.Case("Antigravity", name, execute));
            Add("AccountDirectoriesAreIndependent", fixture =>
            {
                Profile first = Account(fixture); Profile second = Account(fixture, "Personal");
                ProcessStartInfo a = new LaunchService(fixture.Store).Prepare(first, Array.Empty<string>(), true).StartInfo;
                ProcessStartInfo b = new LaunchService(fixture.Store).Prepare(second, Array.Empty<string>()).StartInfo;
                Fixture.Check(a.ArgumentList[1] != b.ArgumentList[1] && a.ArgumentList[1] == fixture.Store.ProfileDirectory(first.Id), "Accounts share a directory.");
                Fixture.Check(a.ArgumentList[3] == "{\"modelProvider\":\"\"}" && b.ArgumentList[3] == a.ArgumentList[3], "Account mode was not enforced on login and launch.");
                Fixture.Check(!a.Environment.ContainsKey("GEMINI_API_KEY") && !b.Environment.ContainsKey("GOOGLE_API_KEY"), "An API key overrode account authentication.");
                Fixture.Check(!string.IsNullOrWhiteSpace(a.Environment["SSH_CONNECTION"]) && a.Environment["AGY_CLI_DISABLE_AUTO_UPDATE"] == "true", "File storage or the update guard is missing.");
            });
            Add("ParentEnvironmentAndApiModeAreUnchanged", fixture =>
            {
                string? ssh = Environment.GetEnvironmentVariable("SSH_CONNECTION");
                string? update = Environment.GetEnvironmentVariable("AGY_CLI_DISABLE_AUTO_UPDATE");
                new LaunchService(fixture.Store).Prepare(Account(fixture), Array.Empty<string>());
                ProcessStartInfo api = new LaunchService(fixture.Store).Prepare(fixture.Add(HarnessKind.Antigravity, "API"), Array.Empty<string>()).StartInfo;
                Fixture.Check(Environment.GetEnvironmentVariable("SSH_CONNECTION") == ssh && Environment.GetEnvironmentVariable("AGY_CLI_DISABLE_AUTO_UPDATE") == update, "Parent environment changed.");
                api.Environment.TryGetValue("SSH_CONNECTION", out string? apiSsh);
                api.Environment.TryGetValue("AGY_CLI_DISABLE_AUTO_UPDATE", out string? apiUpdate);
                Fixture.Check(apiSsh == ssh && apiUpdate == update, "API launches acquired account-only overrides.");
            });
            Add("InheritedAccountAndGatewayOverridesAreRemoved", fixture =>
            {
                ProcessStartInfo start = new ProcessStartInfo("agy");
                foreach (string variable in new[] { "AGY_ACCOUNT", "AGY_ADC_AUTH", "AGY_CDE_ENTERPRISE_AUTH", "AGY_LLM_GATEWAY_API_KEY", "AGY_LLM_GATEWAY_AUTH_MODE", "ANTIGRAVITY_GEMINI_DIR", "GEMINI_API_KEY" })
                    start.Environment[variable] = "synthetic-override";
                Profile profile = Account(fixture);
                AntigravityAuthentication.Configure(start, profile, fixture.Store.ProfileDirectory(profile.Id));
                Fixture.Check(!start.Environment.Values.Contains("synthetic-override"), "Inherited authentication override survived.");
            });
            Add("ExistingSshConnectionIsPreserved", fixture =>
            {
                ProcessStartInfo start = new ProcessStartInfo("agy"); start.Environment["SSH_CONNECTION"] = "192.0.2.1 1234 192.0.2.2 22";
                Profile profile = Account(fixture); AntigravityAuthentication.Configure(start, profile, fixture.Store.ProfileDirectory(profile.Id));
                Fixture.Check(start.Environment["SSH_CONNECTION"] == "192.0.2.1 1234 192.0.2.2 22", "A real SSH connection was replaced.");
            });
            Add("SettingsKeepPreferencesAndRemoveApiProvider", fixture =>
            {
                Profile profile = Account(fixture); string path = Settings(fixture, profile);
                PrivateFiles.Write(path, "{\"modelProvider\":\"gemini\",\"model\":\"synthetic-model\",\"theme\":\"dark\"}");
                new LaunchService(fixture.Store).Prepare(profile, Array.Empty<string>());
                JsonObject settings = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                Fixture.Check(!settings.ContainsKey("modelProvider") && settings["model"]!.GetValue<string>() == "synthetic-model" && settings["theme"]!.GetValue<string>() == "dark", "Account setup lost preferences or retained API mode.");
            });
            Add("MalformedSettingsAreNotReplaced", fixture =>
            {
                Profile profile = Account(fixture); string path = Settings(fixture, profile); PrivateFiles.Write(path, "{invalid");
                Fixture.Throws<JsonException>(() => new LaunchService(fixture.Store).Prepare(profile, Array.Empty<string>()));
                Fixture.Check(File.ReadAllText(path) == "{invalid", "Malformed settings were silently discarded.");
                PrivateFiles.Write(path, "[]"); Fixture.Throws<InvalidDataException>(() => new LaunchService(fixture.Store).Prepare(profile, Array.Empty<string>()));
            });
            Add("TokenLinksAreRejected", fixture =>
            {
                Profile profile = Account(fixture); string path = Token(fixture, profile); PrivateFiles.DirectoryCreate(Path.GetDirectoryName(path)!);
                string source = Path.Combine(fixture.Root, "outside-token"); PrivateFiles.Write(source, CredentialCases.Document(HarnessKind.Antigravity));
                File.CreateSymbolicLink(path, source);
                Fixture.Throws<IOException>(() => new LaunchService(fixture.Store).Prepare(profile, Array.Empty<string>()));
                Fixture.Check(File.ReadAllText(source) == CredentialCases.Document(HarnessKind.Antigravity), "Token link target changed.");
            });
            Add("ArgumentOverridesAndUpdateCommandsAreRejected", fixture =>
            {
                Profile profile = Account(fixture);
                foreach (string argument in new[] { "--app_data_dir=outside", "--cli_dir_name=outside", "--config-rel-dir=outside", "--product=jetski", "--settings={}", "--bg-updater", "--release_base_url=https://example.invalid", "--remote-control", "remote-control", "update", "install" })
                    Fixture.Throws<ArgumentException>(() => new LaunchService(fixture.Store).Prepare(profile, new[] { argument }));
            });
            Add("ImportRetainsRenewableTokensAndMetadata", fixture =>
            {
                Profile profile = Account(fixture);
                JsonObject document = JsonNode.Parse(CredentialCases.Document(HarnessKind.Antigravity))!.AsObject();
                document["id_token_email"] = "synthetic@example.invalid"; document["user_tier"] = "test";
                string json = document.ToJsonString(); new CredentialFiles(fixture.Store).ImportText(profile.Id, json);
                Fixture.Check(fixture.Store.Get(profile.Id).HasNativeCredential && File.ReadAllText(Token(fixture, profile)) == json, "Imported tokens or metadata were lost.");
            });
            Add("InvalidAndLegacyTokensAreRejected", fixture =>
            {
                foreach (string json in new[] { "{\"refresh_token\":\"legacy\"}", "{\"auth_method\":\"consumer\",\"token\":{\"access_token\":\"test\"}}", "{\"auth_method\":\"unknown\",\"token\":{\"access_token\":\"test\",\"refresh_token\":\"test\"}}", "{\"auth_method\":123,\"token\":[]}" })
                    Fixture.Throws<InvalidDataException>(() => CredentialFiles.Validate(HarnessKind.Antigravity, json));
                CredentialFiles.Validate(HarnessKind.Antigravity, CredentialCases.Document(HarnessKind.Antigravity).Replace("consumer", "gcp", StringComparison.Ordinal));
            });
            AddAsync("SupportedCliReceivesAccountConfiguration", async (fixture, cancellationToken) =>
            {
                Profile profile = Account(fixture); string capture = Path.Combine(fixture.Root, "account.json");
                int exit = await new LaunchService(fixture.Store).RunAsync(profile.Id, new[] { "--capture", capture, "--fixture-token", "synthetic-refreshed", "--exit", "19" }, true, cancellationToken).ConfigureAwait(false);
                JsonNode data = JsonNode.Parse(File.ReadAllText(capture))!;
                Fixture.Check(exit == 19 && data["environment"]?["AGY_CLI_DISABLE_AUTO_UPDATE"]?.GetValue<string>() == "true", "Account launch configuration or exit code was lost.");
                Fixture.Check(data["environment"]?["GEMINI_API_KEY"] == null && File.ReadAllText(Token(fixture, profile)).Contains("synthetic-refreshed", StringComparison.Ordinal), "Account tokens were not retained.");
                if (!OperatingSystem.IsWindows()) Fixture.Check(File.GetUnixFileMode(Token(fixture, profile)) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "Token file permissions were not tightened.");
            });
            foreach (string version in new[] { "1.5.7", "1.5.8", "1.5.123", "1.6.0", "1.10.2", "2.0.0", "1.7", "agy version 1.6.3 (build abc123)\n" })
            {
                AddAsync("SupportedVersionStartsAccountSession_" + Label(version), async (fixture, cancellationToken) =>
                {
                    Profile profile = Account(fixture);
                    File.WriteAllText(Path.Combine(fixture.Root, "probe-version.txt"), version);
                    string capture = Path.Combine(fixture.Root, "later.json");
                    int exit = await new LaunchService(fixture.Store).RunAsync(profile.Id, new[] { "--capture", capture, "--fixture-token", "synthetic-refreshed", "--exit", "5" }, true, cancellationToken).ConfigureAwait(false);
                    Fixture.Check(exit == 5 && File.Exists(capture), "A supported version did not start an account session: " + version.Trim());
                    Fixture.Check(File.ReadAllText(Token(fixture, profile)).Contains("synthetic-refreshed", StringComparison.Ordinal), "Account tokens were not retained for a supported version.");
                });
            }
            foreach (string version in new[] { "1.5.6", "1.4.7", "0.9.99", "fail", "synthetic-secret-version-output", new string('x', 5000) })
            {
                AddAsync("UnsupportedVersionRejected_" + Label(version), async (fixture, cancellationToken) =>
                {
                    Profile profile = Account(fixture); new CredentialFiles(fixture.Store).ImportText(profile.Id, CredentialCases.Document(HarnessKind.Antigravity));
                    File.WriteAllText(Path.Combine(fixture.Root, "probe-version.txt"), version);
                    string capture = Path.Combine(fixture.Root, "must-not-start.json");
                    try { await new LaunchService(fixture.Store).RunAsync(profile.Id, new[] { "--capture", capture }, false, cancellationToken).ConfigureAwait(false); throw new TestFailureException("Unsupported CLI launched."); }
                    catch (InvalidOperationException error) { Fixture.Check(!error.Message.Contains("synthetic-secret-version-output", StringComparison.Ordinal), "Version probe output leaked."); }
                    Fixture.Check(!File.Exists(capture) && File.ReadAllText(Token(fixture, profile)) == CredentialCases.Document(HarnessKind.Antigravity), "A rejected version started or changed credentials.");
                    fixture.Store.Update(profile);
                });
            }
            AddAsync("VersionProbeCancellationReleasesLease", async (fixture, cancellationToken) =>
            {
                Profile profile = Account(fixture); File.WriteAllText(Path.Combine(fixture.Root, "probe-version.txt"), "hang");
                using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); cancellation.CancelAfter(200);
                try { await new LaunchService(fixture.Store).RunAsync(profile.Id, Array.Empty<string>(), false, cancellation.Token).ConfigureAwait(false); throw new TestFailureException("Version probe was not canceled."); }
                catch (OperationCanceledException) { }
                fixture.Store.Update(profile);
            });
            AddAsync("VersionProbeTimeoutIsActionable", async (fixture, cancellationToken) =>
            {
                Profile profile = Account(fixture); File.WriteAllText(Path.Combine(fixture.Root, "probe-version.txt"), "hang");
                try { await new LaunchService(fixture.Store).RunAsync(profile.Id, Array.Empty<string>(), false, cancellationToken).ConfigureAwait(false); throw new TestFailureException("Version probe did not time out."); }
                catch (InvalidOperationException error) { Fixture.Check(error.Message.Contains("timed out", StringComparison.Ordinal), "Timeout was not explained."); }
                fixture.Store.Update(profile);
            });
            AddAsync("ConcurrentAccountsKeepRefreshedTokensAfterCancellation", async (fixture, cancellationToken) =>
            {
                Profile first = Account(fixture); Profile second = Account(fixture, "Personal");
                string a = Path.Combine(fixture.Root, "a.json"); string b = Path.Combine(fixture.Root, "b.json");
                using CancellationTokenSource cancelA = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                using CancellationTokenSource cancelB = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task<int> runningA = new LaunchService(fixture.Store).RunAsync(first.Id, new[] { "--capture", a, "--fixture-token", "synthetic-a", "--sleep" }, false, cancelA.Token);
                Task<int> runningB = new LaunchService(fixture.Store).RunAsync(second.Id, new[] { "--capture", b, "--fixture-token", "synthetic-b", "--sleep" }, false, cancelB.Token);
                try
                {
                    for (int attempt = 0; attempt < 200 && (!File.Exists(Token(fixture, first)) || !File.Exists(Token(fixture, second))); attempt++) await Task.Delay(25, cancellationToken).ConfigureAwait(false);
                    Fixture.Check(File.Exists(a) && File.Exists(b), "Concurrent accounts did not start.");
                    Fixture.Throws<InvalidOperationException>(() => fixture.Store.Update(first)); Fixture.Throws<InvalidOperationException>(() => fixture.Store.Delete(second.Id));
                    cancelA.Cancel(); await Canceled(runningA).ConfigureAwait(false);
                    Fixture.Check(!runningB.IsCompleted, "Canceling one account stopped the other.");
                    fixture.Store.Update(first);
                }
                finally { cancelA.Cancel(); cancelB.Cancel(); await Canceled(runningA).ConfigureAwait(false); await Canceled(runningB).ConfigureAwait(false); }
                Fixture.Check(File.ReadAllText(Token(fixture, first)).Contains("synthetic-a", StringComparison.Ordinal) && File.ReadAllText(Token(fixture, second)).Contains("synthetic-b", StringComparison.Ordinal), "Refreshed credentials crossed accounts or were discarded.");
                new LaunchService(fixture.Store).Prepare(first, Array.Empty<string>());
                Fixture.Check(File.ReadAllText(Token(fixture, first)).Contains("synthetic-a", StringComparison.Ordinal), "Relaunch overwrote refreshed credentials.");
                fixture.Store.Delete(first.Id); Fixture.Check(File.Exists(Token(fixture, second)), "Deleting one account removed the other.");
            });
            return new TestSuiteDescriptor("Antigravity", "Google account isolation, compatibility and renewable credentials", cases);
        }

        private static Profile Account(Fixture fixture, string name = "Work")
        {
            Profile profile = fixture.Add(HarnessKind.Antigravity, name, CredentialKind.Native);
            profile.Executable = Fixture.Probe(); fixture.Store.Update(profile); return profile;
        }
        private static string Token(Fixture fixture, Profile profile) => Path.Combine(fixture.Store.ProfileDirectory(profile.Id), HarnessCatalog.CredentialFile(HarnessKind.Antigravity));
        private static string Settings(Fixture fixture, Profile profile) => Path.Combine(fixture.Store.ProfileDirectory(profile.Id), "antigravity-cli", "settings.json");
        private static string Label(string version)
        {
            if (version.Length > 40) return "Oversized";
            StringBuilder label = new StringBuilder();
            foreach (char character in version.Trim()) label.Append(char.IsLetterOrDigit(character) ? character : '_');
            return label.ToString();
        }

        private static async Task Canceled(Task<int> running)
        { try { await running.ConfigureAwait(false); } catch (OperationCanceledException) { } }
    }
}
