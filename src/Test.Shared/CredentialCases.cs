namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json.Nodes;
    using Kameo.Core;
    using Touchstone.Core;

    internal static class CredentialCases
    {
        internal static string Document(HarnessKind harness) => harness switch
        {
            HarnessKind.Claude => "{\"claudeAiOauth\":{\"accessToken\":\"synthetic-access\",\"refreshToken\":\"synthetic-refresh\",\"expiresAt\":1900000000000}}",
            HarnessKind.Codex => "{\"tokens\":{\"access_token\":\"synthetic-access\",\"refresh_token\":\"synthetic-refresh\",\"id_token\":\"synthetic-id\"}}",
            HarnessKind.Gemini => "{\"refresh_token\":\"synthetic-refresh\",\"access_token\":\"synthetic-access\"}",
            HarnessKind.Mux => "{\"endpoints\":[{\"name\":\"test\",\"adapterType\":\"openai\",\"model\":\"test-model\",\"apiKey\":\"synthetic-key\"}]}",
            _ => throw new ArgumentOutOfRangeException(nameof(harness))
        };

        internal static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string name, Action<Fixture> execute) => cases.Add(KameoSuites.Case("Credentials", name, execute));
            foreach (HarnessKind harness in HarnessCatalog.All)
            {
                Add("Import" + harness, fixture =>
                {
                    Profile profile = fixture.Add(harness, kind: CredentialKind.Native);
                    string json = Document(harness);
                    string source = Path.Combine(fixture.Root, "source.json");
                    File.WriteAllText(source, json);
                    new CredentialFiles(fixture.Store).Import(profile.Id, source);
                    Fixture.Check(File.ReadAllText(source) == json, "Import must not modify the source.");
                    Fixture.Check(PrivateFiles.Read(Path.Combine(fixture.Store.ProfileDirectory(profile.Id), HarnessCatalog.CredentialFile(harness))) == json, "Native fields must survive import unchanged.");
                    Fixture.Check(fixture.Store.Get(profile.Id).HasNativeCredential, "Imported login should be visible.");
                });
                Add("InvalidImportPreserves" + harness, fixture =>
                {
                    Profile profile = fixture.Add(harness, kind: CredentialKind.Native);
                    CredentialFiles files = new CredentialFiles(fixture.Store);
                    files.ImportText(profile.Id, Document(harness));
                    Fixture.Throws<InvalidDataException>(() => files.ImportText(profile.Id, "{}"));
                    Fixture.Check(PrivateFiles.Read(Path.Combine(fixture.Store.ProfileDirectory(profile.Id), HarnessCatalog.CredentialFile(harness))) == Document(harness), "Failed import replaced credentials.");
                });
            }
            Add("MalformedJsonDoesNotEchoSecret", fixture =>
            {
                try { CredentialFiles.Validate(HarnessKind.Codex, "{super-secret-not-json"); }
                catch (InvalidDataException error) { Fixture.Check(!error.ToString().Contains("super-secret", StringComparison.Ordinal), "Error leaked input."); return; }
                throw new InvalidOperationException("Malformed JSON accepted.");
            });
            Add("OversizeJsonRejected", fixture => Fixture.Throws<InvalidDataException>(() => CredentialFiles.Validate(HarnessKind.Codex, new string('x', 1024 * 1024 + 1))));
            Add("OversizeFileRejected", fixture => { string path = Path.Combine(fixture.Root, "large"); File.WriteAllBytes(path, new byte[1024 * 1024 + 1]); Fixture.Throws<InvalidDataException>(() => PrivateFiles.Read(path)); });
            Add("ApiProfileRejectsImport", fixture => { Profile profile = fixture.Add(); Fixture.Throws<InvalidOperationException>(() => new CredentialFiles(fixture.Store).ImportText(profile.Id, Document(HarnessKind.Codex))); });
            Add("BusyProfileRejectsImport", fixture => { Profile profile = fixture.Add(kind: CredentialKind.Native); using FileStream lease = fixture.Store.AcquireLease(profile.Id); Fixture.Throws<InvalidOperationException>(() => new CredentialFiles(fixture.Store).ImportText(profile.Id, Document(HarnessKind.Codex))); });
            Add("DifferentProfileIsUnaffected", fixture => { Profile first = fixture.Add(kind: CredentialKind.Native); Profile second = fixture.Add(name: "Other", kind: CredentialKind.Native); new CredentialFiles(fixture.Store).ImportText(first.Id, Document(HarnessKind.Codex)); Fixture.Check(!fixture.Store.Get(second.Id).HasNativeCredential, "Import crossed profile boundaries."); });
            Add("CodexApiFileAccepted", fixture => CredentialFiles.Validate(HarnessKind.Codex, "{\"OPENAI_API_KEY\":\"synthetic\"}"));
            Add("MissingRefreshRejected", fixture => Fixture.Throws<InvalidDataException>(() => CredentialFiles.Validate(HarnessKind.Claude, "{\"claudeAiOauth\":{\"accessToken\":\"test\"}}")));
            Add("WrongTokenTypeRejected", fixture => Fixture.Throws<InvalidDataException>(() => CredentialFiles.Validate(HarnessKind.Gemini, "{\"refresh_token\":123}")));
            Add("MuxEmptyEndpointsRejected", fixture => Fixture.Throws<InvalidDataException>(() => CredentialFiles.Validate(HarnessKind.Mux, "{\"endpoints\":[]}")));
            Add("MuxEnvironmentCredentialCapture", fixture =>
            {
                string json = "{\"endpoints\":[{\"name\":\"test\",\"adapterType\":\"openai\",\"model\":\"model\",\"apiKey\":\"${OPENAI_API_KEY}\",\"headers\":{\"X-Key\":\"%HEADER_KEY%\",\"Authorization\":\"Bearer $env:TOKEN\"}}]}";
                JsonNode resolved = JsonNode.Parse(MuxCredentialImport.Resolve(json, name => "synthetic-" + name))!;
                Fixture.Check(resolved["endpoints"]?[0]?["apiKey"]?.GetValue<string>() == "synthetic-OPENAI_API_KEY", "API key reference was not captured.");
                Fixture.Check(resolved["endpoints"]?[0]?["headers"]?["Authorization"]?.GetValue<string>() == "Bearer synthetic-TOKEN", "Header credential reference was not captured.");
                Fixture.Throws<InvalidOperationException>(() => MuxCredentialImport.Resolve(json, name => null));
            });
            Add("MuxConfigUsesEnvironmentReference", fixture =>
            {
                Profile profile = fixture.Add(HarnessKind.Mux);
                new CredentialFiles(fixture.Store).ConfigureMux(profile.Id, "openai-compatible", "https://example.test/v1", "model-test");
                string json = PrivateFiles.Read(Path.Combine(fixture.Store.ProfileDirectory(profile.Id), "endpoints.json"));
                Fixture.Check(!json.Contains(profile.ApiKey, StringComparison.Ordinal) && json.Contains("${KAMEO_MUX_API_KEY}", StringComparison.Ordinal), "Mux must not duplicate API key in endpoint config.");
                CredentialFiles.Validate(HarnessKind.Mux, json);
            });
            Add("MuxUnsafeEndpointRejected", fixture => { Profile profile = fixture.Add(HarnessKind.Mux); Fixture.Throws<ArgumentException>(() => new CredentialFiles(fixture.Store).ConfigureMux(profile.Id, "openai", "http://example.test", "model")); });
            Add("MuxLocalEndpointAllowed", fixture => { Profile profile = fixture.Add(HarnessKind.Mux); new CredentialFiles(fixture.Store).ConfigureMux(profile.Id, "openai-compatible", "http://127.0.0.1:8000", "model"); });
            Add("AtomicReplacementAndPermissions", fixture =>
            {
                string path = Path.Combine(fixture.Root, "private.json");
                PrivateFiles.Write(path, "one"); PrivateFiles.Write(path, "two");
                Fixture.Check(PrivateFiles.Read(path) == "two" && Directory.GetFiles(fixture.Root, "*.tmp").Length == 0, "Atomic replacement left stale content or temporary files.");
                if (!OperatingSystem.IsWindows()) Fixture.Check(File.GetUnixFileMode(path) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "Credential permissions are not private.");
            });
            Add("SymlinkReadAndWriteRejected", fixture =>
            {
                string source = Path.Combine(fixture.Root, "source"); File.WriteAllText(source, "untouched");
                string link = Path.Combine(fixture.Root, "link"); File.CreateSymbolicLink(link, source);
                Fixture.Throws<IOException>(() => PrivateFiles.Read(link)); Fixture.Throws<IOException>(() => PrivateFiles.Write(link, "changed"));
                Fixture.Check(File.ReadAllText(source) == "untouched", "Link target changed.");
            });
            Add("BrokenSymlinkRejected", fixture => { string path = Path.Combine(fixture.Root, "broken"); File.CreateSymbolicLink(path, Path.Combine(fixture.Root, "missing")); Fixture.Throws<IOException>(() => PrivateFiles.Write(path, "secret")); });
            Add("ClaudeKeychainNamesAreScoped", fixture => { Fixture.Check(MacCredentialBridge.ServiceName(null) == "Claude Code-credentials", "Default service incorrect."); Fixture.Check(MacCredentialBridge.ServiceName("/a") != MacCredentialBridge.ServiceName("/b"), "Profiles share a keychain service."); });
            return new TestSuiteDescriptor("Credentials", "Credential import and filesystem safety", cases);
        }
    }
}
