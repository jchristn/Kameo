namespace Kameo.Core
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Reads Claude's directory-scoped macOS login into its Kameo filesystem profile.</summary>
    public static class MacCredentialBridge
    {
        /// <summary>Returns Claude's keychain service for an explicit config directory, or its default service.</summary>
        public static string ServiceName(string? directory) => "Claude Code-credentials" + (directory == null ? "" : "-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(directory.Normalize(NormalizationForm.FormC))))[..8]);

        /// <summary>Copies an available scoped login to disk after a native Claude process exits.</summary>
        public static async Task SnapshotAsync(string directory, CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsMacOS()) return;
            string? credential = await ReadAsync(ServiceName(directory), cancellationToken).ConfigureAwait(false);
            if (credential == null) return;
            CredentialFiles.Validate(HarnessKind.Claude, credential);
            PrivateFiles.Write(Path.Combine(directory, ".credentials.json"), credential);
        }

        /// <summary>Restores this profile's file into its own native keychain entry before launch.</summary>
        public static async Task RestoreAsync(string directory, CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsMacOS()) return;
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            cancellationToken = timeout.Token;
            string path = Path.Combine(directory, ".credentials.json");
            if (!File.Exists(path)) return;
            string credential = PrivateFiles.Read(path);
            CredentialFiles.Validate(HarnessKind.Claude, credential);
            string account = Environment.GetEnvironmentVariable("USER") ?? Environment.UserName;
            if (account.IndexOfAny(new[] { '"', '\\', '\n', '\r' }) >= 0) throw new InvalidOperationException("The system account name cannot be used for Keychain synchronization.");
            using Process process = new Process { StartInfo = new ProcessStartInfo("/usr/bin/security") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            process.StartInfo.ArgumentList.Add("-i");
            process.Start();
            try
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
                Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.StandardInput.WriteLineAsync(("add-generic-password -U -a \"" + account + "\" -s \"" + ServiceName(directory) + "\" -X \"" + Convert.ToHexString(Encoding.UTF8.GetBytes(credential)) + "\"").AsMemory(), cancellationToken).ConfigureAwait(false);
                process.StandardInput.Close();
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                await output.ConfigureAwait(false);
                await error.ConfigureAwait(false);
                if (process.ExitCode != 0) throw new InvalidOperationException("Unable to synchronize this profile with macOS Keychain. Unlock your login keychain and try again.");
                string? restored = await ReadAsync(ServiceName(directory), cancellationToken).ConfigureAwait(false);
                if (!string.Equals(restored, credential.Trim(), StringComparison.Ordinal))
                    throw new InvalidOperationException("macOS Keychain did not retain this profile's credentials. Unlock your login keychain and try again.");
            }
            finally { if (!process.HasExited) process.Kill(true); }
        }

        /// <summary>Deletes only the native keychain item belonging to an explicit Kameo profile directory.</summary>
        public static void DeleteScoped(string directory)
        {
            if (!OperatingSystem.IsMacOS()) return;
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            using Process process = new Process { StartInfo = new ProcessStartInfo("/usr/bin/security") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true } };
            foreach (string argument in new[] { "delete-generic-password", "-s", ServiceName(directory), "-a", Environment.GetEnvironmentVariable("USER") ?? Environment.UserName }) process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            if (!process.WaitForExit(5000)) { process.Kill(true); throw new IOException("Keychain did not respond. Unlock it and retry deleting this profile."); }
            if (process.ExitCode != 0 && process.ExitCode != 44) throw new IOException("The profile's Keychain entry could not be deleted. Unlock Keychain and retry.");
        }

        /// <summary>Reads an explicitly requested keychain service. Never logs the password or invokes a shell.</summary>
        public static async Task<string?> ReadAsync(string service, CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Keychain import is available on macOS only.");
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using Process process = new Process { StartInfo = new ProcessStartInfo("/usr/bin/security") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true } };
            foreach (string argument in new[] { "find-generic-password", "-s", service, "-w" }) process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            try
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                Task<string> errors = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                string value = await output.ConfigureAwait(false);
                await errors.ConfigureAwait(false);
                return process.ExitCode == 0 ? value.Trim() : null;
            }
            finally { if (!process.HasExited) process.Kill(true); }
        }
    }
}
