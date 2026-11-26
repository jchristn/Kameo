namespace Kameo.Desktop
{
    using System;
    using System.Collections.Generic;

    /// <summary>A terminal session to open for a profile: a launch or a sign-in, its folder, and extra harness arguments.</summary>
    internal sealed class LaunchRequest
    {
        /// <summary>The profile to run.</summary>
        internal string ProfileId { get; }
        /// <summary>True for the harness's sign-in or setup flow instead of a session.</summary>
        internal bool Login { get; }
        /// <summary>The folder the session starts in.</summary>
        internal string WorkingDirectory { get; }
        /// <summary>Arguments passed to the harness after Kameo's own, such as a permission flag.</summary>
        internal IReadOnlyList<string> Arguments { get; }

        /// <summary>Creates a request.</summary>
        internal LaunchRequest(string profileId, bool login, string workingDirectory, IReadOnlyList<string>? arguments = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
            ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
            ProfileId = profileId;
            Login = login;
            WorkingDirectory = workingDirectory;
            Arguments = arguments ?? Array.Empty<string>();
        }
    }
}
