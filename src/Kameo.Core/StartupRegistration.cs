namespace Kameo.Core
{
    using System;
    using System.IO;
    using System.Security;
    using System.Text;

    /// <summary>Per-user startup registration for source builds. Does not require administrative privileges.</summary>
    public static class StartupRegistration
    {
        private const string BeginMarker = "# BEGIN KAMEO STARTUP";
        private const string EndMarker = "# END KAMEO STARTUP";

        /// <summary>Registers the supplied desktop executable for the user's next desktop or WSL shell login.</summary>
        public static void Install(string platform, string executable, string root, string? userHome = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(executable);
            if (!File.Exists(executable)) throw new FileNotFoundException("Build Kameo before registering startup.");
            string home = userHome ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string path = RegistrationPath(platform, home);
            string content;
            switch (platform)
            {
                case "windows":
                    content = "@echo off\r\nsetlocal DisableDelayedExpansion\r\nset \"KAMEO_HOME=" + Batch(root) + "\"\r\nstart \"\" \"" + Batch(executable) + "\" --tray\r\n";
                    break;
                case "macos":
                    content = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\"><plist version=\"1.0\"><dict><key>Label</key><string>com.kameo.tray</string><key>ProgramArguments</key><array><string>" + SecurityElement.Escape(executable) + "</string><string>--tray</string></array><key>EnvironmentVariables</key><dict><key>KAMEO_HOME</key><string>" + SecurityElement.Escape(root) + "</string></dict><key>RunAtLoad</key><true/></dict></plist>";
                    break;
                case "linux":
                    content = "[Desktop Entry]\nType=Application\nName=Kameo\nComment=Agent credential profiles\nExec=env " + DesktopQuote("KAMEO_HOME=" + root) + " " + DesktopQuote(executable) + " --tray\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";
                    break;
                case "wsl": case "wsl2":
                    string cli = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "cli", "kameo"));
                    content = "#!/bin/sh\nif [ -n \"${DISPLAY:-}${WAYLAND_DISPLAY:-}\" ] && ! KAMEO_HOME=" + Shell(root) + " " + Shell(cli) + " desktop-status >/dev/null 2>&1; then\n  KAMEO_HOME=" + Shell(root) + " nohup " + Shell(executable) + " --tray >/dev/null 2>&1 &\nfi\n";
                    foreach (string shellFile in new[] { ".bashrc", ".zshrc" })
                    {
                        string shellPath = Path.Combine(home, shellFile);
                        string original = File.Exists(shellPath) ? File.ReadAllText(shellPath) : "";
                        string block = BeginMarker + "\n[ ! -f " + Shell(path) + " ] || . " + Shell(path) + "\n" + EndMarker;
                        WriteConfig(shellPath, StripBlock(original).TrimEnd() + "\n\n" + block + "\n");
                    }
                    break;
                default: throw new ArgumentException("Unsupported startup platform.", nameof(platform));
            }
            WriteConfig(path, content);
        }

        /// <summary>Removes only Kameo's startup entry and managed WSL shell blocks.</summary>
        public static void Remove(string platform, string? userHome = null)
        {
            string home = userHome ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string path = RegistrationPath(platform, home);
            PrivateFiles.CheckPath(path);
            if (File.Exists(path)) File.Delete(path);
            if (platform is "wsl" or "wsl2")
                foreach (string shellFile in new[] { ".bashrc", ".zshrc" })
                {
                    string shellPath = Path.Combine(home, shellFile);
                    if (File.Exists(shellPath)) WriteConfig(shellPath, StripBlock(File.ReadAllText(shellPath)));
                }
        }

        /// <summary>Returns the per-user registration path, useful for diagnostics.</summary>
        public static string RegistrationPath(string platform, string home) => platform switch
        {
            "windows" => Path.Combine(home, "AppData", "Roaming", "Microsoft", "Windows", "Start Menu", "Programs", "Startup", "Kameo.cmd"),
            "macos" => Path.Combine(home, "Library", "LaunchAgents", "com.kameo.tray.plist"),
            "linux" => Path.Combine(home, ".config", "autostart", "kameo.desktop"),
            "wsl" or "wsl2" => Path.Combine(home, ".config", "kameo", "startup.sh"),
            _ => throw new ArgumentException("Unsupported startup platform.", nameof(platform))
        };

        private static void WriteConfig(string path, string content)
        {
            // Shared configuration directories retain their existing permissions.
            PrivateFiles.CheckPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private static string StripBlock(string content)
        {
            int start = content.IndexOf(BeginMarker, StringComparison.Ordinal);
            if (start < 0) return content;
            int end = content.IndexOf(EndMarker, start, StringComparison.Ordinal);
            if (end < 0) throw new InvalidDataException("The Kameo startup block is incomplete. Restore its END KAMEO STARTUP marker before retrying.");
            return content.Remove(start, end + EndMarker.Length - start);
        }
        private static string Shell(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
        private static string Batch(string value)
        {
            if (value.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0) throw new ArgumentException("The startup path contains invalid characters.");
            return value.Replace("%", "%%", StringComparison.Ordinal);
        }
        private static string DesktopQuote(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("$", "\\$", StringComparison.Ordinal).Replace("`", "\\`", StringComparison.Ordinal).Replace("%", "%%", StringComparison.Ordinal) + "\"";
    }
}
