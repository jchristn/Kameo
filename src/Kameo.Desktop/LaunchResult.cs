namespace Kameo.Desktop
{
    /// <summary>How a launch through the options dialog ended.</summary>
    internal enum LaunchResult
    {
        /// <summary>A terminal opened for the session.</summary>
        Launched,
        /// <summary>The user closed the dialog without launching.</summary>
        Canceled,
        /// <summary>The launch failed and the manager window reported why.</summary>
        Failed
    }
}
