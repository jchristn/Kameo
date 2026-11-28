namespace Kameo.Core
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Gates native account launches on a minimum CLI version: the first release whose file-based token storage Kameo
    /// relies on for account isolation. Any later release, including new minor and major versions, is accepted.
    /// </summary>
    /// <remarks>Thread safe. <see cref="MinimumVersion"/> is read once per check.</remarks>
    public static class AntigravityCompatibility
    {
        private static readonly Regex _VersionNumber = new Regex(@"(?<![\d.])(\d+)\.(\d+)(?:\.(\d+))?(?![\d])", RegexOptions.CultureInvariant);
        private static Version _MinimumVersion = new Version(1, 5, 7);

        /// <summary>
        /// The earliest accepted CLI version. Default 1.5.7, the first release whose per-directory token file Kameo verified.
        /// Earlier releases are rejected because their token storage was never verified and could mix accounts between
        /// profiles. Must not be null.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public static Version MinimumVersion
        {
            get => Volatile.Read(ref _MinimumVersion);
            set => Volatile.Write(ref _MinimumVersion, value ?? throw new ArgumentNullException(nameof(value)));
        }

        /// <summary>The earliest accepted CLI version as display text, for example <c>1.5.7</c>. Never null.</summary>
        public static string SupportedVersion => Format(MinimumVersion);

        /// <summary>
        /// Whether version output names an accepted release: the first <c>MAJOR.MINOR[.PATCH]</c> number in the text is at
        /// least <see cref="MinimumVersion"/>. Prefixes and suffixes such as <c>agy 1.6.0 (abc123)</c> are ignored.
        /// </summary>
        /// <param name="version">The CLI's version output. Must not be null.</param>
        /// <returns>True for an accepted release; false when no version number is found or it is too old.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="version"/> is null.</exception>
        public static bool IsSupported(string version)
        {
            ArgumentNullException.ThrowIfNull(version);
            Match match = _VersionNumber.Match(version);
            if (!match.Success) return false;
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int major)
                || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minor))
                return false;
            int patch = 0;
            if (match.Groups[3].Success && !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out patch)) return false;
            return new Version(major, minor, patch) >= Normalize(MinimumVersion);
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
                    throw new InvalidOperationException(Message());
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
                if (result.Length + count > 4096) throw new InvalidOperationException(Message());
                result.Append(buffer, 0, count);
            }
            return result.ToString();
        }

        private static string Message() => "Google account profiles require Antigravity CLI " + SupportedVersion
            + " or later. This executable did not report a supported version. Update agy, or set the profile executable to the agy binary. No account session was started.";

        private static Version Normalize(Version version) => new Version(version.Major, version.Minor, Math.Max(version.Build, 0));

        private static string Format(Version version) => Normalize(version).ToString(3);
    }
}
