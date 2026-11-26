namespace Kameo.Core
{
    /// <summary>The outcome of a plan usage check for one profile.</summary>
    public enum UsageStatus
    {
        /// <summary>Usage windows were read.</summary>
        Available,
        /// <summary>Plan limits do not apply, such as for API-key profiles or harnesses without usage support.</summary>
        NotApplicable,
        /// <summary>The profile has no saved native login.</summary>
        SignedOut,
        /// <summary>The saved login has expired or was rejected and could not be renewed. The message says whether to launch or sign in again.</summary>
        Expired,
        /// <summary>An exclusive operation, such as sign-in or editing, is using the profile.</summary>
        InUse,
        /// <summary>No local data exists and a live check was not requested.</summary>
        NotChecked,
        /// <summary>The check failed. The message explains why.</summary>
        Unavailable
    }
}
