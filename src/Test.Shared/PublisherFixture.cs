namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Kameo.Publisher;

    /// <summary>A disposable repository root holding a synthetic manifest, license, logo, changelog, and notes template.</summary>
    internal sealed class PublisherFixture : IDisposable
    {
        internal const string ManifestJson = """
            {
              "project": {
                "name": "Sample App", "command": "sample", "identifier": "ai.latticetech.sample",
                "tagline": "A synthetic tagline.", "description": "A synthetic description.",
                "license": "MIT", "licenseFile": "LICENSE.md", "logo": "logo.png", "logoDark": "logo.png",
                "publisher": "Lattice AI, LLC", "publisherUrl": "https://latticetech.ai",
                "repositoryUrl": "https://github.com/example/sample", "copyright": "Copyright (c) 2026 Example"
              },
              "framework": "net10.0",
              "runtimes": [ "win-x64", "win-arm64", "osx-x64", "osx-arm64", "linux-x64", "linux-arm64" ],
              "artifacts": [
                { "name": "cli", "project": "src/Cli/Cli.csproj", "executable": "sample", "kind": "cli" },
                { "name": "desktop", "project": "src/Desktop/Desktop.csproj", "executable": "Sample.Desktop", "kind": "tray" }
              ],
              "channels": [ "zip", "inno", "app", "tarball", "pkg", "dmg", "deb", "rpm", "appimage" ],
              "agent": { "label": "ai.latticetech.sample.agent", "windowsName": "Sample App Agent", "arguments": [ "--tray" ] },
              "linux": { "installDirectory": "/opt/sample", "debDependencies": [ "libicu74 | libicu72", "libx11-6" ], "rpmDependencies": [ "libicu", "libX11" ] }
            }
            """;

        internal string Root { get; } = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "kameo-publisher-" + Guid.NewGuid().ToString("N"));

        internal PublisherManifest Manifest { get; }

        internal PublisherFixture(string version = "1.2.3")
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "publisher.json"), ManifestJson);
            File.WriteAllText(Path.Combine(Root, "LICENSE.md"), "MIT License\r\n\r\nSynthetic license text.\r\n");
            File.WriteAllText(Path.Combine(Root, "Directory.Build.props"), "<Project><PropertyGroup><Version>" + version + "</Version></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(Root, "CHANGELOG.md"), "# Changelog\n\n## [Unreleased]\n\n## [" + version + "] - 2026-11-20\n\n- Synthetic change.\n\n## [0.0.1] - 2026-01-01\n\n- Older change.\n");
            Directory.CreateDirectory(Path.Combine(Root, ".github"));
            File.WriteAllText(Path.Combine(Root, ".github", "release-install-notes.md"), "## Installing\n\nDownload `sample-{version}-win-x64-setup.exe`.\n");
            RgbaImage logo = RgbaImage.Filled(64, 64, 0, 0, 0, 0);
            logo.Draw(RgbaImage.Filled(32, 32, 40, 40, 40, 255), 16, 16);
            File.WriteAllBytes(Path.Combine(Root, "logo.png"), PngCodec.Encode(logo));
            Manifest = ManifestLoader.Load(Path.Combine(Root, "publisher.json"));
        }

        internal PublisherContext Context(IReadOnlyList<string>? rids = null, string version = "1.2.3") => new PublisherContext(Manifest, Root, version, null, rids);

        internal void FakePayload(PublisherContext context, RuntimeTarget target)
        {
            string payload = context.PayloadDirectory(target);
            Directory.CreateDirectory(payload);
            foreach (ArtifactInfo artifact in context.Manifest.Artifacts)
                File.WriteAllText(Path.Combine(payload, artifact.Executable + target.ExecutableSuffix), "synthetic " + artifact.Name + " for " + target.Rid);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
