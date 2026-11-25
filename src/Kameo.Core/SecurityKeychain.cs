namespace Kameo.Core
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>The user's login Keychain, reached through <c>/usr/bin/security</c> as Claude Code does.</summary>
    internal sealed class SecurityKeychain : IClaudeKeychain
    {
        /// <inheritdoc />
        public Task<string?> ReadAsync(string service, CancellationToken cancellationToken) => MacCredentialBridge.ReadStrictAsync(service, cancellationToken);

        /// <inheritdoc />
        public Task WriteAsync(string service, string credential, CancellationToken cancellationToken) => MacCredentialBridge.WriteAsync(service, credential, cancellationToken);
    }
}
