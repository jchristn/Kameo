namespace Kameo.Core
{
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Renews an idle Claude Code sign-in the way Claude Code does, so plan usage keeps working between sessions.
    /// Refresh tokens rotate, so this runs only while Kameo holds the profile's exclusive lease: any running session
    /// holds a shared lease and keeps ownership of its tokens. The new login is saved to the profile file first and then
    /// to the scoped Keychain item; a launch restores the file over an older Keychain copy if the second write fails.
    /// </summary>
    internal static class ClaudeTokenRefresh
    {
        /// <summary>Claude Code's OAuth token endpoint, checked against Claude Code 2.1.332.</summary>
        internal static readonly Uri DefaultEndpoint = new Uri("https://platform.claude.com/v1/oauth/token");

        /// <summary>A sign-in that expires within this window is renewed by a usage check.</summary>
        internal static readonly TimeSpan RenewalWindow = TimeSpan.FromMinutes(30);

        private const string _ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
        private const int _MaximumResponseBytes = 64 * 1024;
        private static readonly JsonSerializerOptions _Output = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>
        /// Renews a profile's sign-in when it expires within <see cref="RenewalWindow"/>, or when its access token is
        /// <paramref name="rejectedAccessToken"/>. Never waits for a running session and never logs token material.
        /// </summary>
        /// <param name="store">The profile store.</param>
        /// <param name="id">A native Claude Code profile.</param>
        /// <param name="client">HTTP client for the token request.</param>
        /// <param name="endpoint">The token endpoint.</param>
        /// <param name="keychain">The macOS Keychain, or null where Claude stores its login only in the profile file.</param>
        /// <param name="clock">Supplies the current time.</param>
        /// <param name="rejectedAccessToken">An access token the usage endpoint rejected, or null.</param>
        /// <param name="cancellationToken">Cancels waiting and the request. Once a new token is issued it is always saved.</param>
        internal static async Task<ClaudeRefreshOutcome> RefreshAsync(ProfileStore store, string id, HttpClient client, Uri endpoint,
            IClaudeKeychain? keychain, TimeProvider clock, string? rejectedAccessToken, CancellationToken cancellationToken)
        {
            FileStream preparation;
            // Serializes with launches, which synchronize the Keychain under the same lease.
            try { preparation = await store.AcquirePreparationLeaseAsync(id, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return ClaudeRefreshOutcome.InUse; }
            using (preparation)
            {
                FileStream lease;
                try { lease = store.AcquireLease(id); }
                catch (InvalidOperationException) { return ClaudeRefreshOutcome.InUse; }
                using (lease)
                    return await RefreshLeasedAsync(store.ProfileDirectory(id), client, endpoint, keychain, clock, rejectedAccessToken, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task<ClaudeRefreshOutcome> RefreshLeasedAsync(string directory, HttpClient client, Uri endpoint,
            IClaudeKeychain? keychain, TimeProvider clock, string? rejectedAccessToken, CancellationToken cancellationToken)
        {
            string path = Path.Combine(directory, HarnessCatalog.CredentialFile(HarnessKind.Claude));
            string service = MacCredentialBridge.ServiceName(directory);
            string? file = File.Exists(path) ? PrivateFiles.Read(path).Trim() : null;
            string? live = null;
            if (keychain != null)
            {
                // The Keychain may hold a newer refresh token. Refreshing an older one would sign the profile out.
                try { live = (await keychain.ReadAsync(service, cancellationToken).ConfigureAwait(false))?.Trim(); }
                catch (IOException) { return ClaudeRefreshOutcome.Failed; }
            }
            long? liveFreshness = MacCredentialBridge.Freshness(live);
            long? fileFreshness = MacCredentialBridge.Freshness(file);
            string? current = liveFreshness != null && (fileFreshness == null || liveFreshness.Value >= fileFreshness.Value) ? live : fileFreshness != null ? file : null;
            if (current == null) return ClaudeRefreshOutcome.Rejected;

            JsonObject root = JsonNode.Parse(current)!.AsObject();
            JsonObject oauth = root["claudeAiOauth"]!.AsObject();
            string accessToken = oauth["accessToken"]!.GetValue<string>();
            string refreshToken = oauth["refreshToken"]!.GetValue<string>();
            long expires = MacCredentialBridge.Freshness(current)!.Value;
            bool rejected = rejectedAccessToken != null && string.Equals(accessToken, rejectedAccessToken, StringComparison.Ordinal);
            if (!rejected && expires > clock.GetUtcNow().Add(RenewalWindow).ToUnixTimeMilliseconds()) return ClaudeRefreshOutcome.NotNeeded;

            JsonObject body = new JsonObject
            {
                ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken,
                ["client_id"] = oauth["clientId"] is JsonValue clientId && clientId.TryGetValue(out string? saved) && !string.IsNullOrWhiteSpace(saved) ? saved : _ClientId
            };
            string scopes = Scopes(oauth);
            if (scopes.Length > 0) body["scope"] = scopes;

            string response;
            try
            {
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Kameo", "0.2.0"));
                using HttpResponseMessage reply = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                response = await ClaudeUsage.ReadBoundedAsync(reply.Content, _MaximumResponseBytes, timeout.Token).ConfigureAwait(false);
                if (reply.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                    return InvalidGrant(response) ? ClaudeRefreshOutcome.Rejected : ClaudeRefreshOutcome.Failed;
                if (!reply.IsSuccessStatusCode) return ClaudeRefreshOutcome.Failed;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return ClaudeRefreshOutcome.Failed; }
            catch (Exception error) when (error is HttpRequestException or InvalidDataException) { return ClaudeRefreshOutcome.Failed; }

            string updated;
            try
            {
                using JsonDocument document = JsonDocument.Parse(response);
                JsonElement reply = document.RootElement;
                if (reply.ValueKind != JsonValueKind.Object
                    || !reply.TryGetProperty("access_token", out JsonElement access) || access.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(access.GetString())
                    || !reply.TryGetProperty("expires_in", out JsonElement lifetime) || lifetime.ValueKind != JsonValueKind.Number || !lifetime.TryGetInt64(out long seconds) || seconds <= 0)
                    return ClaudeRefreshOutcome.Failed;
                long now = clock.GetUtcNow().ToUnixTimeMilliseconds();
                oauth["accessToken"] = access.GetString();
                // The endpoint may keep the existing refresh token by omitting it.
                if (reply.TryGetProperty("refresh_token", out JsonElement rotated) && rotated.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(rotated.GetString()))
                    oauth["refreshToken"] = rotated.GetString();
                oauth["expiresAt"] = now + (seconds * 1000);
                if (reply.TryGetProperty("refresh_token_expires_in", out JsonElement refreshLifetime) && refreshLifetime.ValueKind == JsonValueKind.Number && refreshLifetime.TryGetInt64(out long refreshSeconds))
                    oauth["refreshTokenExpiresAt"] = now + (refreshSeconds * 1000);
                if (reply.TryGetProperty("scope", out JsonElement granted) && granted.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(granted.GetString()))
                {
                    JsonArray list = new JsonArray();
                    foreach (string scope in granted.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries)) list.Add((JsonNode?)JsonValue.Create(scope));
                    oauth["scopes"] = list;
                }
                updated = root.ToJsonString(_Output);
            }
            catch (JsonException) { return ClaudeRefreshOutcome.Failed; }

            // The old refresh token is now invalid. Save the new login even if the caller has since canceled.
            PrivateFiles.Write(path, updated);
            if (keychain != null)
            {
                try { await keychain.WriteAsync(service, updated, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception error) when (error is InvalidOperationException or IOException) { }
            }
            return ClaudeRefreshOutcome.Refreshed;
        }

        private static string Scopes(JsonObject oauth)
        {
            if (oauth["scopes"] is not JsonArray scopes) return "";
            StringBuilder joined = new StringBuilder();
            foreach (JsonNode? scope in scopes)
            {
                if (scope is not JsonValue value || !value.TryGetValue(out string? text) || string.IsNullOrWhiteSpace(text)) continue;
                if (joined.Length > 0) joined.Append(' ');
                joined.Append(text);
            }
            return joined.ToString();
        }

        private static bool InvalidGrant(string response)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(response);
                return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("error", out JsonElement error)
                    && error.ValueKind == JsonValueKind.String && error.GetString() == "invalid_grant";
            }
            catch (JsonException) { return false; }
        }
    }
}
