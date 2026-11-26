namespace Kameo.Core
{
    using System;

    /// <summary>The parts of a saved Claude Code login that a usage check needs. Holds a secret; never log it.</summary>
    internal sealed class ClaudeSavedLogin
    {
        /// <summary>The OAuth access token.</summary>
        internal string AccessToken { get; init; } = "";
        /// <summary>The plan label, such as "Max 20x", or empty.</summary>
        internal string Plan { get; init; } = "";
        /// <summary>When the access token expires, when recorded.</summary>
        internal DateTimeOffset? Expires { get; init; }
        /// <summary>A report explaining why the login cannot be used, or null when it can.</summary>
        internal UsageReport? Problem { get; init; }
    }
}
