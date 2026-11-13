namespace Kameo.Core
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    /// <summary>Imports only native authentication artifacts, never a harness's hooks, plugins or history.</summary>
    public sealed class CredentialFiles
    {
        private readonly ProfileStore _Store;

        /// <summary>Creates an importer for a profile store.</summary>
        public CredentialFiles(ProfileStore store) => _Store = store ?? throw new ArgumentNullException(nameof(store));

        /// <summary>Imports a vendor credential JSON file into an idle native profile.</summary>
        public void Import(string id, string source)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            ImportText(id, PrivateFiles.Read(Path.GetFullPath(source)));
        }

        /// <summary>Validates and atomically replaces a profile's native authentication document.</summary>
        public void ImportText(string id, string rawCredentialJson)
        {
            using FileStream lease = _Store.AcquireLease(id);
            Profile profile = _Store.Get(id);
            if (profile.Kind != CredentialKind.Native) throw new InvalidOperationException("Import is available for native login profiles only.");
            Validate(profile.Harness, rawCredentialJson);
            if (profile.Harness == HarnessKind.Mux) rawCredentialJson = MuxCredentialImport.Resolve(rawCredentialJson, Environment.GetEnvironmentVariable);
            PrivateFiles.Write(Path.Combine(_Store.ProfileDirectory(id), HarnessCatalog.CredentialFile(profile.Harness)), rawCredentialJson);
        }

        /// <summary>Checks the minimum native credential shape without exposing input in errors.</summary>
        /// <exception cref="InvalidDataException">The JSON does not describe credentials for this harness.</exception>
        public static void Validate(HarnessKind harness, string rawCredentialJson)
        {
            ArgumentNullException.ThrowIfNull(rawCredentialJson);
            if (rawCredentialJson.Length > 1024 * 1024) throw new InvalidDataException("Credential files must be smaller than 1 MiB.");
            try
            {
                using JsonDocument document = JsonDocument.Parse(rawCredentialJson);
                JsonElement root = document.RootElement;
                bool valid = root.ValueKind == JsonValueKind.Object && harness switch
                {
                    HarnessKind.Claude => root.TryGetProperty("claudeAiOauth", out JsonElement oauth) && HasString(oauth, "accessToken") && HasString(oauth, "refreshToken"),
                    HarnessKind.Codex => HasString(root, "OPENAI_API_KEY") || (root.TryGetProperty("tokens", out JsonElement tokens) && HasString(tokens, "access_token") && HasString(tokens, "refresh_token")),
                    HarnessKind.Gemini => HasString(root, "refresh_token"),
                    HarnessKind.Mux => root.TryGetProperty("endpoints", out JsonElement endpoints) && endpoints.ValueKind == JsonValueKind.Array && endpoints.GetArrayLength() > 0 && ValidEndpoints(endpoints),
                    _ => false
                };
                if (!valid) throw new InvalidDataException("This file does not contain valid " + HarnessCatalog.Name(harness) + " credentials. Import its native credential file or sign in again.");
            }
            catch (JsonException) { throw new InvalidDataException("The credential file is not valid JSON."); }
        }

        /// <summary>Creates a Mux endpoint document using a child-only environment reference for the API key.</summary>
        public void ConfigureMux(string id, string adapter, string baseUrl, string model)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(adapter);
            ArgumentException.ThrowIfNullOrWhiteSpace(model);
            if (adapter != "openai" && adapter != "openai-compatible" && adapter != "anthropic" && adapter != "gemini" && adapter != "xai")
                throw new ArgumentException("Choose openai, openai-compatible, anthropic, gemini, or xai.", nameof(adapter));
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri) || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
                throw new ArgumentException("Use an HTTPS endpoint, or HTTP for a local endpoint.", nameof(baseUrl));
            using FileStream lease = _Store.AcquireLease(id);
            Profile profile = _Store.Get(id);
            if (profile.Harness != HarnessKind.Mux || profile.Kind != CredentialKind.ApiKey) throw new InvalidOperationException("This operation requires a Mux API-key profile.");
            JsonObject endpoint = new JsonObject
            {
                ["name"] = "kameo", ["adapterType"] = adapter, ["baseUrl"] = baseUrl,
                ["model"] = model.Trim(), ["isDefault"] = true, ["apiKey"] = "${KAMEO_MUX_API_KEY}"
            };
            JsonObject root = new JsonObject { ["endpoints"] = new JsonArray(endpoint) };
            PrivateFiles.Write(Path.Combine(_Store.ProfileDirectory(id), "endpoints.json"), root.ToJsonString());
        }

        private static bool HasString(JsonElement element, string property) => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString());

        private static bool ValidEndpoints(JsonElement endpoints)
        {
            foreach (JsonElement endpoint in endpoints.EnumerateArray())
                if (!HasString(endpoint, "name") || !HasString(endpoint, "adapterType") || !HasString(endpoint, "model")) return false;
            return true;
        }
    }
}
