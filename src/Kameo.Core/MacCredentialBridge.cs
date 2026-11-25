namespace Kameo.Core
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Keeps Claude's directory-scoped macOS Keychain login and its Kameo profile file in step. Claude reads and refreshes
    /// the Keychain item, so it is authoritative whenever it holds a usable login; the file is a durable copy that only
    /// replaces the Keychain item when the item is missing, cleared, or older.
    /// </summary>
    public static class MacCredentialBridge
    {
        private const int _ItemNotFound = 44;

        /// <summary>Returns Claude's keychain service for an explicit config directory, or its default service.</summary>
        public static string ServiceName(string? directory) => "Claude Code-credentials" + (directory == null ? "" : "-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(directory.Normalize(NormalizationForm.FormC))))[..8]);

        /// <summary>
        /// Saves the profile's Keychain login to its file when the Keychain copy is at least as fresh, and, when
        /// <paramref name="restore"/> is set, writes the file back to the Keychain when the Keychain copy is missing,
        /// cleared or older. Does nothing outside macOS.
        /// </summary>
        /// <exception cref="InvalidOperationException">The Keychain could not be read or written.</exception>
        public static Task SynchronizeAsync(string directory, bool restore, CancellationToken cancellationToken)
            => OperatingSystem.IsMacOS() ? SynchronizeAsync(directory, restore, new SecurityKeychain(), cancellationToken) : Task.CompletedTask;

        internal static async Task SynchronizeAsync(string directory, bool restore, IClaudeKeychain keychain, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            ArgumentNullException.ThrowIfNull(keychain);
            string service = ServiceName(directory);
            string path = Path.Combine(directory, HarnessCatalog.CredentialFile(HarnessKind.Claude));
            string? file = File.Exists(path) ? PrivateFiles.Read(path).Trim() : null;
            string? live;
            try { live = (await keychain.ReadAsync(service, cancellationToken).ConfigureAwait(false))?.Trim(); }
            catch (IOException) { throw new InvalidOperationException("Unable to read this profile's login from macOS Keychain. Unlock your login keychain and try again."); }
            long? liveFreshness = Freshness(live);
            long? fileFreshness = Freshness(file);
            if (liveFreshness != null && (fileFreshness == null || liveFreshness.Value >= fileFreshness.Value))
            {
                // Claude refreshed or replaced the login while it ran. Keep the durable copy current.
                if (!string.Equals(live, file, StringComparison.Ordinal)) PrivateFiles.Write(path, live!);
                return;
            }
            if (restore && fileFreshness != null) await keychain.WriteAsync(service, file!, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Returns a usable Claude login's access-token expiry in Unix milliseconds, or null when it cannot sign in.</summary>
        internal static long? Freshness(string? credential)
        {
            if (string.IsNullOrWhiteSpace(credential)) return null;
            try { CredentialFiles.Validate(HarnessKind.Claude, credential); }
            catch (InvalidDataException) { return null; }
            using JsonDocument document = JsonDocument.Parse(credential);
            return document.RootElement.GetProperty("claudeAiOauth").TryGetProperty("expiresAt", out JsonElement expiry)
                && expiry.ValueKind == JsonValueKind.Number && expiry.TryGetInt64(out long milliseconds) ? milliseconds : 0;
        }

        /// <summary>Deletes only the native keychain item belonging to an explicit Kameo profile directory.</summary>
        public static void DeleteScoped(string directory)
        {
            if (!OperatingSystem.IsMacOS()) return;
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            using Process process = new Process { StartInfo = new ProcessStartInfo("/usr/bin/security") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true } };
            foreach (string argument in new[] { "delete-generic-password", "-s", ServiceName(directory), "-a", Account() }) process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            if (!process.WaitForExit(5000)) { process.Kill(true); throw new IOException("Keychain did not respond. Unlock it and retry."); }
            if (process.ExitCode != 0 && process.ExitCode != _ItemNotFound) throw new IOException("The profile's Keychain entry could not be deleted. Unlock Keychain and retry.");
        }

        /// <summary>Reads an explicitly requested keychain service, or null when it cannot be read. Never logs the password or invokes a shell.</summary>
        public static async Task<string?> ReadAsync(string service, CancellationToken cancellationToken)
        {
            try { return await ReadStrictAsync(service, cancellationToken).ConfigureAwait(false); }
            catch (IOException) { return null; }
        }

        /// <summary>Reads a keychain service, returning null only when the item does not exist.</summary>
        /// <exception cref="IOException">The Keychain was locked, denied access, or did not respond.</exception>
        internal static async Task<string?> ReadStrictAsync(string service, CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Keychain access is available on macOS only.");
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using Process process = new Process { StartInfo = new ProcessStartInfo("/usr/bin/security") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true } };
            foreach (string argument in new[] { "find-generic-password", "-s", service, "-w" }) process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            try
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                Task<string> errors = process.StandardError.ReadToEndAsync(timeout.Token);
                try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new IOException("Keychain did not respond."); }
                string value = await output.ConfigureAwait(false);
                await errors.ConfigureAwait(false);
                if (process.ExitCode == _ItemNotFound) return null;
                if (process.ExitCode != 0) throw new IOException("Keychain could not be read.");
                return value.Trim();
            }
            finally { if (!process.HasExited) process.Kill(true); }
        }

        /// <summary>Writes a keychain service through <c>security</c>'s standard input so the value never appears in argv.</summary>
        internal static async Task WriteAsync(string service, string credential, CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Keychain access is available on macOS only.");
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            cancellationToken = timeout.Token;
            string account = Account();
            if (account.IndexOfAny(new[] { '"', '\\', '\n', '\r' }) >= 0) throw new InvalidOperationException("The system account name cannot be used for Keychain synchronization.");
            using Process process = new Process { StartInfo = new ProcessStartInfo("/usr/bin/security") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            process.StartInfo.ArgumentList.Add("-i");
            process.Start();
            try
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
                Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.StandardInput.WriteLineAsync(("add-generic-password -U -a \"" + account + "\" -s \"" + service + "\" -X \"" + Convert.ToHexString(Encoding.UTF8.GetBytes(credential)) + "\"").AsMemory(), cancellationToken).ConfigureAwait(false);
                process.StandardInput.Close();
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                await output.ConfigureAwait(false);
                await error.ConfigureAwait(false);
                if (process.ExitCode != 0) throw new InvalidOperationException("Unable to synchronize this profile with macOS Keychain. Unlock your login keychain and try again.");
            }
            finally { if (!process.HasExited) process.Kill(true); }
            string? restored = await ReadAsync(service, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(restored, credential.Trim(), StringComparison.Ordinal))
                throw new InvalidOperationException("macOS Keychain did not retain this profile's credentials. Unlock your login keychain and try again.");
        }

        private static string Account() => Environment.GetEnvironmentVariable("USER") ?? Environment.UserName;
    }
}
