namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Kameo.Core;
    using Touchstone.Core;

    /// <summary>Native logins survive relaunches, closed terminals, and other accounts running at the same time.</summary>
    internal static class LoginPersistenceCases
    {
        private const string _Suite = "LoginPersistence";

        internal static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string name, Func<Fixture, CancellationToken, Task> execute) => cases.Add(KameoSuites.Case(_Suite, name, execute));

            Add("ClaudeRefreshedKeychainLoginIsKept", async (fixture, cancellationToken) =>
            {
                // A session refreshed the login in the Keychain and its terminal closed before Kameo saved it.
                string directory = ClaudeDirectory(fixture);
                FakeKeychain keychain = new FakeKeychain();
                WriteFile(directory, Claude("stale", 1000));
                keychain.Items[MacCredentialBridge.ServiceName(directory)] = Claude("refreshed", 2000);
                await MacCredentialBridge.SynchronizeAsync(directory, true, keychain, cancellationToken).ConfigureAwait(false);
                Fixture.Check(keychain.Writes == 0 && keychain.Items[MacCredentialBridge.ServiceName(directory)] == Claude("refreshed", 2000), "A launch replaced a refreshed Keychain login with an older saved copy.");
                Fixture.Check(ReadFile(directory) == Claude("refreshed", 2000), "The refreshed login was not saved to the profile.");
            });
            Add("ClaudeMissingKeychainLoginIsRestored", async (fixture, cancellationToken) =>
            {
                string directory = ClaudeDirectory(fixture);
                FakeKeychain keychain = new FakeKeychain();
                WriteFile(directory, Claude("saved", 1000));
                await MacCredentialBridge.SynchronizeAsync(directory, true, keychain, cancellationToken).ConfigureAwait(false);
                Fixture.Check(keychain.Items[MacCredentialBridge.ServiceName(directory)] == Claude("saved", 1000), "The saved login was not restored.");
            });
            Add("ClaudeClearedKeychainLoginIsRestored", async (fixture, cancellationToken) =>
            {
                // Claude blanks the tokens after a failed refresh. That item must not overwrite a usable saved login.
                string directory = ClaudeDirectory(fixture);
                FakeKeychain keychain = new FakeKeychain();
                string cleared = "{\"claudeAiOauth\":{\"accessToken\":\"\",\"refreshToken\":\"\",\"expiresAt\":0}}";
                WriteFile(directory, Claude("saved", 1000));
                keychain.Items[MacCredentialBridge.ServiceName(directory)] = cleared;
                await MacCredentialBridge.SynchronizeAsync(directory, false, keychain, cancellationToken).ConfigureAwait(false);
                Fixture.Check(ReadFile(directory) == Claude("saved", 1000) && keychain.Writes == 0, "Ending a session saved a cleared login or wrote the Keychain.");
                await MacCredentialBridge.SynchronizeAsync(directory, true, keychain, cancellationToken).ConfigureAwait(false);
                Fixture.Check(keychain.Items[MacCredentialBridge.ServiceName(directory)] == Claude("saved", 1000), "A cleared Keychain login was not restored at launch.");
            });
            Add("ClaudeNewerSavedLoginIsRestored", async (fixture, cancellationToken) =>
            {
                string directory = ClaudeDirectory(fixture);
                FakeKeychain keychain = new FakeKeychain();
                WriteFile(directory, Claude("newer", 3000));
                keychain.Items[MacCredentialBridge.ServiceName(directory)] = Claude("older", 2000);
                await MacCredentialBridge.SynchronizeAsync(directory, false, keychain, cancellationToken).ConfigureAwait(false);
                Fixture.Check(keychain.Writes == 0 && ReadFile(directory) == Claude("newer", 3000), "Ending a session replaced a newer saved login.");
                await MacCredentialBridge.SynchronizeAsync(directory, true, keychain, cancellationToken).ConfigureAwait(false);
                Fixture.Check(keychain.Items[MacCredentialBridge.ServiceName(directory)] == Claude("newer", 3000), "A newer saved login was not restored.");
            });
            Add("ClaudeNoLoginWritesNothing", async (fixture, cancellationToken) =>
            {
                string directory = ClaudeDirectory(fixture);
                FakeKeychain keychain = new FakeKeychain();
                await MacCredentialBridge.SynchronizeAsync(directory, true, keychain, cancellationToken).ConfigureAwait(false);
                Fixture.Check(keychain.Writes == 0 && !File.Exists(Path.Combine(directory, ".credentials.json")), "Synchronizing an empty profile wrote a login.");
            });
            Add("ClaudeUnreadableKeychainKeepsBothCopies", async (fixture, cancellationToken) =>
            {
                string directory = ClaudeDirectory(fixture);
                FakeKeychain keychain = new FakeKeychain();
                keychain.Items[MacCredentialBridge.ServiceName(directory)] = Claude("live", 2000);
                WriteFile(directory, Claude("saved", 1000));
                keychain.Locked = true;
                await Fixture.ThrowsAsync<InvalidOperationException>(() => MacCredentialBridge.SynchronizeAsync(directory, true, keychain, cancellationToken)).ConfigureAwait(false);
                Fixture.Check(keychain.Writes == 0 && ReadFile(directory) == Claude("saved", 1000), "An unreadable Keychain was overwritten or changed the saved login.");
            });
            Add("ClaudeAccountsSynchronizeIndependently", async (fixture, cancellationToken) =>
            {
                FakeKeychain keychain = new FakeKeychain();
                Profile first = fixture.Add(HarnessKind.Claude, "first@example.test", CredentialKind.Native);
                Profile second = fixture.Add(HarnessKind.Claude, "second@example.test", CredentialKind.Native);
                string firstDirectory = fixture.Store.ProfileDirectory(first.Id);
                string secondDirectory = fixture.Store.ProfileDirectory(second.Id);
                WriteFile(firstDirectory, Claude("first-saved", 1000));
                WriteFile(secondDirectory, Claude("second-saved", 1000));
                await MacCredentialBridge.SynchronizeAsync(firstDirectory, true, keychain, cancellationToken).ConfigureAwait(false);
                await MacCredentialBridge.SynchronizeAsync(secondDirectory, true, keychain, cancellationToken).ConfigureAwait(false);
                keychain.Items[MacCredentialBridge.ServiceName(firstDirectory)] = Claude("first-refreshed", 2000);
                await MacCredentialBridge.SynchronizeAsync(firstDirectory, false, keychain, cancellationToken).ConfigureAwait(false);
                await MacCredentialBridge.SynchronizeAsync(secondDirectory, true, keychain, cancellationToken).ConfigureAwait(false);
                Fixture.Check(ReadFile(firstDirectory) == Claude("first-refreshed", 2000) && ReadFile(secondDirectory) == Claude("second-saved", 1000)
                    && keychain.Items[MacCredentialBridge.ServiceName(secondDirectory)] == Claude("second-saved", 1000), "Claude accounts crossed during synchronization.");
            });
            Add("ClaudeUsagePrefersUsableLogin", async (fixture, cancellationToken) =>
            {
                string directory = ClaudeDirectory(fixture);
                WriteFile(directory, Claude("saved", 1000));
                string? credential = await ClaudeUsage.ReadCredentialAsync(directory, new FakeKeychain(), cancellationToken).ConfigureAwait(false);
                Fixture.Check(credential != null && MacCredentialBridge.Freshness(credential) == 1000, "Usage did not read the saved login.");
                Fixture.Check(MacCredentialBridge.Freshness("{\"claudeAiOauth\":{\"accessToken\":\"\",\"refreshToken\":\"\",\"expiresAt\":5}}") == null
                    && MacCredentialBridge.Freshness("not json") == null && MacCredentialBridge.Freshness(null) == null, "Unusable logins were treated as usable.");
            });

            Add("ClaudeMaintenanceBacksUpRunningSessionLogin", async (fixture, cancellationToken) =>
            {
                // A session refreshed its login days after launch. A usage check saves it without waiting for the session to end.
                Profile profile = fixture.Add(HarnessKind.Claude, "backup@example.test", CredentialKind.Native);
                string directory = fixture.Store.ProfileDirectory(profile.Id);
                FakeKeychain keychain = new FakeKeychain();
                WriteFile(directory, Claude("launched", 1000));
                keychain.Items[MacCredentialBridge.ServiceName(directory)] = Claude("rotated", 2000);
                using (FileStream session = fixture.Store.AcquireSessionLease(profile.Id))
                    await MacCredentialBridge.MaintainAsync(fixture.Store, profile.Id, keychain, cancellationToken).ConfigureAwait(false);
                Fixture.Check(ReadFile(directory) == Claude("rotated", 2000) && keychain.Writes == 0, "A running session's refreshed login was not backed up.");
            });
            Add("ClaudeMaintenanceRestoresClearedLoginOnce", async (fixture, cancellationToken) =>
            {
                Profile profile = fixture.Add(HarnessKind.Claude, "restore@example.test", CredentialKind.Native);
                string directory = fixture.Store.ProfileDirectory(profile.Id);
                string service = MacCredentialBridge.ServiceName(directory);
                FakeKeychain keychain = new FakeKeychain();
                string cleared = "{\"claudeAiOauth\":{\"accessToken\":\"\",\"refreshToken\":\"\",\"expiresAt\":0}}";
                WriteFile(directory, Claude("saved", 1000));
                keychain.Items[service] = cleared;
                using (FileStream session = fixture.Store.AcquireSessionLease(profile.Id))
                {
                    await MacCredentialBridge.MaintainAsync(fixture.Store, profile.Id, keychain, cancellationToken).ConfigureAwait(false);
                    Fixture.Check(keychain.Items[service] == Claude("saved", 1000) && ReadFile(directory) == Claude("saved", 1000), "A cleared Keychain login was not restored while sessions ran.");
                    // Claude rejects the restored login and clears it again. The same saved version is not restored twice.
                    keychain.Items[service] = cleared;
                    await MacCredentialBridge.MaintainAsync(fixture.Store, profile.Id, keychain, cancellationToken).ConfigureAwait(false);
                    Fixture.Check(keychain.Items[service] == cleared && keychain.Writes == 1, "A rejected saved login was restored repeatedly.");
                    // A newer saved login, such as from a sign-in, is restored.
                    WriteFile(directory, Claude("newer", 3000));
                    await MacCredentialBridge.MaintainAsync(fixture.Store, profile.Id, keychain, cancellationToken).ConfigureAwait(false);
                    Fixture.Check(keychain.Items[service] == Claude("newer", 3000), "A newer saved login was not restored.");
                }
            });
            Add("ClaudeMaintenanceWaitsForSignIn", async (fixture, cancellationToken) =>
            {
                Profile profile = fixture.Add(HarnessKind.Claude, "signin@example.test", CredentialKind.Native);
                string directory = fixture.Store.ProfileDirectory(profile.Id);
                FakeKeychain keychain = new FakeKeychain();
                keychain.Items[MacCredentialBridge.ServiceName(directory)] = "{\"claudeAiOauth\":{\"accessToken\":\"\",\"refreshToken\":\"\",\"expiresAt\":0}}";
                WriteFile(directory, Claude("saved", 1000));
                using (FileStream signIn = fixture.Store.AcquireSignInLease(profile.Id))
                {
                    Fixture.Check(fixture.Store.SignInRunning(profile.Id), "A running sign-in was not detected.");
                    Fixture.Throws<InvalidOperationException>(() => fixture.Store.AcquireSignInLease(profile.Id));
                    await MacCredentialBridge.MaintainAsync(fixture.Store, profile.Id, keychain, cancellationToken).ConfigureAwait(false);
                }
                Fixture.Check(keychain.Writes == 0 && !fixture.Store.SignInRunning(profile.Id), "Maintenance restored a saved login over a running sign-in.");
            });
            Add("ClaudeMaintenanceLeavesMissingAndLockedKeychainAlone", async (fixture, cancellationToken) =>
            {
                Profile profile = fixture.Add(HarnessKind.Claude, "missing@example.test", CredentialKind.Native);
                string directory = fixture.Store.ProfileDirectory(profile.Id);
                FakeKeychain keychain = new FakeKeychain();
                WriteFile(directory, Claude("saved", 1000));
                await MacCredentialBridge.MaintainAsync(fixture.Store, profile.Id, keychain, cancellationToken).ConfigureAwait(false);
                keychain.Items[MacCredentialBridge.ServiceName(directory)] = Claude("live", 2000);
                keychain.Locked = true;
                await MacCredentialBridge.MaintainAsync(fixture.Store, profile.Id, keychain, cancellationToken).ConfigureAwait(false);
                Fixture.Check(keychain.Writes == 0 && ReadFile(directory) == Claude("saved", 1000), "Maintenance wrote a missing or unreadable Keychain item.");
            });
            Add("ClaudeAccountSurvivesClearedLogin", async (fixture, cancellationToken) =>
            {
                Profile profile = fixture.Add(HarnessKind.Claude, "account@example.test", CredentialKind.Native);
                string directory = fixture.Store.ProfileDirectory(profile.Id);
                FakeKeychain keychain = new FakeKeychain();
                ClaudeAccount empty = await ClaudeAccount.ReadAsync(directory, keychain, cancellationToken).ConfigureAwait(false);
                Fixture.Check(empty.IsUnknown, "An empty profile reported an account.");
                keychain.Items[MacCredentialBridge.ServiceName(directory)] = "{\"claudeAiOauth\":{\"accessToken\":\"\",\"refreshToken\":\"\",\"expiresAt\":0,\"accountUuid\":\"synthetic-account-a\"}}";
                PrivateFiles.Write(Path.Combine(directory, ".claude.json"), "{\"oauthAccount\":{\"accountUuid\":\"synthetic-account-a\",\"emailAddress\":\"account@example.test\"}}");
                ClaudeAccount before = await ClaudeAccount.ReadAsync(directory, keychain, cancellationToken).ConfigureAwait(false);
                Fixture.Check(before.Id == "synthetic-account-a" && before.Email == "account@example.test", "A cleared login lost its account.");
                keychain.Items[MacCredentialBridge.ServiceName(directory)] = "{\"claudeAiOauth\":{\"accessToken\":\"synthetic-b\",\"refreshToken\":\"synthetic-b-refresh\",\"expiresAt\":5,\"accountUuid\":\"synthetic-account-b\"}}";
                PrivateFiles.Write(Path.Combine(directory, ".claude.json"), "{\"oauthAccount\":{\"emailAddress\":\"other@example.test\"}}");
                ClaudeAccount after = await ClaudeAccount.ReadAsync(directory, keychain, cancellationToken).ConfigureAwait(false);
                Fixture.Check(after.Id == "synthetic-account-b" && ClaudeAccount.AreDifferent(before, after), "A sign-in to a different account was not detected.");
                Fixture.Check(!ClaudeAccount.AreDifferent(before, new ClaudeAccount { Email = "ACCOUNT@example.test" }) && !ClaudeAccount.AreDifferent(before, empty)
                    && ClaudeAccount.AreDifferent(new ClaudeAccount { Email = "a@example.test" }, new ClaudeAccount { Email = "b@example.test" }), "Account comparison is wrong.");
            });

            foreach (HarnessKind harness in HarnessCatalog.All)
            {
                Add(harness + "RefreshSurvivesRelaunch", (fixture, cancellationToken) => RelaunchAsync(fixture, harness, cancellationToken));
                Add(harness + "ConcurrentAccountsKeepOwnLogins", (fixture, cancellationToken) => ConcurrentAsync(fixture, harness, cancellationToken));
                Add(harness + "ClosedTerminalReleasesSession", (fixture, cancellationToken) => HangupAsync(fixture, harness, cancellationToken));
            }
            return new TestSuiteDescriptor(_Suite, "Native logins persist across launches, closed terminals and concurrent accounts", cases);
        }

        private static async Task RelaunchAsync(Fixture fixture, HarnessKind harness, CancellationToken cancellationToken)
        {
            if (SkipsKeychain(harness)) return;
            Profile profile = Create(fixture, harness, "relaunch");
            CliResult first = await RunAsync(fixture, profile, "first", new[] { "--write-credential", "synthetic-refreshed" }, cancellationToken).ConfigureAwait(false);
            Fixture.Check(first.ExitCode == 0, "The first session failed.");
            for (int launch = 0; launch < 2; launch++)
            {
                CliResult again = await RunAsync(fixture, profile, "again" + launch, Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
                Fixture.Check(again.ExitCode == 0, "A later session failed.");
                Fixture.Check(Saved(fixture, profile).Contains("synthetic-refreshed", StringComparison.Ordinal), "A relaunch lost or replaced the refreshed login.");
            }
        }

        private static async Task ConcurrentAsync(Fixture fixture, HarnessKind harness, CancellationToken cancellationToken)
        {
            if (SkipsKeychain(harness)) return;
            Profile first = Create(fixture, harness, "first");
            Profile second = Create(fixture, harness, "second");
            string stop = Path.Combine(fixture.Root, "both.stop");
            Task<CliResult> a = RunAsync(fixture, first, "a", new[] { "--write-credential", "synthetic-first-refreshed", "--wait-for-file", stop }, cancellationToken);
            Task<CliResult> b = RunAsync(fixture, second, "b", new[] { "--write-credential", "synthetic-second-refreshed", "--wait-for-file", stop }, cancellationToken);
            try
            {
                await WaitForAsync(Path.Combine(fixture.Root, "a.json"), a, cancellationToken).ConfigureAwait(false);
                await WaitForAsync(Path.Combine(fixture.Root, "b.json"), b, cancellationToken).ConfigureAwait(false);
            }
            finally { File.WriteAllText(stop, "stop"); }
            Fixture.Check((await a.ConfigureAwait(false)).ExitCode == 0 && (await b.ConfigureAwait(false)).ExitCode == 0, "A concurrent session failed.");
            for (int round = 0; round < 2; round++)
            {
                Task<CliResult> again = RunAsync(fixture, first, "a" + round, Array.Empty<string>(), cancellationToken);
                Task<CliResult> other = RunAsync(fixture, second, "b" + round, Array.Empty<string>(), cancellationToken);
                Fixture.Check((await again.ConfigureAwait(false)).ExitCode == 0 && (await other.ConfigureAwait(false)).ExitCode == 0, "A concurrent relaunch failed.");
            }
            string firstLogin = Saved(fixture, first);
            string secondLogin = Saved(fixture, second);
            Fixture.Check(firstLogin.Contains("synthetic-first-refreshed", StringComparison.Ordinal) && !firstLogin.Contains("second", StringComparison.Ordinal)
                && secondLogin.Contains("synthetic-second-refreshed", StringComparison.Ordinal) && !secondLogin.Contains("first", StringComparison.Ordinal), "Concurrent accounts lost or crossed their logins.");
        }

        private static async Task HangupAsync(Fixture fixture, HarnessKind harness, CancellationToken cancellationToken)
        {
            // Windows has no SIGHUP to send. Claude on macOS would synchronize with the real Keychain.
            if (OperatingSystem.IsWindows() || SkipsKeychain(harness)) return;
            Profile profile = Create(fixture, harness, "hangup");
            string capture = Path.Combine(fixture.Root, "hangup.json");
            using Process process = new Process { StartInfo = new ProcessStartInfo(Fixture.Cli()) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true } };
            process.StartInfo.Environment["KAMEO_HOME"] = fixture.Root;
            foreach (string argument in new[] { "--home", fixture.Root, "run-profile", profile.Id, "--", "--capture", capture, "--write-credential", "synthetic-hangup", "--sleep" })
                process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                while (!File.Exists(capture))
                {
                    Fixture.Check(!process.HasExited, "The session exited before its harness started.");
                    await Task.Delay(25, timeout.Token).ConfigureAwait(false);
                }
                int child = JsonNode.Parse(File.ReadAllText(capture))!["pid"]!.GetValue<int>();
                using (Process signal = Process.Start(new ProcessStartInfo("kill") { UseShellExecute = false, ArgumentList = { "-HUP", process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) } })!)
                    await signal.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                await output.ConfigureAwait(false);
                await error.ConfigureAwait(false);
                Fixture.Check(process.ExitCode == 130, "A closed terminal did not end the session through cancellation.");
                bool childRunning;
                try { using Process remaining = Process.GetProcessById(child); childRunning = !remaining.HasExited; }
                catch (ArgumentException) { childRunning = false; }
                Fixture.Check(!childRunning, "A closed terminal left the harness running.");
                fixture.Store.Update(fixture.Store.Get(profile.Id));
                Fixture.Check(Saved(fixture, profile).Contains("synthetic-hangup", StringComparison.Ordinal), "A closed terminal lost the login.");
                CliResult again = await RunAsync(fixture, profile, "after-hangup", Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
                Fixture.Check(again.ExitCode == 0 && Saved(fixture, profile).Contains("synthetic-hangup", StringComparison.Ordinal), "The login did not survive a closed terminal.");
            }
            finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } }
        }

        private static bool SkipsKeychain(HarnessKind harness) => harness == HarnessKind.Claude && OperatingSystem.IsMacOS();

        private static Profile Create(Fixture fixture, HarnessKind harness, string name)
        {
            Profile profile = fixture.Add(harness, name + "@example.test", CredentialKind.Native);
            profile.Executable = Fixture.Probe();
            fixture.Store.Update(profile);
            new CredentialFiles(fixture.Store).ImportText(profile.Id, CredentialCases.Document(harness));
            return profile;
        }

        private static Task<CliResult> RunAsync(Fixture fixture, Profile profile, string label, string[] extra, CancellationToken cancellationToken)
        {
            List<string> arguments = new List<string> { "run-profile", profile.Id, "--", "--capture", Path.Combine(fixture.Root, label + ".json") };
            arguments.AddRange(extra);
            return CliCases.RunAsync(fixture, arguments.ToArray(), null, cancellationToken);
        }

        private static async Task WaitForAsync(string path, Task<CliResult> process, CancellationToken cancellationToken)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            while (!File.Exists(path))
            {
                Fixture.Check(!process.IsCompleted, "A session exited before its harness started.");
                await Task.Delay(25, timeout.Token).ConfigureAwait(false);
            }
        }

        private static string Saved(Fixture fixture, Profile profile)
            => PrivateFiles.Read(Path.Combine(fixture.Store.ProfileDirectory(profile.Id), HarnessCatalog.CredentialFile(profile.Harness)));

        private static string ClaudeDirectory(Fixture fixture) => fixture.Store.ProfileDirectory(fixture.Add(HarnessKind.Claude, "work@example.test", CredentialKind.Native).Id);

        private static string Claude(string token, long expires)
            => "{\"claudeAiOauth\":{\"accessToken\":\"synthetic-" + token + "\",\"refreshToken\":\"synthetic-" + token + "-refresh\",\"expiresAt\":" + expires.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}";

        private static void WriteFile(string directory, string credential) => PrivateFiles.Write(Path.Combine(directory, ".credentials.json"), credential);

        private static string ReadFile(string directory) => PrivateFiles.Read(Path.Combine(directory, ".credentials.json"));
    }
}
