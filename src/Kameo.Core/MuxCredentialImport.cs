namespace Kameo.Core
{
    using System;
    using System.Text.Json.Nodes;
    using System.Text.RegularExpressions;

    /// <summary>Captures explicit Mux credential environment references into an imported profile.</summary>
    public static class MuxCredentialImport
    {

        private static readonly Regex _References = new Regex(@"\$\{(?<braced>[A-Za-z_][A-Za-z0-9_]*)\}|%(?<percent>[A-Za-z_][A-Za-z0-9_]*)%|\$(?:env:)?(?<bare>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant);

        /// <summary>Resolves endpoint API keys and headers so future launches do not depend on the importing shell's account.</summary>
        /// <exception cref="InvalidOperationException">A referenced credential variable is unavailable.</exception>
        public static string Resolve(string rawCredentialJson, Func<string, string?> readEnvironment)
        {
            ArgumentNullException.ThrowIfNull(readEnvironment);
            CredentialFiles.Validate(HarnessKind.Mux, rawCredentialJson);
            JsonObject root = JsonNode.Parse(rawCredentialJson)!.AsObject();
            bool changed = false;
            foreach (JsonNode? node in root["endpoints"]!.AsArray())
            {
                JsonObject endpoint = node!.AsObject();
                if (endpoint["apiKey"] is JsonValue value && value.TryGetValue(out string? apiKey))
                {
                    string resolved = ResolveValue(apiKey, readEnvironment);
                    changed |= resolved != apiKey;
                    endpoint["apiKey"] = resolved;
                }
                if (endpoint["headers"] is JsonObject headers)
                {
                    foreach (string key in System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(headers, item => item.Key)))
                    {
                        if (headers[key] is not JsonValue header || !header.TryGetValue(out string? text)) continue;
                        string resolved = ResolveValue(text, readEnvironment);
                        changed |= resolved != text;
                        headers[key] = resolved;
                    }
                }
            }
            return changed ? root.ToJsonString() : rawCredentialJson;
        }

        private static string ResolveValue(string value, Func<string, string?> readEnvironment) => _References.Replace(value, match =>
        {
            string variable = match.Groups["braced"].Success ? match.Groups["braced"].Value : match.Groups["percent"].Success ? match.Groups["percent"].Value : match.Groups["bare"].Value;
            return readEnvironment(variable) ?? throw new InvalidOperationException("The imported Mux credential references an unset environment variable: " + variable + ". Set it before importing, or use an API-key profile.");
        });    }
}
