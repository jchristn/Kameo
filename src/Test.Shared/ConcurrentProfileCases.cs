namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Kameo.Core;
    using Touchstone.Core;

    internal static class ConcurrentProfileCases
    {
        internal static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            foreach (HarnessKind harness in HarnessCatalog.All)
                foreach (CredentialKind kind in new[] { CredentialKind.ApiKey, CredentialKind.Native })
                    cases.Add(KameoSuites.Case("ConcurrentProfiles", harness + kind.ToString(),
                        (fixture, cancellationToken) => RunAsync(fixture, harness, kind, cancellationToken)));
            return new TestSuiteDescriptor("ConcurrentProfiles", "Independent simultaneous profiles and profile-local sign-in", cases);
        }

        private static async Task RunAsync(Fixture fixture, HarnessKind harness, CredentialKind kind, CancellationToken cancellationToken)
        {
            Profile firstProfile = Create(fixture, harness, kind, "first");
            Profile secondProfile = Create(fixture, harness, kind, "second");
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            List<Task<CliResult>> processes = new List<Task<CliResult>>();
            string firstStop = Path.Combine(fixture.Root, "first.stop");
            string loginStop = Path.Combine(fixture.Root, "login.stop");
            string secondStop = Path.Combine(fixture.Root, "second.stop");
            Task<CliResult> first = Start(fixture, firstProfile, false, "first", firstStop, timeout.Token);
            processes.Add(first);
            try
            {
                await WaitForCaptureAsync(fixture, "first", first, timeout.Token).ConfigureAwait(false);
                if (kind == CredentialKind.Native)
                {
                    Task<CliResult> login = Start(fixture, secondProfile, true, "login", loginStop, timeout.Token);
                    processes.Add(login);
                    await WaitForCaptureAsync(fixture, "login", login, timeout.Token).ConfigureAwait(false);
                    CheckIdentity(fixture, secondProfile, "login");
                    Fixture.Check(!first.IsCompleted, "Signing into another profile ended the first session.");
                    File.WriteAllText(loginStop, "stop");
                    Fixture.Check((await login.ConfigureAwait(false)).ExitCode == 0, "Other-profile sign-in failed.");
                }
                Task<CliResult> second = Start(fixture, secondProfile, false, "second", secondStop, timeout.Token);
                processes.Add(second);
                await WaitForCaptureAsync(fixture, "second", second, timeout.Token).ConfigureAwait(false);
                Fixture.Check(!first.IsCompleted && !second.IsCompleted, "Both profiles must run simultaneously.");
                fixture.Store.Select(secondProfile.Id);
                CheckIdentity(fixture, firstProfile, "first");
                CheckIdentity(fixture, secondProfile, "second");
                Fixture.Throws<InvalidOperationException>(() => fixture.Store.Update(firstProfile));
                Fixture.Throws<InvalidOperationException>(() => fixture.Store.Update(secondProfile));
                if (harness == HarnessKind.Claude)
                    Fixture.Check(MacCredentialBridge.ServiceName(fixture.Store.ProfileDirectory(firstProfile.Id))
                        != MacCredentialBridge.ServiceName(fixture.Store.ProfileDirectory(secondProfile.Id)), "Claude accounts share a Keychain service.");
                File.WriteAllText(firstStop, "stop");
                Fixture.Check((await first.ConfigureAwait(false)).ExitCode == 0, "First profile failed to exit.");
                Fixture.Check(!second.IsCompleted, "Closing the first profile ended the second session.");
                fixture.Store.Update(firstProfile);
                if (kind == CredentialKind.Native)
                {
                    CliResult login = await CliCases.RunAsync(fixture, new[] { "login", firstProfile.Id, "--", "--capture", Path.Combine(fixture.Root, "relogin.json") }, null, timeout.Token).ConfigureAwait(false);
                    Fixture.Check(login.ExitCode == 0 && !second.IsCompleted, "Reauthenticating an idle profile disrupted another account.");
                    CheckIdentity(fixture, firstProfile, "relogin");
                }
                File.WriteAllText(secondStop, "stop");
                Fixture.Check((await second.ConfigureAwait(false)).ExitCode == 0, "Second profile failed to exit.");
                fixture.Store.Update(secondProfile);
            }
            finally
            {
                foreach (string path in new[] { firstStop, loginStop, secondStop }) File.WriteAllText(path, "stop");
                await Task.WhenAll(processes).ConfigureAwait(false);
            }
        }

        private static Profile Create(Fixture fixture, HarnessKind harness, CredentialKind kind, string name)
        {
            Profile profile = fixture.Add(harness, name + "@example.test", kind);
            profile.Executable = Fixture.Probe();
            if (kind == CredentialKind.ApiKey) profile.ApiKey = "synthetic-" + name;
            fixture.Store.Update(profile);
            if (harness == HarnessKind.Mux && kind == CredentialKind.ApiKey)
                new CredentialFiles(fixture.Store).ConfigureMux(profile.Id, "openai", "https://example.test", "test-model");
            // Empty Claude native profiles avoid writing to macOS Keychain in this process-contract test.
            if (kind == CredentialKind.Native && harness != HarnessKind.Claude)
                new CredentialFiles(fixture.Store).ImportText(profile.Id, CredentialCases.Document(harness).Replace("synthetic-", "synthetic-" + name + "-", StringComparison.Ordinal));
            return profile;
        }

        private static Task<CliResult> Start(Fixture fixture, Profile profile, bool login, string label, string stop, CancellationToken cancellationToken)
            => CliCases.RunAsync(fixture, new[] { login ? "login" : "run-profile", profile.Id, "--", "--capture", Path.Combine(fixture.Root, label + ".json"), "--wait-for-file", stop }, null, cancellationToken);

        private static async Task WaitForCaptureAsync(Fixture fixture, string label, Task<CliResult> process, CancellationToken cancellationToken)
        {
            while (!File.Exists(Path.Combine(fixture.Root, label + ".json")))
            {
                Fixture.Check(!process.IsCompleted, "A profile exited before its synthetic harness started.");
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }

        private static void CheckIdentity(Fixture fixture, Profile profile, string label)
        {
            JsonNode data = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.Root, label + ".json")))!;
            string directory = fixture.Store.ProfileDirectory(profile.Id);
            Fixture.Check(data["environment"]?["KAMEO_PROFILE_ID"]?.GetValue<string>() == profile.Id, "The child used another profile identifier.");
            if (profile.Harness == HarnessKind.Antigravity)
            {
                string[] arguments = data["arguments"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray();
                int index = Array.IndexOf(arguments, "--gemini_dir");
                Fixture.Check(index >= 0 && index + 1 < arguments.Length && arguments[index + 1] == directory, "Antigravity used another account directory.");
            }
            else
            {
                string variable = profile.Harness switch { HarnessKind.Claude => "CLAUDE_CONFIG_DIR", HarnessKind.Codex => "CODEX_HOME", _ => "MUX_CONFIG_DIR" };
                Fixture.Check(data["environment"]?[variable]?.GetValue<string>() == directory, "The child used another account directory.");
            }
            if (profile.Harness == HarnessKind.Claude)
                Fixture.Check(data["environment"]?["CLAUDE_SECURESTORAGE_CONFIG_DIR"]?.GetValue<string>() == directory
                    && data["environment"]?["ANTHROPIC_CONFIG_DIR"]?.GetValue<string>() == Path.Combine(directory, "anthropic"), "Claude credential stores crossed accounts.");
            if (profile.Kind == CredentialKind.ApiKey)
            {
                string variable = profile.Harness switch { HarnessKind.Claude => "ANTHROPIC_API_KEY", HarnessKind.Codex => "OPENAI_API_KEY", HarnessKind.Antigravity => "GEMINI_API_KEY", _ => "KAMEO_MUX_API_KEY" };
                Fixture.Check(data["environment"]?[variable]?.GetValue<string>() == profile.ApiKey, "The child received another account's key.");
            }
        }
    }
}
