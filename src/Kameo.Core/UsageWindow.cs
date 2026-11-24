namespace Kameo.Core
{
    using System;

    /// <summary>One plan limit window, such as a five-hour session allowance or a weekly allowance.</summary>
    public sealed class UsageWindow
    {
        /// <summary>Creates a window.</summary>
        /// <param name="label">Short display label, such as "5-hour" or "Weekly".</param>
        /// <param name="usedPercent">Percentage used. Negative and non-finite values are treated as zero.</param>
        /// <param name="resetsAt">When the window resets, if known.</param>
        /// <param name="windowMinutes">Window length in minutes, if known.</param>
        public UsageWindow(string label, double usedPercent, DateTimeOffset? resetsAt, int? windowMinutes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
            Label = label;
            UsedPercent = double.IsFinite(usedPercent) && usedPercent > 0 ? usedPercent : 0;
            ResetsAt = resetsAt;
            WindowMinutes = windowMinutes;
        }

        /// <summary>Short display label, such as "5-hour" or "Weekly".</summary>
        public string Label { get; }
        /// <summary>Percentage of the allowance used, normally 0 to 100.</summary>
        public double UsedPercent { get; }
        /// <summary>When the window resets, if known.</summary>
        public DateTimeOffset? ResetsAt { get; }
        /// <summary>Window length in minutes, if known.</summary>
        public int? WindowMinutes { get; }
    }
}
