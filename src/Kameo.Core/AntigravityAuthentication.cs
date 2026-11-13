namespace Kameo.Core
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Nodes;

    /// <summary>Configures Antigravity's profile directory and file-backed Google account authentication.</summary>
    public static class AntigravityAuthentication
    {
        /// <summary>Prepares API or account authentication without accessing the shared OS keyring.</summary>
        public static void Configure(ProcessStartInfo start, Profile profile, string directory)
        {
            ArgumentNullException.ThrowIfNull(start);
            ArgumentNullException.ThrowIfNull(profile);
            foreach (string name in start.Environment.Keys.ToArray())
                if (name.StartsWith("ANTIGRAVITY_", StringComparison.Ordinal) || name.StartsWith("AGY_LLM_GATEWAY_", StringComparison.Ordinal)
                    || name.StartsWith("AGY_CDE_", StringComparison.Ordinal) || name is "AGY_ACCOUNT" or "AGY_ADC_AUTH") start.Environment.Remove(name);
            start.ArgumentList.Add("--gemini_dir"); start.ArgumentList.Add(directory);
            string settingsPath = Path.Combine(directory, "antigravity-cli", "settings.json");
            JsonObject settings = File.Exists(settingsPath)
                ? JsonNode.Parse(PrivateFiles.Read(settingsPath)) as JsonObject ?? throw new InvalidDataException("Antigravity settings must be a JSON object.")
                : new JsonObject();
            JsonObject overrides = new JsonObject();
            if (profile.Kind == CredentialKind.ApiKey)
            {
                start.Environment["GEMINI_API_KEY"] = profile.ApiKey;
                settings["modelProvider"] = "gemini";
                overrides["modelProvider"] = "gemini";
            }
            else
            {
                start.Environment.Remove("GEMINI_API_KEY");
                // In CLI 1.5.7, this detector selects file-only load/save/remove paths for the entire session.
                if (!start.Environment.TryGetValue("SSH_CONNECTION", out string? connection) || string.IsNullOrWhiteSpace(connection))
                    start.Environment["SSH_CONNECTION"] = "127.0.0.1 1 127.0.0.1 22";
                start.Environment["AGY_CLI_DISABLE_AUTO_UPDATE"] = "true";
                settings.Remove("modelProvider");
                overrides["modelProvider"] = "";
                ProtectCredentialFile(directory);
            }
            start.ArgumentList.Add("--settings"); start.ArgumentList.Add(overrides.ToJsonString());
            PrivateFiles.Write(settingsPath, settings.ToJsonString());
        }

        /// <summary>Checks the token path and preserves owner-only permissions without rewriting refreshed tokens.</summary>
        public static void ProtectCredentialFile(string directory)
        {
            string path = Path.Combine(directory, HarnessCatalog.CredentialFile(HarnessKind.Antigravity));
            PrivateFiles.CheckPath(path);
            if (File.Exists(path) && !OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        /// <summary>Rejects directory, authentication, background-service and updater overrides.</summary>
        public static bool OverridesIsolation(string argument)
        {
            ArgumentNullException.ThrowIfNull(argument);
            string name = argument.TrimStart('-').Split('=')[0].Replace('_', '-');
            return name is "gemini-dir" or "app-data-dir" or "cli-dir-name" or "config-rel-dir" or "product" or "settings"
                or "remote-agent" or "remote-control" or "flagfile" or "undefok" or "bg-updater" or "release-base-url"
                or "update" or "install";
        }
    }
}
