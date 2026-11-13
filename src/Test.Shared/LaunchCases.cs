namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Kameo.Core;
    using Touchstone.Core;

    internal static class LaunchCases
    {
        internal static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string name, Action<Fixture> execute) => cases.Add(KameoSuites.Case("Launch", name, execute));
            foreach (HarnessKind harness in HarnessCatalog.All)
            {
                Add("ApiEnvironment" + harness, fixture =>
                {
                    Profile profile = fixture.Add(harness);
                    if (harness == HarnessKind.Mux) new CredentialFiles(fixture.Store).ConfigureMux(profile.Id, "openai", "https://example.test", "test-model");
                    ProcessStartInfo start = new LaunchService(fixture.Store).Prepare(profile, new[] { "space value", "$(literal)", "a\"b", "" }).StartInfo;
                    Fixture.Check(!start.UseShellExecute, "Must not invoke a shell.");
                    Fixture.Check(start.ArgumentList.TakeLast(4).SequenceEqual(new[] { "space value", "$(literal)", "a\"b", "" }), "Arguments were reinterpreted.");
                    string variable = harness switch { HarnessKind.Claude => "ANTHROPIC_API_KEY", HarnessKind.Codex => "OPENAI_API_KEY", HarnessKind.Antigravity => "GEMINI_API_KEY", _ => "KAMEO_MUX_API_KEY" };
                    Fixture.Check(start.Environment[variable] == profile.ApiKey, "Selected key missing.");
                    Fixture.Check(!start.ArgumentList.Any(argument => argument.Contains(profile.ApiKey, StringComparison.Ordinal)), "Key appears in argv.");
                    Fixture.Check(start.Environment["KAMEO_PROFILE_ID"] == profile.Id, "Wrong profile identifier.");
                });
                if (HarnessCatalog.SupportsNativeLogin(harness)) Add("NativeLogin" + harness, fixture =>
                {
                    Profile profile = fixture.Add(harness, kind: CredentialKind.Native);
                    ProcessStartInfo start = new LaunchService(fixture.Store).Prepare(profile, Array.Empty<string>(), true).StartInfo;
                    string variable = harness switch { HarnessKind.Claude => "CLAUDE_CONFIG_DIR", HarnessKind.Codex => "CODEX_HOME", HarnessKind.Antigravity => "SSH_CONNECTION", _ => "MUX_CONFIG_DIR" };
                    Fixture.Check(harness == HarnessKind.Antigravity ? start.ArgumentList.Contains(fixture.Store.ProfileDirectory(profile.Id)) && !string.IsNullOrWhiteSpace(start.Environment[variable]) : start.Environment[variable] == fixture.Store.ProfileDirectory(profile.Id), "Native directory not isolated.");
                    if (harness == HarnessKind.Claude) Fixture.Check(start.ArgumentList.SequenceEqual(new[] { "auth", "login" }), "Claude login command incorrect.");
                    if (harness == HarnessKind.Codex) Fixture.Check(start.ArgumentList.Contains("login") && start.ArgumentList.Contains("cli_auth_credentials_store=\"file\""), "Codex file auth missing.");
                    if (harness == HarnessKind.Mux) Fixture.Check(start.ArgumentList.Count == 0, "Mux should open its interactive first-run wizard without submitting a prompt.");
                    Fixture.Check(!start.Environment.ContainsKey("OPENAI_API_KEY") && !start.Environment.ContainsKey("ANTHROPIC_API_KEY"), "Inherited API keys must not override native login.");
                });
            }
            Add("SwitchDoesNotMutateExistingPlan", fixture =>
            {
                Profile first = fixture.Add(); Profile second = fixture.Add(name: "Other");
                LaunchService service = new LaunchService(fixture.Store);
                LaunchPlan old = service.Prepare(first, Array.Empty<string>());
                fixture.Store.Select(second.Id);
                LaunchPlan next = service.Prepare(fixture.Store.Selected(HarnessKind.Codex), Array.Empty<string>());
                Fixture.Check(old.StartInfo.Environment["CODEX_HOME"] != next.StartInfo.Environment["CODEX_HOME"] && old.ProfileId == first.Id, "Old session changed with selection.");
            });
            Add("ParentEnvironmentUnchanged", fixture =>
            {
                string? before = Environment.GetEnvironmentVariable("CODEX_HOME");
                new LaunchService(fixture.Store).Prepare(fixture.Add(), Array.Empty<string>());
                Fixture.Check(Environment.GetEnvironmentVariable("CODEX_HOME") == before, "Launcher changed parent environment.");
            });
            Add("AntigravityDirectoryAndApiProvider", fixture =>
            {
                Profile profile = fixture.Add(HarnessKind.Antigravity);
                ProcessStartInfo start = new LaunchService(fixture.Store).Prepare(profile, Array.Empty<string>()).StartInfo;
                string directory = fixture.Store.ProfileDirectory(profile.Id);
                string path = Path.Combine(directory, "antigravity-cli", "settings.json");
                Fixture.Check(start.FileName == "agy", "Antigravity must launch agy, not the retired gemini command.");
                Fixture.Check(JsonNode.Parse(File.ReadAllText(path))?["modelProvider"]?.GetValue<string>() == "gemini", "The Gemini API provider was not enabled.");
                Fixture.Check(start.ArgumentList.SequenceEqual(new[] { "--gemini_dir", directory, "--settings", "{\"modelProvider\":\"gemini\"}" }), "Antigravity profile configuration is not enforced.");
                Fixture.Check(!start.Environment.ContainsKey("GEMINI_CLI_HOME"), "Legacy Gemini configuration leaked into Antigravity.");
            });
            Add("AntigravityCommandAliases", fixture =>
            {
                foreach (string name in new[] { "antigravity", "agy", "gemini" }) Fixture.Check(HarnessCatalog.Parse(name) == HarnessKind.Antigravity, "Antigravity alias was not recognized.");
                Fixture.Check(HarnessCatalog.Command(HarnessKind.Antigravity) == "antigravity", "Wrapper command should use the current product name.");
            });
            Add("AntigravityNativeCreationAllowed", fixture => Fixture.Check(fixture.Add(HarnessKind.Antigravity, kind: CredentialKind.Native).Kind == CredentialKind.Native, "Google account creation failed."));
            Add("AntigravityLegacyImportRejected", fixture => Fixture.Throws<InvalidDataException>(() => CredentialFiles.Validate(HarnessKind.Antigravity, "{\"refresh_token\":\"legacy\"}")));
            Add("AntigravityIsolationOverridesRejected", fixture =>
            {
                Profile profile = fixture.Add(HarnessKind.Antigravity);
                foreach (string argument in new[] { "--gemini_dir=elsewhere", "--gemini-dir", "-gemini_dir", "--settings={}", "--remote-agent", "--flagfile=outside" })
                    Fixture.Throws<ArgumentException>(() => new LaunchService(fixture.Store).Prepare(profile, new[] { argument }));
            });
            Add("CodexKeyRotationReplacesAuthFile", fixture =>
            {
                Profile profile = fixture.Add(); LaunchService service = new LaunchService(fixture.Store);
                service.Prepare(profile, Array.Empty<string>());
                profile.ApiKey = "rotated-synthetic-key"; fixture.Store.Update(profile); service.Prepare(fixture.Store.Get(profile.Id), Array.Empty<string>());
                Fixture.Check(JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.Store.ProfileDirectory(profile.Id), "auth.json")))?["OPENAI_API_KEY"]?.GetValue<string>() == profile.ApiKey, "Stale Codex key persisted.");
            });
            Add("MuxMissingConfigurationRejected", fixture => Fixture.Throws<InvalidOperationException>(() => new LaunchService(fixture.Store).Prepare(fixture.Add(HarnessKind.Mux), Array.Empty<string>())));
            Add("ApiLoginRejected", fixture => Fixture.Throws<InvalidOperationException>(() => new LaunchService(fixture.Store).Prepare(fixture.Add(), Array.Empty<string>(), true)));
            Add("MuxDirectoryOverrideRejected", fixture => Fixture.Throws<ArgumentException>(() => new LaunchService(fixture.Store).Prepare(fixture.Add(HarnessKind.Mux), new[] { "--config-dir=/outside" })));
            Add("CodexKeyringOverrideRejected", fixture => Fixture.Throws<ArgumentException>(() => new LaunchService(fixture.Store).Prepare(fixture.Add(), new[] { "-c", "cli_auth_credentials_store=keyring" })));
            Add("ClaudeSettingsOverrideRejected", fixture => Fixture.Throws<ArgumentException>(() => new LaunchService(fixture.Store).Prepare(fixture.Add(HarnessKind.Claude), new[] { "--settings", "outside.json" })));
            Add("MissingExecutableActionable", fixture => Fixture.Throws<FileNotFoundException>(() => ExecutableResolver.Resolve(new ProcessStartInfo(Path.Combine(fixture.Root, "missing-executable")))));
            Add("UnknownBatchLauncherRejected", fixture => { string path = Path.Combine(fixture.Root, "unsafe.cmd"); File.WriteAllText(path, "@echo off\r\necho %*\r\n"); Fixture.Throws<InvalidOperationException>(() => ExecutableResolver.Resolve(new ProcessStartInfo(path))); });
            cases.Add(KameoSuites.Case("Launch", "FailedStartReleasesLease", async (fixture, cancellationToken) =>
            {
                Profile profile = fixture.Add(); profile.Executable = Path.Combine(fixture.Root, "does-not-exist"); fixture.Store.Update(profile);
                try { await new LaunchService(fixture.Store).RunAsync(profile.Id, Array.Empty<string>(), false, cancellationToken).ConfigureAwait(false); throw new InvalidOperationException("Expected launch failure."); }
                catch (FileNotFoundException) { }
                profile.Executable = "codex"; fixture.Store.Update(profile);
            }));
            cases.Add(KameoSuites.Case("Launch", "ChildArgumentsEnvironmentAndExitCode", async (fixture, cancellationToken) =>
            {
                Profile profile = fixture.Add(); profile.Executable = Fixture.Probe(); fixture.Store.Update(profile);
                string output = Path.Combine(fixture.Root, "capture.json");
                int exit = await new LaunchService(fixture.Store).RunAsync(profile.Id, new[] { "--capture", output, "--exit", "23", "spaces and 'quotes'", "$(not-a-shell)", "" }, false, cancellationToken).ConfigureAwait(false);
                JsonNode data = JsonNode.Parse(File.ReadAllText(output))!;
                Fixture.Check(exit == 23, "Child exit code was lost.");
                Fixture.Check(data["environment"]?["OPENAI_API_KEY"]?.GetValue<string>() == "synthetic-test-key", "Child got the wrong key.");
                Fixture.Check(data["environment"]?["CODEX_HOME"]?.GetValue<string>() == fixture.Store.ProfileDirectory(profile.Id), "Child got the wrong home.");
                Fixture.Check(data["arguments"]!.AsArray().Any(value => value?.GetValue<string>() == "$(not-a-shell)"), "Argument was interpreted by a shell.");
                Fixture.Check(data["arguments"]!.AsArray().Last()?.GetValue<string>() == "", "Empty argument was lost.");
            }));
            cases.Add(KameoSuites.Case("Launch", "CancellationReleasesLease", async (fixture, cancellationToken) =>
            {
                Profile profile = fixture.Add(); profile.Executable = Fixture.Probe(); fixture.Store.Update(profile);
                string output = Path.Combine(fixture.Root, "capture.json");
                using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task<int> running = new LaunchService(fixture.Store).RunAsync(profile.Id, new[] { "--capture", output, "--sleep" }, false, cancellation.Token);
                for (int attempt = 0; attempt < 100 && !File.Exists(output); attempt++) await Task.Delay(30, cancellationToken).ConfigureAwait(false);
                Fixture.Check(File.Exists(output), "Probe failed to start.");
                Fixture.Throws<InvalidOperationException>(() => fixture.Store.Update(profile));
                cancellation.Cancel();
                try { await running.ConfigureAwait(false); throw new InvalidOperationException("Expected cancellation."); }
                catch (OperationCanceledException) { }
                fixture.Store.Update(profile);
            }));
            return new TestSuiteDescriptor("Launch", "Harness isolation and real child processes", cases);
        }
    }
}
