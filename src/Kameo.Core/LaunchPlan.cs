namespace Kameo.Core
{
    using System;
    using System.Diagnostics;

    /// <summary>A child process launch plan. Contains secrets in the environment; never log it.</summary>
    public sealed class LaunchPlan
    {
        /// <summary>The complete child process configuration.</summary>
        public ProcessStartInfo StartInfo { get; }
        /// <summary>The immutable profile identifier used for this invocation.</summary>
        public string ProfileId { get; }

        /// <summary>Creates a process plan.</summary>
        public LaunchPlan(string profileId, ProcessStartInfo startInfo)
        {
            ProfileId = profileId ?? throw new ArgumentNullException(nameof(profileId));
            StartInfo = startInfo ?? throw new ArgumentNullException(nameof(startInfo));
        }
    }
}
