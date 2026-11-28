namespace Kameo.Core
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.RegularExpressions;

    /// <summary>Resolves native commands and npm Windows shims without passing user arguments through cmd.exe.</summary>
    public static class ExecutableResolver
    {
        // Matches the shim's real invocation: a %dp0%-rooted .exe/.js target immediately before the "%*" argument
        // forwarder. Anchoring on "%*" skips the "IF EXIST \"%dp0%\node.exe\"" probe that modern npm .cmd shims emit
        // first, which would otherwise resolve the harness to node.exe. Resolve() routes .js/.cjs/.mjs through node.
        private static readonly Regex _NpmScript = new Regex("\"%~?dp0%?[\\\\/]?([^\"]+\\.(?:exe|js|cjs|mjs))\"\\s*%\\*", RegexOptions.IgnoreCase);

        /// <summary>Resolves a child command. JavaScript entries are run with node and separate argument values.</summary>
        public static ProcessStartInfo Resolve(ProcessStartInfo start)
        {
            ArgumentNullException.ThrowIfNull(start);
            string resolved = Find(start.FileName) ?? throw new FileNotFoundException("The harness executable was not found. Install it on PATH or set its executable path in the profile.");
            string extension = Path.GetExtension(resolved);
            if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
            {
                Match match = _NpmScript.Match(File.ReadAllText(resolved));
                if (!match.Success) throw new InvalidOperationException("This batch launcher is not an npm shim. Set the profile executable to its .exe or JavaScript entry point.");
                resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(resolved)!, match.Groups[1].Value));
                if (!File.Exists(resolved)) throw new FileNotFoundException("The npm launcher's target executable is missing.");
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
            // On Windows the npm global bin holds both a .cmd shim and an extensionless sh script; the sh script is not a
            // runnable PE. Probe real Windows extensions first so a bare "claude" resolves to claude.cmd, not the sh file.
            string[] extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat", "" } : new[] { "" };
            foreach (string directory in SearchDirectories())
                foreach (string extension in extensions)
                {
                    string path = Path.Combine(directory, command + extension);
                    if (File.Exists(path)) return path;
                }
            return null;
        }

        // Lists the process PATH directories first, then on Windows the persisted user and machine PATH. A desktop
        // session launched before an installer appended to PATH keeps a stale process copy, so a harness the user can
        // already run from a new terminal is still resolved. Each directory is visited once, in order.
        private static List<string> SearchDirectories()
        {
            List<string> directories = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string?[] sources = OperatingSystem.IsWindows()
                ? new[] { Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User), Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) }
                : new[] { Environment.GetEnvironmentVariable("PATH") };
            foreach (string? source in sources)
                foreach (string directory in (source ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = directory.Trim('"');
                    if (trimmed.Length > 0 && seen.Add(trimmed)) directories.Add(trimmed);
                }
            return directories;
        }
    }
}
