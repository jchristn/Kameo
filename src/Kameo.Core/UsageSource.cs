namespace Kameo.Core
{
    /// <summary>Where usage figures came from.</summary>
    public enum UsageSource
    {
        /// <summary>No figures were read.</summary>
        None,
        /// <summary>Read from the provider when the check ran.</summary>
        Live,
        /// <summary>Recorded by the harness in its local session log during the profile's most recent session.</summary>
        SessionLog
    }
}
