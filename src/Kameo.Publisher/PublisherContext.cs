namespace Kameo.Publisher
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    /// <summary>Everything a channel needs: the manifest, the version, and the repository, staging, and output paths.</summary>
    public sealed class PublisherContext
    {
        /// <summary>The validated manifest.</summary>
        public PublisherManifest Manifest { get; }

        /// <summary>Repository root (the directory holding publisher.json).</summary>
        public string Root { get; }

        /// <summary>The version being packaged.</summary>
        public string Version { get; }

        /// <summary>Final release files: <c>installers/&lt;version&gt;</c> under the root unless overridden.</summary>
        public string OutputDirectory { get; }

        /// <summary>Intermediate files: <c>artifacts/publisher/&lt;version&gt;</c> under the root.</summary>
        public string WorkDirectory { get; }

        /// <summary>Runtime identifiers selected for this run.</summary>
        public IReadOnlyList<RuntimeTarget> Targets { get; }

        /// <summary>The executable placed on PATH.</summary>
        public ArtifactInfo Cli => Manifest.Artifacts.First(artifact => artifact.Kind == "cli");

        /// <summary>The tray executable, or null when the product has none.</summary>
        public ArtifactInfo? Tray => Manifest.Artifacts.FirstOrDefault(artifact => artifact.Kind == "tray");

        /// <summary>Generated icons and installer images.</summary>
        public string ArtworkDirectory => Path.Combine(WorkDirectory, "artwork");

        /// <summary>Creates a context.</summary>
        /// <param name="manifest">The validated manifest.</param>
        /// <param name="root">Repository root.</param>
        /// <param name="version">The version to package.</param>
        /// <param name="outputDirectory">Release output directory, or null for <c>installers/&lt;version&gt;</c>.</param>
        /// <param name="rids">Runtime identifiers to package, or null for every manifest runtime.</param>
        /// <exception cref="PublisherException">A requested runtime identifier is not in the manifest.</exception>
        public PublisherContext(PublisherManifest manifest, string root, string version, string? outputDirectory, IReadOnlyList<string>? rids)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            ArgumentException.ThrowIfNullOrWhiteSpace(root);
            Manifest = manifest;
            Root = Path.GetFullPath(root);
            Version = VersionReader.Validate(version);
            OutputDirectory = Path.GetFullPath(outputDirectory ?? Path.Combine(Root, "installers", Version));
            WorkDirectory = Path.Combine(Root, "artifacts", "publisher", Version);
            List<RuntimeTarget> targets = new List<RuntimeTarget>();
            foreach (string rid in rids ?? manifest.Runtimes)
            {
                if (!manifest.Runtimes.Contains(rid, StringComparer.Ordinal)) throw new PublisherException("Runtime '" + rid + "' is not listed in publisher.json.");
                targets.Add(RuntimeTarget.Parse(rid));
            }
            Targets = targets;
        }

        /// <summary>Returns the selected targets for one operating system.</summary>
        /// <param name="operatingSystem"><c>windows</c>, <c>macos</c>, or <c>linux</c>.</param>
        /// <returns>The matching targets, possibly empty.</returns>
        public IReadOnlyList<RuntimeTarget> TargetsFor(string operatingSystem) => Targets.Where(target => target.OperatingSystem == operatingSystem).ToList();

        /// <summary>Resolves a repository-relative path.</summary>
        /// <param name="relative">Path relative to the root, using forward slashes.</param>
        /// <returns>The absolute path.</returns>
        public string Resolve(string relative)
        {
            ArgumentNullException.ThrowIfNull(relative);
            return Path.GetFullPath(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));
        }

        /// <summary>Directory holding every self-contained executable for one runtime identifier.</summary>
        /// <param name="target">The runtime identifier.</param>
        /// <returns>The payload directory.</returns>
        public string PayloadDirectory(RuntimeTarget target)
        {
            ArgumentNullException.ThrowIfNull(target);
            return Path.Combine(WorkDirectory, "payload", target.Rid);
        }

        /// <summary>Path of a release file in the output directory.</summary>
        /// <param name="channel">The channel producing it.</param>
        /// <param name="target">The runtime identifier.</param>
        /// <returns>The absolute output path.</returns>
        public string OutputFile(string channel, RuntimeTarget target) => Path.Combine(OutputDirectory, AssetNames.For(Manifest.Project.Command, Version, channel, target));

        /// <summary>The license text with line endings normalized to LF.</summary>
        /// <returns>The license text.</returns>
        /// <exception cref="PublisherException">The license file is missing.</exception>
        public string LicenseText()
        {
            string path = Resolve(Manifest.Project.LicenseFile);
            if (!File.Exists(path)) throw new PublisherException("License file not found: " + path + ".");
            return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        }
    }
}
