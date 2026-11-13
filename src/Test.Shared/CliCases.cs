namespace Test.Shared
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Kameo.Core;
    using Touchstone.Core;

    internal static class CliCases
    {
        internal static TestSuiteDescriptor Build() => new TestSuiteDescriptor("Cli", "Command-line end-to-end workflows", new[]
        {
            KameoSuites.Case("Cli", "CreateSelectRenameDelete", async (fixture, cancellationToken) =>
            {
                CliResult add = await RunAsync(fixture, new[] { "add", "codex", "CLI Work", "--native" }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(add.ExitCode == 0, "CLI creation failed.");
                string id = add.Output.Trim();
                CliResult edit = await RunAsync(fixture, new[] { "edit", id, "--name", "CLI Personal" }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(edit.ExitCode == 0 && fixture.Store.Get(id).Name == "CLI Personal", "CLI rename failed.");
                CliResult select = await RunAsync(fixture, new[] { "select", id }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(select.ExitCode == 0 && fixture.Store.Selected(HarnessKind.Codex).Id == id, "CLI selection failed.");
                CliResult delete = await RunAsync(fixture, new[] { "delete", id }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(delete.ExitCode == 0 && fixture.Store.List().Count == 0, "CLI deletion failed.");
            }),
            KameoSuites.Case("Cli", "StdinKeyAndRedactedList", async (fixture, cancellationToken) =>
            {
                CliResult add = await RunAsync(fixture, new[] { "add", "codex", "CLI API", "--api-key-stdin" }, "synthetic-stdin-key\n", cancellationToken).ConfigureAwait(false);
                Fixture.Check(add.ExitCode == 0 && fixture.Store.Get(add.Output.Trim()).ApiKey == "synthetic-stdin-key", "Standard input key was not saved.");
                CliResult list = await RunAsync(fixture, new[] { "list" }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(list.ExitCode == 0 && !list.Output.Contains("synthetic-stdin-key", StringComparison.Ordinal) && list.Output.Contains("CLI API", StringComparison.Ordinal), "CLI list leaked a key or lost the profile.");
            }),
            KameoSuites.Case("Cli", "UnknownCommandFails", async (fixture, cancellationToken) =>
            {
                CliResult result = await RunAsync(fixture, new[] { "unknown-command" }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(result.ExitCode != 0 && result.Error.Contains("Unknown command", StringComparison.Ordinal), "Unknown command should fail with usage guidance.");
            }),
            KameoSuites.Case("Cli", "SharedMuxFileCanBeAddedChangedAndCleared", async (fixture, cancellationToken) =>
            {
                string first = Path.Combine(fixture.Root, "First endpoints.json");
                string second = Path.Combine(fixture.Root, "Second endpoints.json");
                File.WriteAllText(first, CredentialCases.Document(HarnessKind.Mux));
                File.WriteAllText(second, CredentialCases.Document(HarnessKind.Mux));
                CliResult add = await RunAsync(fixture, new[] { "add", "mux", "Shared", "--native", "--endpoints-file", first }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(add.ExitCode == 0, "Shared Mux creation failed.");
                string id = add.Output.Trim();
                Fixture.Check(fixture.Store.Get(id).MuxEndpointsFile == first, "CLI lost the shared path.");
                CliResult edit = await RunAsync(fixture, new[] { "edit", id, "--endpoints-file", second }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(edit.ExitCode == 0 && fixture.Store.Get(id).MuxEndpointsFile == second, "CLI did not change the shared path.");
                CliResult clear = await RunAsync(fixture, new[] { "edit", id, "--endpoints-file", "" }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(clear.ExitCode == 0 && fixture.Store.Get(id).MuxEndpointsFile.Length == 0 && File.Exists(first) && File.Exists(second), "CLI did not clear the link while preserving both sources.");
            }),
            KameoSuites.Case("Cli", "AntigravityAcceptsApiKeysAndGoogleAccounts", async (fixture, cancellationToken) =>
            {
                CliResult native = await RunAsync(fixture, new[] { "add", "antigravity", "Native", "--native" }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(native.ExitCode == 0 && fixture.Store.Get(native.Output.Trim()).Kind == CredentialKind.Native, "Google account profile creation failed.");
                CliResult add = await RunAsync(fixture, new[] { "add", "antigravity", "API", "--api-key-stdin" }, "synthetic-antigravity-key\n", cancellationToken).ConfigureAwait(false);
                Fixture.Check(add.ExitCode == 0, "Antigravity API creation failed.");
                Profile profile = fixture.Store.Get(add.Output.Trim());
                Fixture.Check(profile.Harness == HarnessKind.Antigravity && profile.ApiKey == "synthetic-antigravity-key", "Antigravity API credentials did not persist.");
            }),
            KameoSuites.Case("Cli", "AntigravityLoginAndSelectedLaunchKeepAccountTokens", async (fixture, cancellationToken) =>
            {
                CliResult add = await RunAsync(fixture, new[] { "add", "antigravity", "Google account", "--native", "--executable", Fixture.Probe() }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(add.ExitCode == 0, "Account creation failed.");
                string id = add.Output.Trim();
                string capture = Path.Combine(fixture.Root, "account-launch.json");
                CliResult login = await RunAsync(fixture, new[] { "login", id, "--", "--capture", capture, "--fixture-token", "synthetic-cli-login" }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(login.ExitCode == 0 && fixture.Store.Get(id).HasNativeCredential, "CLI login did not save an account token.");
                File.Delete(capture);
                CliResult launch = await RunAsync(fixture, new[] { "antigravity", "--", "--capture", capture, "--exit", "17" }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(launch.ExitCode == 17 && File.Exists(capture), "Selected account launch failed.");
                string token = Path.Combine(fixture.Store.ProfileDirectory(id), HarnessCatalog.CredentialFile(HarnessKind.Antigravity));
                Fixture.Check(File.ReadAllText(token).Contains("synthetic-cli-login", StringComparison.Ordinal), "Relaunch lost the account token.");
                File.WriteAllText(Path.Combine(fixture.Root, "probe-version.txt"), "2.0.0");
                CliResult incompatible = await RunAsync(fixture, new[] { "login", id }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(incompatible.ExitCode == 1 && incompatible.Error.Contains(AntigravityCompatibility.SupportedVersion, StringComparison.Ordinal), "CLI did not explain its compatibility requirement.");
            }),
            KameoSuites.Case("Cli", "RunRelaysChildExitCode", async (fixture, cancellationToken) =>
            {
                Profile profile = fixture.Add(); profile.Executable = Fixture.Probe(); fixture.Store.Update(profile);
                string output = Path.Combine(fixture.Root, "cli-probe.json");
                CliResult result = await RunAsync(fixture, new[] { "run", "codex", "--", "--capture", output, "--exit", "17", "literal spaces; $(text)" }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(result.ExitCode == 17 && File.Exists(output), "CLI did not relay the harness exit status.");
            }),
            KameoSuites.Case("Cli", "MalformedImportFailsWithoutSecret", async (fixture, cancellationToken) =>
            {
                Profile profile = fixture.Add(kind: CredentialKind.Native);
                string path = Path.Combine(fixture.Root, "invalid.json"); File.WriteAllText(path, "{synthetic-sensitive-marker");
                CliResult result = await RunAsync(fixture, new[] { "import", profile.Id, path }, null, cancellationToken).ConfigureAwait(false);
                Fixture.Check(result.ExitCode == 1 && !result.Error.Contains("synthetic-sensitive-marker", StringComparison.Ordinal), "Import errors must fail without echoing credential input.");
            })
        });

        private static async Task<CliResult> RunAsync(Fixture fixture, string[] arguments, string? input, CancellationToken cancellationToken)
        {
            using Process process = new Process { StartInfo = new ProcessStartInfo(Fixture.Cli()) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true } };
            process.StartInfo.ArgumentList.Add("--home"); process.StartInfo.ArgumentList.Add(fixture.Root);
            foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            try
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
                Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
                if (input != null) await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken).ConfigureAwait(false);
                process.StandardInput.Close();
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                return new CliResult { ExitCode = process.ExitCode, Output = await output.ConfigureAwait(false), Error = await error.ConfigureAwait(false) };
            }
            finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } }
        }
    }
}
