namespace Kameo.Publisher
{
    using System.Collections.Generic;

    /// <summary>The deserialized publisher.json manifest: the single source of truth for packaging.</summary>
    public sealed class PublisherManifest
    {
        /// <summary>Product presentation and repository paths.</summary>
        public ProjectInfo Project { get; set; } = new ProjectInfo();

        /// <summary>Target framework passed to <c>dotnet publish</c>.</summary>
        public string Framework { get; set; } = "";

        /// <summary>Runtime identifiers to build, for example <c>win-x64</c> or <c>osx-arm64</c>.</summary>
        public List<string> Runtimes { get; set; } = new List<string>();

        /// <summary>Executables shipped in every installer.</summary>
        public List<ArtifactInfo> Artifacts { get; set; } = new List<ArtifactInfo>();

        /// <summary>Enabled channels. See <see cref="ChannelCatalog"/> for the valid names.</summary>
        public List<string> Channels { get; set; } = new List<string>();

        /// <summary>True marks every 0.x.y release as a GitHub pre-release, as alpha releases. Default true.</summary>
        public bool PrereleaseBeforeOne { get; set; } = true;

        /// <summary>Login startup registration for the tray artifact.</summary>
        public AgentSettings Agent { get; set; } = new AgentSettings();

        /// <summary>Linux package metadata.</summary>
        public LinuxSettings Linux { get; set; } = new LinuxSettings();

        /// <summary>macOS bundle metadata.</summary>
        public MacSettings Macos { get; set; } = new MacSettings();

        /// <summary>Code-signing switches.</summary>
        public SigningSettings Signing { get; set; } = new SigningSettings();
    }
}
