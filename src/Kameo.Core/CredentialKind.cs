namespace Kameo.Core
{
    /// <summary>How a profile authenticates.</summary>
    public enum CredentialKind
    {
        /// <summary>An API key supplied to the child process.</summary>
        ApiKey,
        /// <summary>A native harness login, including renewable OAuth credentials.</summary>
        Native
    }
}
