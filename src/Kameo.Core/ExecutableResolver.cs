namespace Kameo.Core
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Text.RegularExpressions;

    /// <summary>Resolves native commands and npm Windows shims without passing user arguments through cmd.exe.</summary>
    public static partial class ExecutableResolver
    {
        /// <summary>Resolves a child command. JavaScript entries are run with node and separate argument values.</summary>
        public static ProcessStartInfo Resolve(ProcessStartInfo start)
        {
            ArgumentNullException.ThrowIfNull(start);
            string resolved = Find(start.FileName) ?? throw new FileNotFoundException("The harness executable was not found. Install it on PATH or set its executable path in the profile.");
            string extension = Path.GetExtension(resolved);
            if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
            {
                Match match = NpmScript().Match(File.ReadAllText(resolved));
                if (!match.Success) throw new InvalidOperationException("This batch launcher is not an npm shim. Set the profile executable to its .exe or JavaScript entry point.");
                resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(resolved)!, match.Groups[1].Value));
                if (!File.Exists(resolved)) throw new FileNotFoundException("The npm launcher's JavaScript entry point is missing.");
                extension = Path.GetExtension(resolved);
            }
            if (extension is ".js" or ".cjs" or ".mjs")
            {
                start.FileName = Find("node") ?? throw new FileNotFoundException("Node.js is required to launch this harness.");
                start.ArgumentList.Insert(0, resolved);
            }
            else start.FileName = resolved;
            return start;
        }

        /// <summary>Finds an executable by absolute path or PATH, including standard Windows extensions.</summary>
        public static string? Find(string command)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(command);
            if (Path.IsPathRooted(command)) return File.Exists(command) ? command : null;
            if (command.Contains(Path.DirectorySeparatorChar) || command.Contains(Path.AltDirectorySeparatorChar))
                return File.Exists(command) ? Path.GetFullPath(command) : null;
            string[] extensions = OperatingSystem.IsWindows() ? new[] { "", ".exe", ".cmd", ".bat" } : new[] { "" };
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                foreach (string extension in extensions)
                {
                    string path = Path.Combine(directory.Trim('"'), command + extension);
                    if (File.Exists(path)) return path;
                }
            return null;
        }

        [GeneratedRegex("\"%~?dp0%?[\\\\/]([^\"]+\\.(?:js|cjs|mjs))\"", RegexOptions.IgnoreCase)]
        private static partial Regex NpmScript();
    }
}
