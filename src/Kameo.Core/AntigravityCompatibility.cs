namespace Kameo.Core
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Gates native account launches on the CLI version whose file-storage behavior was verified.</summary>
    public static class AntigravityCompatibility
    {
        /// <summary>The earliest CLI version verified for account isolation. Later 1.5.x patch releases are accepted.</summary>
        public const string SupportedVersion = "1.5.7";
        private const string _Message = "Google account profiles require Antigravity CLI " + SupportedVersion
            + " or a later 1.5.x release. This executable did not report a verified version. Set the profile executable to a supported agy binary, or update Kameo for newer CLI support. No account session was started.";

        /// <summary>Whether a reported version is a verified release: the 1.5 line at patch 7 or later.</summary>
        public static bool IsSupported(string version)
        {
            ArgumentNullException.ThrowIfNull(version);
            string[] parts = version.Trim().Split('.');
            return parts.Length == 3
                && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) && major == 1
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor) && minor == 5
                && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int patch) && patch >= 7;
        }

        /// <summary>Runs a bounded version probe using the profile's isolated environment, with a five-second timeout.</summary>
        public static async Task VerifyAsync(ProcessStartInfo configured, string directory, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(configured);
            cancellationToken.ThrowIfCancellationRequested();
            ProcessStartInfo probe = new ProcessStartInfo(configured.FileName)
            {
                WorkingDirectory = configured.WorkingDirectory, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, CreateNoWindow = true
            };
            probe.Environment.Clear();
            foreach (KeyValuePair<string, string?> entry in configured.Environment) probe.Environment[entry.Key] = entry.Value;
            probe.ArgumentList.Add("--gemini_dir"); probe.ArgumentList.Add(directory); probe.ArgumentList.Add("--version");
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using Process process = new Process { StartInfo = probe };
            process.Start();
            process.StandardInput.Close();
            try
            {
                Task<string> output = ReadBoundedAsync(process.StandardOutput, timeout.Token);
                Task<string> error = ReadBoundedAsync(process.StandardError, timeout.Token);
                await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
                if (process.ExitCode != 0 || !IsSupported(await output.ConfigureAwait(false)))
                    throw new InvalidOperationException(_Message);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new InvalidOperationException("Antigravity's version check timed out. No account session was started. Verify the profile executable and try again."); }
            finally
            {
                if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
            }
        }

        private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
        {
            char[] buffer = new char[256];
            StringBuilder result = new StringBuilder();
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (result.Length + count > 4096) throw new InvalidOperationException(_Message);
                result.Append(buffer, 0, count);
            }
            return result.ToString();
        }
    }
}
