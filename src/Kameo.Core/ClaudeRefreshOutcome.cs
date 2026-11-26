namespace Kameo.Core
{
    /// <summary>What happened when Kameo tried to renew an idle Claude Code sign-in.</summary>
    internal enum ClaudeRefreshOutcome
    {
        /// <summary>The saved sign-in is still fresh, possibly because another process renewed it first.</summary>
        NotNeeded,
        /// <summary>A new access token and refresh token were saved.</summary>
        Refreshed,
        /// <summary>A session or exclusive operation is using the profile, so the harness owns its tokens.</summary>
        InUse,
        /// <summary>Anthropic rejected the refresh token, or the saved login has none. The user must sign in again.</summary>
        Rejected,
        /// <summary>The refresh could not complete, such as a network failure or an unreadable Keychain. Nothing was changed.</summary>
        Failed
    }
}
