namespace Test.Shared
{
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Kameo.Core;

    /// <summary>An in-memory stand-in for the macOS login Keychain. Never touches the real Keychain.</summary>
    internal sealed class FakeKeychain : IClaudeKeychain
    {
        internal Dictionary<string, string> Items { get; } = new Dictionary<string, string>();
        internal int Writes { get; private set; }
        internal bool Locked { get; set; }

        /// <inheritdoc />
        public Task<string?> ReadAsync(string service, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Locked) throw new IOException("Synthetic locked keychain.");
            return Task.FromResult(Items.TryGetValue(service, out string? value) ? value : null);
        }

        /// <inheritdoc />
        public Task WriteAsync(string service, string credential, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Items[service] = credential;
            Writes++;
            return Task.CompletedTask;
        }
    }
}
