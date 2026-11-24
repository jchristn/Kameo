namespace Kameo.Core
{
    using System;
    using System.Collections.Generic;

    /// <summary>Plan usage for one profile. Contains no credential material.</summary>
    public sealed class UsageReport
    {
        /// <summary>The profile identifier.</summary>
        public string ProfileId { get; init; } = "";
        /// <summary>The profile display name.</summary>
        public string ProfileName { get; init; } = "";
        /// <summary>The profile harness.</summary>
        public HarnessKind Harness { get; init; }
        /// <summary>The check outcome.</summary>
        public UsageStatus Status { get; init; }
        /// <summary>Where the figures came from.</summary>
        public UsageSource Source { get; init; }
        /// <summary>An explanation for non-available results, or a note about the figures. May be empty.</summary>
        public string Message { get; init; } = "";
        /// <summary>The subscription plan reported by the provider or the saved login, such as "Max 20x". May be empty.</summary>
        public string Plan { get; init; } = "";
        /// <summary>Additional plan state, such as a credit balance. May be empty.</summary>
        public string Detail { get; init; } = "";
        /// <summary>When the figures were observed, in UTC.</summary>
        public DateTimeOffset? ObservedUtc { get; init; }
        /// <summary>Limit windows in display order. Empty unless <see cref="Status"/> is <see cref="UsageStatus.Available"/>.</summary>
        public IReadOnlyList<UsageWindow> Windows { get; init; } = Array.Empty<UsageWindow>();

        internal static UsageReport For(Profile profile, UsageStatus status, string message) => new UsageReport
        {
            ProfileId = profile.Id, ProfileName = profile.Name, Harness = profile.Harness, Status = status, Message = message
        };
    }
}
