namespace Kameo.Cli
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Kameo.Core;

    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
            try { return await ExecuteAsync(args, cancellation.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { Console.Error.WriteLine("Canceled."); return 130; }
            catch (System.Text.Json.JsonException) { Console.Error.WriteLine("Kameo: A profile's native configuration is not valid JSON. Repair or re-import it before launching."); return 1; }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException or KeyNotFoundException or TimeoutException or System.ComponentModel.Win32Exception or Microsoft.Data.Sqlite.SqliteException)
            {
                // Vendor JSON and database exception text may contain credential values.
                string message = error is Microsoft.Data.Sqlite.SqliteException ? "Unable to access the profile database. Check its permissions and available disk space." : error.Message;
                Console.Error.WriteLine("Kameo: " + message);
                return 1;
            }
        }

        private static async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken)
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h") { Help(); return 0; }
            if (args[0] == "--version") { Console.WriteLine("Kameo 0.1.0 (alpha)"); return 0; }
            string? root = null;
            if (args[0] == "--home") { Require(args, 3); root = args[1]; args = args[2..]; }
            ProfileStore store = new ProfileStore(root);
            switch (args[0])
            {
                case "desktop-status": return DesktopControl.IsRunning(store) ? 0 : 3;
                case "desktop-stop": await DesktopControl.StopAsync(store, cancellationToken).ConfigureAwait(false); return 0;
                case "startup": Require(args, 3); StartupRegistration.Install(args[1], Path.GetFullPath(args[2]), store.Root); return 0;
                case "remove-startup": Require(args, 2); StartupRegistration.Remove(args[1]); return 0;
                case "list":
                    foreach (Profile profile in store.List())
                        Console.WriteLine($"{(profile.IsActive ? "*" : " ")} {profile.Id}  {HarnessCatalog.Command(profile.Harness),-7}  {profile.Kind,-6}  {profile.Name}");
                    return 0;
                case "add":
                    Require(args, 4);
                    bool native = args.Contains("--native", StringComparer.Ordinal);
                    bool api = args.Contains("--api-key-stdin", StringComparer.Ordinal);
                    if (native == api) throw new ArgumentException("Choose exactly one of --native or --api-key-stdin.");
                    Profile created = store.Create(new Profile
                    {
                        Harness = HarnessCatalog.Parse(args[1]), Name = args[2], Kind = native ? CredentialKind.Native : CredentialKind.ApiKey,
                        ApiKey = api ? (await Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).TrimEnd('\r', '\n') : "",
                        Executable = Option(args, "--executable") ?? "", MuxEndpointsFile = Option(args, "--endpoints-file") ?? ""
                    });
                    Console.WriteLine(created.Id);
                    return 0;
                case "edit":
                    Require(args, 2);
                    Profile edit = store.Get(args[1]);
                    edit.Name = Option(args, "--name") ?? edit.Name;
                    edit.Executable = Option(args, "--executable") ?? edit.Executable;
                    edit.MuxEndpointsFile = Option(args, "--endpoints-file") ?? edit.MuxEndpointsFile;
                    if (args.Contains("--api-key-stdin", StringComparer.Ordinal)) edit.ApiKey = (await Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).TrimEnd('\r', '\n');
                    store.Update(edit);
                    return 0;
                case "select": Require(args, 2); store.Select(args[1]); return 0;
                case "delete": Require(args, 2); store.Delete(args[1]); return 0;
                case "import": Require(args, 3); new CredentialFiles(store).Import(args[1], args[2]); return 0;
                case "import-current": Require(args, 2); await new CurrentLoginImporter(store).ImportAsync(args[1], cancellationToken).ConfigureAwait(false); return 0;
                case "mux-config": Require(args, 5); new CredentialFiles(store).ConfigureMux(args[1], args[2], args[3], args[4]); return 0;
                case "login":
                case "run-profile":
                    Require(args, 2);
                    return await new LaunchService(store).RunAsync(args[1], Tail(args, 2), args[0] == "login", cancellationToken).ConfigureAwait(false);
                case "run":
                    Require(args, 2);
                    return await new LaunchService(store).RunAsync(store.Selected(HarnessCatalog.Parse(args[1])).Id, Tail(args, 2), false, cancellationToken).ConfigureAwait(false);
                case "claude": case "codex": case "antigravity": case "agy": case "gemini": case "mux":
                    return await new LaunchService(store).RunAsync(store.Selected(HarnessCatalog.Parse(args[0])).Id, Tail(args, 1), false, cancellationToken).ConfigureAwait(false);
                default: throw new ArgumentException("Unknown command. Run kameo --help for usage.");
            }
        }

        private static string[] Tail(string[] args, int offset) => args.Length > offset && args[offset] == "--" ? args[(offset + 1)..] : args[offset..];
        private static void Require(string[] args, int count)
        {
            if (args.Length < count) throw new ArgumentException("Missing arguments. Run kameo --help for usage.");
        }
        private static string? Option(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            if (index < 0) return null;
            if (index + 1 >= args.Length) throw new ArgumentException("Missing value for " + name + ".");
            return args[index + 1];
        }
        private static void Help() => Console.WriteLine("""
            Kameo 0.1.0 (alpha) - switch identities, keep your workflow.

            kameo [--home PATH] COMMAND
              list                                     List profiles (* = selected)
              add HARNESS NAME --native                Create an isolated native login
                [--endpoints-file PATH]                Link a shared Mux endpoints file
              add HARNESS NAME --api-key-stdin          Read an API key from standard input
                [--executable PATH]                    Override the harness executable
              edit ID [--name NAME] [--executable PATH] [--api-key-stdin]
                [--endpoints-file PATH]                Link a shared Mux endpoints file (native profiles)
              select ID                                Select for the next invocation
              delete ID                                Delete profile and stored native data
              import ID FILE                           Import native credential JSON
              import-current ID                        Import an existing local login
              login ID [-- --device-auth]               Native login (Mux: setup)
              mux-config ID ADAPTER URL MODEL          Configure a Mux API-key endpoint
              run HARNESS -- [ARGS...]                 Launch the selected profile
              run-profile ID -- [ARGS...]              Launch a specific profile
              claude|codex|antigravity|mux [ARGS...]     Shorthand for run (Antigravity launches agy)

            API keys never go in command arguments. Pass them through stdin or the desktop editor.
            Antigravity uses API-key profiles only; Mux opens its endpoint setup wizard.
            Run with --device-auth only for Codex. Harnesses must already be installed.
            """);
    }
}
