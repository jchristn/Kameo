namespace Kameo.Publisher
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// Maps publisher.json onto <see cref="PublisherManifest"/> by hand, so manifest loading works under trimming and
    /// native AOT without reflection or generated code. Property names are camelCase and matched exactly. Comments and
    /// trailing commas are allowed. Unknown properties, nulls, and values of the wrong JSON type are rejected.
    /// </summary>
    /// <remarks>Stateless and thread safe.</remarks>
    public static class ManifestReader
    {
        private static readonly JsonDocumentOptions _Options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        /// <summary>Parses manifest JSON without validating its values.</summary>
        /// <param name="json">The manifest text. Must not be null.</param>
        /// <returns>The populated manifest. Omitted properties keep their defaults.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
        /// <exception cref="PublisherException">The text is not JSON, or a property is unknown or has the wrong type.</exception>
        public static PublisherManifest Read(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            try
            {
                using JsonDocument document = JsonDocument.Parse(json, _Options);
                PublisherManifest manifest = new PublisherManifest();
                foreach (JsonProperty property in Object(document.RootElement, "$"))
                {
                    string path = "$." + property.Name;
                    switch (property.Name)
                    {
                        case "project": manifest.Project = Project(property.Value, path); break;
                        case "framework": manifest.Framework = Text(property.Value, path); break;
                        case "runtimes": manifest.Runtimes = TextList(property.Value, path); break;
                        case "artifacts": manifest.Artifacts = Artifacts(property.Value, path); break;
                        case "channels": manifest.Channels = TextList(property.Value, path); break;
                        case "prereleaseBeforeOne": manifest.PrereleaseBeforeOne = Flag(property.Value, path); break;
                        case "agent": manifest.Agent = Agent(property.Value, path); break;
                        case "linux": manifest.Linux = Linux(property.Value, path); break;
                        case "macos": manifest.Macos = Mac(property.Value, path); break;
                        case "signing": manifest.Signing = Signing(property.Value, path); break;
                        default: throw Unknown(path);
                    }
                }
                return manifest;
            }
            catch (JsonException error)
            {
                throw new PublisherException("publisher.json is not valid: " + error.Message, error);
            }
        }

        private static ProjectInfo Project(JsonElement element, string path)
        {
            ProjectInfo project = new ProjectInfo();
            foreach (JsonProperty property in Object(element, path))
            {
                string child = path + "." + property.Name;
                switch (property.Name)
                {
                    case "name": project.Name = Text(property.Value, child); break;
                    case "command": project.Command = Text(property.Value, child); break;
                    case "identifier": project.Identifier = Text(property.Value, child); break;
                    case "tagline": project.Tagline = Text(property.Value, child); break;
                    case "description": project.Description = Text(property.Value, child); break;
                    case "license": project.License = Text(property.Value, child); break;
                    case "licenseFile": project.LicenseFile = Text(property.Value, child); break;
                    case "logo": project.Logo = Text(property.Value, child); break;
                    case "logoDark": project.LogoDark = Text(property.Value, child); break;
                    case "publisher": project.Publisher = Text(property.Value, child); break;
                    case "publisherUrl": project.PublisherUrl = Text(property.Value, child); break;
                    case "repositoryUrl": project.RepositoryUrl = Text(property.Value, child); break;
                    case "copyright": project.Copyright = Text(property.Value, child); break;
                    case "versionFile": project.VersionFile = Text(property.Value, child); break;
                    case "changelog": project.Changelog = Text(property.Value, child); break;
                    case "installNotes": project.InstallNotes = Text(property.Value, child); break;
                    default: throw Unknown(child);
                }
            }
            return project;
        }

        private static List<ArtifactInfo> Artifacts(JsonElement element, string path)
        {
            List<ArtifactInfo> artifacts = new List<ArtifactInfo>();
            int index = 0;
            foreach (JsonElement item in Array(element, path))
            {
                string itemPath = path + "[" + index++ + "]";
                ArtifactInfo artifact = new ArtifactInfo();
                foreach (JsonProperty property in Object(item, itemPath))
                {
                    string child = itemPath + "." + property.Name;
                    switch (property.Name)
                    {
                        case "name": artifact.Name = Text(property.Value, child); break;
                        case "project": artifact.Project = Text(property.Value, child); break;
                        case "executable": artifact.Executable = Text(property.Value, child); break;
                        case "kind": artifact.Kind = Text(property.Value, child); break;
                        default: throw Unknown(child);
                    }
                }
                artifacts.Add(artifact);
            }
            return artifacts;
        }

        private static AgentSettings Agent(JsonElement element, string path)
        {
            AgentSettings agent = new AgentSettings();
            foreach (JsonProperty property in Object(element, path))
            {
                string child = path + "." + property.Name;
                switch (property.Name)
                {
                    case "label": agent.Label = Text(property.Value, child); break;
                    case "windowsName": agent.WindowsName = Text(property.Value, child); break;
                    case "arguments": agent.Arguments = TextList(property.Value, child); break;
                    default: throw Unknown(child);
                }
            }
            return agent;
        }

        private static LinuxSettings Linux(JsonElement element, string path)
        {
            LinuxSettings linux = new LinuxSettings();
            foreach (JsonProperty property in Object(element, path))
            {
                string child = path + "." + property.Name;
                switch (property.Name)
                {
                    case "installDirectory": linux.InstallDirectory = Text(property.Value, child); break;
                    case "category": linux.Category = Text(property.Value, child); break;
                    case "desktopCategories": linux.DesktopCategories = Text(property.Value, child); break;
                    case "debDependencies": linux.DebDependencies = TextList(property.Value, child); break;
                    case "rpmDependencies": linux.RpmDependencies = TextList(property.Value, child); break;
                    default: throw Unknown(child);
                }
            }
            return linux;
        }

        private static MacSettings Mac(JsonElement element, string path)
        {
            MacSettings mac = new MacSettings();
            foreach (JsonProperty property in Object(element, path))
            {
                string child = path + "." + property.Name;
                switch (property.Name)
                {
                    case "minimumSystemVersion": mac.MinimumSystemVersion = Text(property.Value, child); break;
                    case "commandDirectory": mac.CommandDirectory = Text(property.Value, child); break;
                    default: throw Unknown(child);
                }
            }
            return mac;
        }

        private static SigningSettings Signing(JsonElement element, string path)
        {
            SigningSettings signing = new SigningSettings();
            foreach (JsonProperty property in Object(element, path))
            {
                string child = path + "." + property.Name;
                switch (property.Name)
                {
                    case "windows": signing.Windows = SigningTarget(property.Value, child); break;
                    case "macos": signing.Macos = SigningTarget(property.Value, child); break;
                    default: throw Unknown(child);
                }
            }
            return signing;
        }

        private static SigningTarget SigningTarget(JsonElement element, string path)
        {
            SigningTarget target = new SigningTarget();
            foreach (JsonProperty property in Object(element, path))
            {
                string child = path + "." + property.Name;
                switch (property.Name)
                {
                    case "isConfigured": target.IsConfigured = Flag(property.Value, child); break;
                    case "identity": target.Identity = Text(property.Value, child); break;
                    case "installerIdentity": target.InstallerIdentity = Text(property.Value, child); break;
                    case "notaryProfile": target.NotaryProfile = Text(property.Value, child); break;
                    case "certificateEnvironment": target.CertificateEnvironment = Text(property.Value, child); break;
                    case "passwordEnvironment": target.PasswordEnvironment = Text(property.Value, child); break;
                    case "timestampUrl": target.TimestampUrl = Text(property.Value, child); break;
                    default: throw Unknown(child);
                }
            }
            return target;
        }

        private static JsonElement.ObjectEnumerator Object(JsonElement element, string path)
        {
            if (element.ValueKind != JsonValueKind.Object) throw WrongType(path, "an object", element);
            return element.EnumerateObject();
        }

        private static JsonElement.ArrayEnumerator Array(JsonElement element, string path)
        {
            if (element.ValueKind != JsonValueKind.Array) throw WrongType(path, "an array", element);
            return element.EnumerateArray();
        }

        private static string Text(JsonElement element, string path)
        {
            if (element.ValueKind != JsonValueKind.String) throw WrongType(path, "a string", element);
            return element.GetString() ?? "";
        }

        private static bool Flag(JsonElement element, string path)
        {
            if (element.ValueKind == JsonValueKind.True) return true;
            if (element.ValueKind == JsonValueKind.False) return false;
            throw WrongType(path, "true or false", element);
        }

        private static List<string> TextList(JsonElement element, string path)
        {
            List<string> values = new List<string>();
            int index = 0;
            foreach (JsonElement item in Array(element, path)) values.Add(Text(item, path + "[" + index++ + "]"));
            return values;
        }

        private static PublisherException Unknown(string path) => new PublisherException("publisher.json is not valid: unknown property " + path + ".");

        private static PublisherException WrongType(string path, string expected, JsonElement element) =>
            new PublisherException("publisher.json is not valid: " + path + " must be " + expected + ", not " + element.ValueKind.ToString().ToLowerInvariant() + ".");
    }
}
