namespace Test.Desktop
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Kameo.Core;

    internal static class NativeSmoke
    {
        internal static async Task<int> RunAsync(string executable, CancellationToken cancellationToken)
        {
            string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "kameo-native-test-" + Guid.NewGuid().ToString("N"));
            ProfileStore store = new ProfileStore(root);
            using Process process = new Process { StartInfo = new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false } };
            process.StartInfo.Environment["KAMEO_HOME"] = root;
            try
            {
                process.Start();
                await Task.Delay(3000, cancellationToken).ConfigureAwait(false);
                if (process.HasExited || !DesktopControl.IsRunning(store)) throw new InvalidOperationException("The native desktop failed during initial startup.");
                Profile first = store.Create(new Profile { Name = "Smoke work", Harness = HarnessKind.Codex, ApiKey = "synthetic" });
                Profile second = store.Create(new Profile { Name = "Smoke personal", Harness = HarnessKind.Codex, ApiKey = "synthetic" });
                store.Select(second.Id);
                await Task.Delay(3000, cancellationToken).ConfigureAwait(false);
                if (process.HasExited) throw new InvalidOperationException("The native desktop crashed while updating its tray menu.");
                store.Delete(first.Id);
                await Task.Delay(2500, cancellationToken).ConfigureAwait(false);
                if (process.HasExited) throw new InvalidOperationException("The native desktop crashed after deleting a profile.");
                await DesktopControl.StopAsync(store, cancellationToken).ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                if (process.ExitCode != 0) throw new InvalidOperationException("The native desktop did not shut down cleanly.");
                Console.WriteLine("PASS: native startup, tray refresh, profile changes, and graceful shutdown.");
                return 0;
            }
            finally
            {
                if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
                Directory.Delete(root, true);
            }
        }
    }
}
