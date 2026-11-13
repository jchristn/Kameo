namespace Kameo.Core
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Builds isolated harness environments and runs child processes without a shell.</summary>
    public sealed class LaunchService
    {
        private readonly ProfileStore _Store;
        private static readonly string[] _CredentialVariables =
        {
            "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_BASE_URL", "ANTHROPIC_CUSTOM_HEADERS",
            "CLAUDE_CODE_OAUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR", "CLAUDE_CODE_API_KEY_FILE_DESCRIPTOR",
            "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY", "CLAUDE_CONFIG_DIR",
            "OPENAI_API_KEY", "OPENAI_BASE_URL", "CODEX_API_KEY", "CODEX_HOME", "CODEX_ACCESS_TOKEN",
            "GEMINI_API_KEY", "GOOGLE_API_KEY", "GOOGLE_APPLICATION_CREDENTIALS", "GOOGLE_CLOUD_ACCESS_TOKEN",
            "GOOGLE_GENAI_USE_VERTEXAI", "GOOGLE_GENAI_USE_GCA", "GOOGLE_GEMINI_BASE_URL", "GEMINI_CLI_HOME",
            "GEMINI_FORCE_ENCRYPTED_FILE_STORAGE", "GEMINI_FORCE_FILE_STORAGE", "MUX_CONFIG_DIR", "KAMEO_MUX_API_KEY",
            "AGY_ADC_AUTH", "AGY_ACCOUNT", "AGY_LLM_GATEWAY_API_KEY", "AGY_LLM_GATEWAY_URL", "AGY_LLM_GATEWAY_HEADERS",
            "AGY_CDE_ENTERPRISE_AUTH", "AGY_CDE_SEAMLESS_AUTH", "ANTIGRAVITY_GEMINI_DIR", "ANTIGRAVITY_CONFIG_REL_DIR",
            "ANTIGRAVITY_APP_DATA_DIR", "ANTIGRAVITY_CREDENTIAL_SCOPE"
        };

        /// <summary>Creates a launcher.</summary>
        public LaunchService(ProfileStore store) => _Store = store ?? throw new ArgumentNullException(nameof(store));

        /// <summary>Prepares a child environment. Call under the profile lease before starting the process.</summary>
        /// <exception cref="ArgumentException">Arguments attempt to override profile isolation.</exception>
        public LaunchPlan Prepare(Profile profile, IReadOnlyList<string> arguments, bool login = false, string? workingDirectory = null)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(arguments);
            string directory = _Store.ProfileDirectory(profile.Id);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The profile directory is missing. Recreate the profile or restore a backup.");
            ValidateArguments(profile.Harness, arguments);
            if (login && profile.Kind != CredentialKind.Native) throw new InvalidOperationException("API-key profiles do not need a browser login.");
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(profile.Executable) ? HarnessCatalog.Executable(profile.Harness) : profile.Executable,
                WorkingDirectory = Path.GetFullPath(workingDirectory ?? Environment.CurrentDirectory), UseShellExecute = false
            };
            foreach (string name in _CredentialVariables) start.Environment.Remove(name);
            start.Environment["KAMEO_PROFILE_ID"] = profile.Id;
            start.Environment["KAMEO_HOME"] = _Store.Root;

            switch (profile.Harness)
            {
                case HarnessKind.Claude:
                    start.Environment["CLAUDE_CONFIG_DIR"] = directory;
                    if (profile.Kind == CredentialKind.ApiKey) start.Environment["ANTHROPIC_API_KEY"] = profile.ApiKey;
                    if (login) { start.ArgumentList.Add("auth"); start.ArgumentList.Add("login"); }
                    break;
                case HarnessKind.Codex:
                    start.Environment["CODEX_HOME"] = directory;
                    start.ArgumentList.Add("-c"); start.ArgumentList.Add("cli_auth_credentials_store=\"file\"");
                    if (profile.Kind == CredentialKind.ApiKey)
                    {
                        JsonObject auth = new JsonObject { ["OPENAI_API_KEY"] = profile.ApiKey };
                        PrivateFiles.Write(Path.Combine(directory, "auth.json"), auth.ToJsonString());
                        start.Environment["OPENAI_API_KEY"] = profile.ApiKey;
                    }
                    if (login) start.ArgumentList.Add("login");
                    break;
                case HarnessKind.Antigravity:
                    AntigravityAuthentication.Configure(start, profile, directory);
                    break;
                case HarnessKind.Mux:
                    start.Environment["MUX_CONFIG_DIR"] = directory;
                    if (profile.MuxEndpointsFile.Length > 0) MuxEndpointLink.Refresh(_Store, profile);
                    if (profile.Kind == CredentialKind.ApiKey)
                    {
                        if (!File.Exists(Path.Combine(directory, "endpoints.json"))) throw new InvalidOperationException("Configure a model and endpoint for this Mux profile first.");
                        start.Environment["KAMEO_MUX_API_KEY"] = profile.ApiKey;
                    }
                    // Mux starts its first-run wizard in the interactive TUI. "setup" would be a prompt.
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(profile));
            }
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            return new LaunchPlan(profile.Id, start);
        }

        /// <summary>Runs a selected profile, preserves arguments and exit codes, and kills the child tree on cancellation.</summary>
        public async Task<int> RunAsync(string id, IReadOnlyList<string> arguments, bool login, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using FileStream lease = _Store.AcquireLease(id);
            Profile profile = _Store.Get(id);
            LaunchPlan plan = Prepare(profile, arguments, login);
            ProcessStartInfo resolved = ExecutableResolver.Resolve(plan.StartInfo);
            if (profile.Harness == HarnessKind.Antigravity && profile.Kind == CredentialKind.Native)
                await AntigravityCompatibility.VerifyAsync(resolved, _Store.ProfileDirectory(id), cancellationToken).ConfigureAwait(false);
            if (profile.Harness == HarnessKind.Claude && profile.Kind == CredentialKind.Native && OperatingSystem.IsMacOS())
                await MacCredentialBridge.RestoreAsync(_Store.ProfileDirectory(id), cancellationToken).ConfigureAwait(false);
            using Process process = new Process { StartInfo = resolved };
            process.Start();
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                return process.ExitCode;
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                if (profile.Harness == HarnessKind.Claude && profile.Kind == CredentialKind.Native && OperatingSystem.IsMacOS())
                    await MacCredentialBridge.SnapshotAsync(_Store.ProfileDirectory(id), cancellationToken: CancellationToken.None).ConfigureAwait(false);
                if (profile.Harness == HarnessKind.Antigravity && profile.Kind == CredentialKind.Native)
                    AntigravityAuthentication.ProtectCredentialFile(_Store.ProfileDirectory(id));
            }
        }

        private static void ValidateArguments(HarnessKind harness, IReadOnlyList<string> arguments)
        {
            foreach (string argument in arguments)
            {
                ArgumentNullException.ThrowIfNull(argument);
                bool blocked = harness switch
                {
                    HarnessKind.Mux => argument == "--config-dir" || argument.StartsWith("--config-dir=", StringComparison.Ordinal),
                    HarnessKind.Claude => argument == "--settings" || argument.StartsWith("--settings=", StringComparison.Ordinal),
                    HarnessKind.Codex => argument.Contains("cli_auth_credentials_store", StringComparison.Ordinal) || argument.Contains("forced_login_method", StringComparison.Ordinal),
                    HarnessKind.Antigravity => AntigravityAuthentication.OverridesIsolation(argument),
                    _ => false
                };
                if (blocked) throw new ArgumentException("That argument overrides Kameo's profile authentication or directory. Configure the profile in Kameo instead.", nameof(arguments));
            }
        }
    }
}
