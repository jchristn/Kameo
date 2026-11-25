namespace Kameo.Desktop
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using Kameo.Core;

    internal static class TerminalLauncher
    {
        internal static void Open(ProfileStore store, string id, bool login, string workingDirectory)
        {
            string cli = FindCli();
            string[] args = { "--home", store.Root, login ? "login" : "run-profile", id };
            if (OperatingSystem.IsWindows())
            {
                string directory = Path.Combine(store.Root, "launchers");
                PrivateFiles.DirectoryCreate(directory);
                string script = Path.Combine(directory, id + ".cmd");
                StringBuilder command = new StringBuilder();
                command.Append(QuoteWindows(cli));
                foreach (string argument in args) command.Append(' ').Append(QuoteWindows(argument));
                StringBuilder body = new StringBuilder();
                body.Append("@echo off\r\n");
                body.Append("cd /d ").Append(QuoteWindows(workingDirectory)).Append("\r\n");
                body.Append(command).Append("\r\n");
                body.Append("set STATUS=%ERRORLEVEL%\r\n");
                body.Append("echo.\r\n");
                body.Append("echo Session ended ^(exit %STATUS%^). Press any key to close.\r\n");
                body.Append("pause >nul\r\n");
                body.Append("exit /b %STATUS%\r\n");
                PrivateFiles.Write(script, body.ToString());
                // ShellExecute on a .cmd allocates a fresh console so the user can see output and respond to interactive prompts.
                ProcessStartInfo info = new ProcessStartInfo(script) { UseShellExecute = true, WorkingDirectory = workingDirectory };
                Process.Start(info)?.Dispose();
            }
            else if (OperatingSystem.IsMacOS())
            {
                string directory = Path.Combine(store.Root, "launchers");
                PrivateFiles.DirectoryCreate(directory);
                string script = Path.Combine(directory, id + ".command");
                string command = Quote(cli);
                foreach (string argument in args) command += " " + Quote(argument);
                PrivateFiles.Write(script, "#!/bin/sh\ncd " + Quote(workingDirectory) + " || exit 1\n" + command + "\nstatus=$?\nprintf '\\nSession ended (exit %s). Press Return to close. ' \"$status\"\nread -r answer\nexit \"$status\"\n");
                File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                ProcessStartInfo info = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
                foreach (string argument in new[] { "-a", "Terminal", script }) info.ArgumentList.Add(argument);
                Process.Start(info)?.Dispose();
            }
            else
            {
                string? terminal = ExecutableResolver.Find("x-terminal-emulator") ?? ExecutableResolver.Find("gnome-terminal") ?? ExecutableResolver.Find("konsole") ?? ExecutableResolver.Find("xterm");
                if (terminal == null) throw new InvalidOperationException("No terminal was found. Install xterm, or run the copied Kameo command in your terminal.");
                ProcessStartInfo info = new ProcessStartInfo(terminal) { UseShellExecute = false, WorkingDirectory = workingDirectory };
                info.ArgumentList.Add(Path.GetFileName(terminal) == "gnome-terminal" ? "--" : "-e");
                info.ArgumentList.Add(cli);
                foreach (string argument in args) info.ArgumentList.Add(argument);
                Process.Start(info)?.Dispose();
            }
        }

        private static string Quote(string text) => "'" + text.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
        private static string QuoteWindows(string text) => "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        private static string FindCli()
        {
            string filename = OperatingSystem.IsWindows() ? "kameo.exe" : "kameo";
            // Installers place the CLI beside the desktop executable (and inside the macOS bundle's MacOS directory).
            string installed = Path.Combine(AppContext.BaseDirectory, filename);
            if (File.Exists(installed)) return installed;
            string sibling = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "cli", filename));
            if (File.Exists(sibling)) return sibling;
            string bundleSibling = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "cli", filename));
            if (File.Exists(bundleSibling)) return bundleSibling;
            string development = AppContext.BaseDirectory.Replace(Path.DirectorySeparatorChar + "Kameo.Desktop" + Path.DirectorySeparatorChar, Path.DirectorySeparatorChar + "Kameo.Cli" + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            if (File.Exists(Path.Combine(development, filename))) return Path.Combine(development, filename);
            return ExecutableResolver.Find("kameo") ?? throw new FileNotFoundException("Build the CLI with scripts/<platform>/update before launching a harness.");
        }
    }
}
