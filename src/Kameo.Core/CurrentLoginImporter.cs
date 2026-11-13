namespace Kameo.Core
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Explicitly imports the user's existing harness authentication into a new Kameo profile.</summary>
    public sealed class CurrentLoginImporter
    {
        private readonly ProfileStore _Store;
        /// <summary>Creates an importer.</summary>
        public CurrentLoginImporter(ProfileStore store) => _Store = store ?? throw new ArgumentNullException(nameof(store));

        /// <summary>Imports the standard native file, or Claude's default macOS Keychain login if necessary.</summary>
        public async Task ImportAsync(string id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Profile profile = _Store.Get(id);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string sourceDirectory = profile.Harness switch
            {
                HarnessKind.Claude => Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(home, ".claude"),
                HarnessKind.Codex => Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(home, ".codex"),
                HarnessKind.Antigravity => Path.Combine(home, ".gemini"),
                HarnessKind.Mux => Environment.GetEnvironmentVariable("MUX_CONFIG_DIR") ?? Path.Combine(home, ".mux"),
                _ => throw new ArgumentOutOfRangeException(nameof(id))
            };
            CredentialFiles importer = new CredentialFiles(_Store);
            if (profile.Harness == HarnessKind.Claude && OperatingSystem.IsMacOS())
            {
                string? credential = await MacCredentialBridge.ReadAsync(MacCredentialBridge.ServiceName(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")), cancellationToken).ConfigureAwait(false);
                if (credential != null) { importer.ImportText(id, credential); return; }
            }
            string source = Path.Combine(sourceDirectory, HarnessCatalog.CredentialFile(profile.Harness));
            if (!File.Exists(source)) throw new FileNotFoundException("No importable credential file was found. Use Sign in to create an isolated login, or import an exported native credential JSON file. OS-only credential stores are not automatically exported for this harness.");
            importer.Import(id, source);
        }
    }
}
