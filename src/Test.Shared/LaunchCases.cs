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
                foreach (CredentialKind kind in new[] { CredentialKind.ApiKey, CredentialKind.Native })
                    cases.Add(KameoSuites.Case("Launch", "ConcurrentSameProfile" + harness + kind,
                        (fixture, cancellationToken) => ConcurrentSessionsAsync(fixture, harness, kind, cancellationToken)));
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
            Add("ClaudeLoginEmailHint", fixture =>
            {
                Profile profile = fixture.Add(HarnessKind.Claude, "work@example.test", CredentialKind.Native);
                LaunchService launcher = new LaunchService(fixture.Store);
                Fixture.Check(launcher.Prepare(profile, Array.Empty<string>(), true).StartInfo.ArgumentList.SequenceEqual(new[] { "auth", "login", "--email", profile.Name }), "Email profile did not supply a login hint.");
                Fixture.Check(launcher.Prepare(profile, Array.Empty<string>()).StartInfo.ArgumentList.Count == 0, "Ordinary launch received login flags.");
                foreach (string[] arguments in new[] { new[] { "--email", "other@example.test" }, new[] { "--email=other@example.test" } })
                    Fixture.Check(launcher.Prepare(profile, arguments, true).StartInfo.ArgumentList.SequenceEqual(new[] { "auth", "login" }.Concat(arguments)), "Explicit email was overridden or duplicated.");
                profile.Name = "Work account";
                Fixture.Check(launcher.Prepare(profile, Array.Empty<string>(), true).StartInfo.ArgumentList.SequenceEqual(new[] { "auth", "login" }), "Display name was treated as an email.");
            });
            cases.Add(KameoSuites.Case("Launch", "ClaudeInheritedCredentialStoresAreIsolated", async (fixture, cancellationToken) =>
            {
                string outside = Path.Combine(fixture.Root, "other-account");
                PrivateFiles.DirectoryCreate(outside);
                string sentinel = Path.Combine(outside, ".credentials.json");
                PrivateFiles.Write(sentinel, CredentialCases.Document(HarnessKind.Claude));
                Dictionary<string, string> inherited = new Dictionary<string, string>
                {
                    ["CLAUDE_CONFIG_DIR"] = outside, ["CLAUDE_SECURESTORAGE_CONFIG_DIR"] = outside,
                    ["ANTHROPIC_CONFIG_DIR"] = outside, ["ANTHROPIC_PROFILE"] = "other-account",
                    ["ANTHROPIC_FEDERATION_RULE_ID"] = "synthetic-rule", ["ANTHROPIC_ORGANIZATION_ID"] = "synthetic-org",
                    ["ANTHROPIC_IDENTITY_TOKEN"] = "synthetic-token", ["ANTHROPIC_IDENTITY_TOKEN_FILE"] = sentinel,
                    ["CLAUDE_CODE_FEDERATION_CACHE_DIR"] = outside
                };
                foreach (CredentialKind kind in new[] { CredentialKind.Native, CredentialKind.ApiKey })
                {
                    Profile profile = fixture.Add(HarnessKind.Claude, kind + "@example.test", kind);
                    profile.Executable = Fixture.Probe(); fixture.Store.Update(profile);
                    foreach (bool login in kind == CredentialKind.Native ? new[] { false, true } : new[] { false })
                    {
                        string capture = Path.Combine(fixture.Root, kind + "-" + login + ".json");
                        CliResult result = await CliCases.RunAsync(fixture, new[] { login ? "login" : "run-profile", profile.Id, "--", "--capture", capture }, null, cancellationToken, inherited).ConfigureAwait(false);
                        Fixture.Check(result.ExitCode == 0, "Synthetic Claude did not start.");
                        JsonNode data = JsonNode.Parse(File.ReadAllText(capture))!;
                        string directory = fixture.Store.ProfileDirectory(profile.Id);
                        Fixture.Check(data["environment"]?["KAMEO_PROFILE_ID"]?.GetValue<string>() == profile.Id, "Claude used another profile.");
                        foreach (string variable in new[] { "CLAUDE_CONFIG_DIR", "CLAUDE_SECURESTORAGE_CONFIG_DIR" })
                            Fixture.Check(data["environment"]?[variable]?.GetValue<string>() == directory, "Claude credential storage escaped the selected profile.");
                        Fixture.Check(data["environment"]?["ANTHROPIC_CONFIG_DIR"]?.GetValue<string>() == Path.Combine(directory, "anthropic"), "Anthropic fallback storage escaped the selected profile.");
                        Fixture.Check(data["hasClaudeCredentialOverride"]?.GetValue<bool>() == false, "An inherited credential selector reached Claude.");
                        if (login) Fixture.Check(data["arguments"]!.AsArray().Any(value => value?.GetValue<string>() == profile.Name), "CLI sign-in lost the email hint.");
                    }
                }
                Fixture.Check(PrivateFiles.Read(sentinel) == CredentialCases.Document(HarnessKind.Claude), "Another account's credentials changed.");
            }));
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
            Add("NpmShimProbeLineIsNotTheTarget", fixture =>
            {
                // Modern npm .cmd shims test for a bundled node before the exec line. When node.exe exists beside the
                // shim the resolver must still select the exec target, not the probe, or the harness runs as node.exe.
                string directory = Path.Combine(fixture.Root, "shim"); Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "node.exe"), "");
                string target = Path.Combine(directory, "harness.exe"); File.WriteAllText(target, "");
                string shim = Path.Combine(directory, "harness.cmd");
                File.WriteAllText(shim, "@ECHO off\r\nIF EXIST \"%~dp0\\node.exe\" (\r\n  SET \"_prog=%~dp0\\node.exe\"\r\n)\r\n\"%~dp0\\harness.exe\"   %*\r\n");
                Fixture.Check(ExecutableResolver.Resolve(new ProcessStartInfo(shim)).FileName == target, "The npm shim resolved to its node.exe probe instead of the exec target.");
            });
            cases.Add(KameoSuites.Case("Launch", "FailedStartReleasesLease", async (fixture, cancellationToken) =>
            {
                Profile profile = fixture.Add(); profile.Executable = Path.Combine(fixture.Root, "does-not-exist"); fixture.Store.Update(profile);
                try { await new LaunchService(fixture.Store).RunAsync(profile.Id, Array.Empty<string>(), false, cancellationToken).ConfigureAwait(false); throw new TestFailureException("Expected launch failure."); }
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
                Fixture.Check(data["arguments"]!.AsArray().LastOrDefault()?.GetValue<string>() == "", "Empty argument was lost.");
            }));
            cases.Add(KameoSuites.Case("Launch", "CancellationPreservesSiblingLease", async (fixture, cancellationToken) =>
            {
                Profile profile = fixture.Add(); profile.Executable = Fixture.Probe(); fixture.Store.Update(profile);
                string output = Path.Combine(fixture.Root, "capture.json");
                string siblingOutput = Path.Combine(fixture.Root, "sibling.json");
                using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                using CancellationTokenSource siblingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task<int> running = new LaunchService(fixture.Store).RunAsync(profile.Id, new[] { "--capture", output, "--sleep" }, false, cancellation.Token);
                Task<int> sibling = new LaunchService(fixture.Store).RunAsync(profile.Id, new[] { "--capture", siblingOutput, "--sleep" }, false, siblingCancellation.Token);
                try
                {
                    for (int attempt = 0; attempt < 100 && (!File.Exists(output) || !File.Exists(siblingOutput)); attempt++)
                        await Task.Delay(30, cancellationToken).ConfigureAwait(false);
                    Fixture.Check(File.Exists(output) && File.Exists(siblingOutput), "Both probes must start.");
                    Fixture.Throws<InvalidOperationException>(() => fixture.Store.Update(profile));
                    cancellation.Cancel();
                    await Fixture.ThrowsAsync<OperationCanceledException>(async () => await running.ConfigureAwait(false)).ConfigureAwait(false);
                    Fixture.Check(!sibling.IsCompleted, "Canceling a session stopped its sibling.");
                    Fixture.Throws<InvalidOperationException>(() => fixture.Store.Update(profile));
                }
                finally
                {
                    cancellation.Cancel(); siblingCancellation.Cancel();
                    try { await Task.WhenAll(running, sibling).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }
                fixture.Store.Update(profile);
            }));
            cases.Add(KameoSuites.Case("Launch", "LoginBlocksConcurrentLaunch", async (fixture, cancellationToken) =>
            {
                Profile profile = fixture.Add(kind: CredentialKind.Native); profile.Executable = Fixture.Probe(); fixture.Store.Update(profile);
                string output = Path.Combine(fixture.Root, "login.json");
                using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task<int> login = new LaunchService(fixture.Store).RunAsync(profile.Id, new[] { "--capture", output, "--sleep" }, true, cancellation.Token);
                try
                {
                    for (int attempt = 0; attempt < 100 && !File.Exists(output); attempt++) await Task.Delay(30, cancellationToken).ConfigureAwait(false);
                    Fixture.Check(File.Exists(output), "Login probe failed to start.");
                    await Fixture.ThrowsAsync<InvalidOperationException>(() => new LaunchService(fixture.Store).RunAsync(profile.Id, Array.Empty<string>(), false, cancellationToken)).ConfigureAwait(false);
                }
                finally
                {
                    cancellation.Cancel();
                    try { await login.ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }
                fixture.Store.Update(profile);
            }));
            Add("PermissionLevelsPassThroughIsolation", fixture =>
            {
                foreach (HarnessKind harness in HarnessCatalog.All)
                {
                    IReadOnlyList<LaunchPermission> levels = LaunchPermissions.For(harness);
                    Fixture.Check(levels[0].Id == LaunchPermissions.DefaultId && levels[0].Arguments.Count == 0 && !levels[0].IsDangerous, harness + " must start with a default level that adds no arguments.");
                    Fixture.Check(levels.Select(level => level.Id).Distinct(StringComparer.Ordinal).Count() == levels.Count, harness + " permission identifiers must be unique.");
                    Fixture.Check(levels.Count(level => level.IsDangerous) == 1 && levels.Single(level => level.IsDangerous).Id == "bypass", harness + " must offer exactly one dangerous bypass level.");
                    Fixture.Check(LaunchPermissions.Find(harness, "unknown") == levels[0] && LaunchPermissions.Find(harness, null) == levels[0] && LaunchPermissions.Find(harness, "bypass").IsDangerous, "Unknown levels must fall back to the default.");
                    Profile profile = fixture.Add(harness, "Permissions " + harness);
                    if (harness == HarnessKind.Mux) new CredentialFiles(fixture.Store).ConfigureMux(profile.Id, "openai", "https://example.test", "test-model");
                    foreach (LaunchPermission level in levels)
                    {
                        ProcessStartInfo start = new LaunchService(fixture.Store).Prepare(profile, level.Arguments).StartInfo;
                        Fixture.Check(start.ArgumentList.TakeLast(level.Arguments.Count).SequenceEqual(level.Arguments), harness + " " + level.Id + " arguments were changed or rejected.");
                    }
                }
                Fixture.Check(LaunchPermissions.Find(HarnessKind.Claude, "bypass").Arguments.SequenceEqual(new[] { "--dangerously-skip-permissions" })
                    && LaunchPermissions.Find(HarnessKind.Codex, "bypass").Arguments.SequenceEqual(new[] { "--dangerously-bypass-approvals-and-sandbox" })
                    && LaunchPermissions.Find(HarnessKind.Mux, "bypass").Arguments.SequenceEqual(new[] { "--yolo" }), "Bypass flags changed.");
                Fixture.Check(LaunchPermissions.PreferenceName(HarnessKind.Claude) == "launch-permission-claude", "The remembered level preference name changed.");
            });
            return new TestSuiteDescriptor("Launch", "Harness isolation and real child processes", cases);
        }

        // A Claude Code sign-in joins open sessions. While it runs, launches and a second sign-in wait for it, and edits stay blocked.
        private static async Task SignInAlongsideSessionsAsync(Fixture fixture, Profile profile, CancellationToken cancellationToken)
        {
            string capture = Path.Combine(fixture.Root, "signin.json");
            string stop = Path.Combine(fixture.Root, "signin.stop");
            Task<CliResult> login = CliCases.RunAsync(fixture, new[] { "login", profile.Id, "--", "--capture", capture, "--wait-for-file", stop }, null, cancellationToken);
            try
            {
                while (!File.Exists(capture))
                {
                    Fixture.Check(!login.IsCompleted, "A Claude sign-in was refused while sessions were open: " + (login.IsCompleted ? login.Result.Error : ""));
                    await Task.Delay(25, cancellationToken).ConfigureAwait(false);
                }
                JsonNode data = JsonNode.Parse(File.ReadAllText(capture))!;
                Fixture.Check(data["arguments"]!.AsArray().Take(2).Select(value => value?.GetValue<string>()).SequenceEqual(new[] { "auth", "login" }), "The sign-in did not run claude auth login.");
                CliResult launch = await CliCases.RunAsync(fixture, new[] { "run-profile", profile.Id, "--", "--capture", Path.Combine(fixture.Root, "during.json") }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(launch.ExitCode == 1 && launch.Error.Contains("sign-in is running", StringComparison.Ordinal), "A launch started during a sign-in.");
                CliResult second = await CliCases.RunAsync(fixture, new[] { "login", profile.Id, "--", "--capture", Path.Combine(fixture.Root, "second-signin.json") }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(second.ExitCode == 1 && second.Error.Contains("already running", StringComparison.Ordinal), "Two sign-ins ran for one profile.");
                Fixture.Throws<InvalidOperationException>(() => fixture.Store.Update(profile));
            }
            finally { File.WriteAllText(stop, "stop"); }
            Fixture.Check((await login.ConfigureAwait(false)).ExitCode == 0, "The sign-in did not finish.");
            Fixture.Check(!fixture.Store.SignInRunning(profile.Id), "The sign-in lock outlived its sign-in.");
            CliResult after = await CliCases.RunAsync(fixture, new[] { "run-profile", profile.Id, "--", "--capture", Path.Combine(fixture.Root, "after-signin.json") }, null, cancellationToken).ConfigureAwait(false);
            Fixture.Check(after.ExitCode == 0, "A launch failed after the sign-in finished.");
        }

        private static async Task ConcurrentSessionsAsync(Fixture fixture, HarnessKind harness, CredentialKind kind, CancellationToken cancellationToken)
        {
            Profile profile = fixture.Add(harness, kind: kind);
            profile.Executable = Fixture.Probe();
            fixture.Store.Update(profile);
            if (harness == HarnessKind.Mux && kind == CredentialKind.ApiKey)
                new CredentialFiles(fixture.Store).ConfigureMux(profile.Id, "openai", "https://example.test", "test-model");
            // Claude native sessions use an empty synthetic profile to avoid writing to macOS Keychain.
            if (kind == CredentialKind.Native && harness != HarnessKind.Claude)
                new CredentialFiles(fixture.Store).ImportText(profile.Id, CredentialCases.Document(harness));
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            string firstCapture = Path.Combine(fixture.Root, "first.json");
            string secondCapture = Path.Combine(fixture.Root, "second.json");
            string firstStop = Path.Combine(fixture.Root, "first.stop");
            string secondStop = Path.Combine(fixture.Root, "second.stop");
            Task<CliResult> first = CliCases.RunAsync(fixture, new[] { "run-profile", profile.Id, "--", "--capture", firstCapture, "--wait-for-file", firstStop, "--exit", "17" }, null, timeout.Token);
            Task<CliResult> second = CliCases.RunAsync(fixture, new[] { HarnessCatalog.Command(harness), "--", "--capture", secondCapture, "--wait-for-file", secondStop, "--exit", "23" }, null, timeout.Token);
            try
            {
                while (!File.Exists(firstCapture) || !File.Exists(secondCapture))
                {
                    Fixture.Check(!first.IsCompleted && !second.IsCompleted, "A same-profile CLI session exited before both harnesses started.");
                    await Task.Delay(25, timeout.Token).ConfigureAwait(false);
                }
                foreach (string capture in new[] { firstCapture, secondCapture })
                {
                    JsonNode data = JsonNode.Parse(File.ReadAllText(capture))!;
                    Fixture.Check(data["environment"]?["KAMEO_PROFILE_ID"]?.GetValue<string>() == profile.Id, "Concurrent child used a different profile.");
                    string directory = fixture.Store.ProfileDirectory(profile.Id);
                    string variable = harness switch { HarnessKind.Claude => "CLAUDE_CONFIG_DIR", HarnessKind.Codex => "CODEX_HOME", _ => "MUX_CONFIG_DIR" };
                    Fixture.Check(harness == HarnessKind.Antigravity
                        ? data["arguments"]!.AsArray().Any(value => value?.GetValue<string>() == directory)
                        : data["environment"]?[variable]?.GetValue<string>() == directory, "Concurrent child lost the shared native profile directory.");
                }
                Fixture.Throws<InvalidOperationException>(() => fixture.Store.Update(profile));
                Fixture.Throws<InvalidOperationException>(() => fixture.Store.Delete(profile.Id));
                if (kind == CredentialKind.Native)
                {
                    Fixture.Throws<InvalidOperationException>(() => new CredentialFiles(fixture.Store).ImportText(profile.Id, CredentialCases.Document(harness)));
                    if (harness == HarnessKind.Claude) await SignInAlongsideSessionsAsync(fixture, profile, timeout.Token).ConfigureAwait(false);
                    else
                    {
                        CliResult login = await CliCases.RunAsync(fixture, new[] { "login", profile.Id }, null, timeout.Token).ConfigureAwait(false);
                        Fixture.Check(login.ExitCode == 1 && login.Error.Contains("in use", StringComparison.Ordinal), "Login must remain exclusive while sessions run.");
                    }
                }
                File.WriteAllText(firstStop, "stop");
                Fixture.Check((await first.ConfigureAwait(false)).ExitCode == 17, "First session lost its exit code.");
                Fixture.Check(!second.IsCompleted, "Closing one session stopped its sibling.");
                Fixture.Throws<InvalidOperationException>(() => fixture.Store.Update(profile));
                CliResult third = await CliCases.RunAsync(fixture, new[] { "run-profile", profile.Id, "--", "--capture", Path.Combine(fixture.Root, "third.json"), "--exit", "7" }, null, timeout.Token).ConfigureAwait(false);
                Fixture.Check(third.ExitCode == 7, "A new session could not join a remaining session.");
                File.WriteAllText(secondStop, "stop");
                Fixture.Check((await second.ConfigureAwait(false)).ExitCode == 23, "Second session lost its exit code.");
                fixture.Store.Update(profile);
            }
            finally
            {
                File.WriteAllText(firstStop, "stop");
                File.WriteAllText(secondStop, "stop");
                await Task.WhenAll(first, second).ConfigureAwait(false);
            }
        }
    }
}
