namespace Kameo.Core
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The Claude account a native profile is signed in to, read from non-secret fields of its saved login and Claude
    /// Code's profile state. Claude keeps the account identifier even after it clears a login's tokens, so a later
    /// sign-in can be compared with the account that was there before.
    /// </summary>
    public sealed class ClaudeAccount
    {
        /// <summary>Anthropic's account identifier, or empty when unknown.</summary>
        public string Id { get; init; } = "";

        /// <summary>The account's email address, or empty when unknown.</summary>
        public string Email { get; init; } = "";

        /// <summary>Whether neither the identifier nor the email address is known.</summary>
        public bool IsUnknown => Id.Length == 0 && Email.Length == 0;

        /// <summary>Reads the account for a profile directory, using the macOS Keychain where Claude keeps its login.</summary>
        /// <param name="directory">The profile directory.</param>
        /// <param name="cancellationToken">Cancels the Keychain read.</param>
        public static Task<ClaudeAccount> ReadAsync(string directory, CancellationToken cancellationToken)
            => ReadAsync(directory, OperatingSystem.IsMacOS() ? new SecurityKeychain() : null, cancellationToken);

        internal static async Task<ClaudeAccount> ReadAsync(string directory, IClaudeKeychain? keychain, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            string id = "";
            string? live = null;
            if (keychain != null)
            {
                try { live = await keychain.ReadAsync(MacCredentialBridge.ServiceName(directory), cancellationToken).ConfigureAwait(false); }
                catch (IOException) { }
            }
            string path = Path.Combine(directory, HarnessCatalog.CredentialFile(HarnessKind.Claude));
            string? file = null;
            try { file = File.Exists(path) ? PrivateFiles.Read(path) : null; }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { }
            // The fresher usable login names the account. A cleared login still carries the identifier of the account it held.
            long? liveFreshness = MacCredentialBridge.Freshness(live);
            long? fileFreshness = MacCredentialBridge.Freshness(file);
            bool liveFirst = (liveFreshness != null && (fileFreshness == null || liveFreshness.Value >= fileFreshness.Value)) || (fileFreshness == null && live != null);
            foreach (string? credential in liveFirst ? new[] { live, file } : new[] { file, live })
            {
                id = Field(credential, "claudeAiOauth", "accountUuid");
                if (id.Length > 0) break;
            }
            string email = "";
            string state = Path.Combine(directory, ".claude.json");
            try { if (File.Exists(state)) email = Field(PrivateFiles.Read(state), "oauthAccount", "emailAddress"); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { }
            return new ClaudeAccount { Id = id, Email = email };
        }

        /// <summary>
        /// Whether two readings name different accounts. Identifiers are compared when both are known, otherwise email
        /// addresses, ignoring case. An unknown reading is never different.
        /// </summary>
        public static bool AreDifferent(ClaudeAccount before, ClaudeAccount after)
        {
            ArgumentNullException.ThrowIfNull(before);
            ArgumentNullException.ThrowIfNull(after);
            if (before.Id.Length > 0 && after.Id.Length > 0) return !string.Equals(before.Id, after.Id, StringComparison.Ordinal);
            if (before.Email.Length > 0 && after.Email.Length > 0) return !string.Equals(before.Email, after.Email, StringComparison.OrdinalIgnoreCase);
            return false;
        }

        private static string Field(string? json, string section, string name)
        {
            if (string.IsNullOrWhiteSpace(json)) return "";
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(section, out JsonElement parent)
                    && parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                    ? (value.GetString() ?? "").Trim() : "";
            }
            catch (JsonException) { return ""; }
        }
    }
}
