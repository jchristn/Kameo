namespace Kameo.Core
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>The Keychain operations Claude login synchronization needs. Tests substitute an in-memory store.</summary>
    internal interface IClaudeKeychain
    {
        /// <summary>Returns a service's password, or null when the item does not exist.</summary>
        /// <exception cref="System.IO.IOException">The Keychain could not be read, so the item's state is unknown.</exception>
        Task<string?> ReadAsync(string service, CancellationToken cancellationToken);

        /// <summary>Creates or replaces a service's password.</summary>
        /// <exception cref="System.InvalidOperationException">The Keychain rejected or did not retain the value.</exception>
        Task WriteAsync(string service, string credential, CancellationToken cancellationToken);
    }
}
