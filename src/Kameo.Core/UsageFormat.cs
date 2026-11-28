namespace Kameo.Core
{
    using System;
    using System.Globalization;
    using System.Net.Mail;

    /// <summary>Shared, culture-invariant text for usage reports in the desktop and CLI.</summary>
    public static class UsageFormat
    {
        /// <summary>Returns a short label for a window length, such as "5-hour" or "Weekly".</summary>
        public static string WindowLabel(int? minutes) => minutes switch
        {
            null or <= 0 => "Limit",
            300 => "5-hour",
            1440 => "Daily",
            10080 => "Weekly",
            >= 40320 and <= 44640 => "Monthly",
            int value when value % 1440 == 0 => (value / 1440).ToString(CultureInfo.InvariantCulture) + "-day",
            int value when value % 60 == 0 => (value / 60).ToString(CultureInfo.InvariantCulture) + "-hour",
            int value => value.ToString(CultureInfo.InvariantCulture) + "-minute"
        };

        /// <summary>Formats a percentage, rounded to a whole number.</summary>
        public static string Percent(double value) => Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";

        /// <summary>Describes when a window resets relative to <paramref name="now"/>. Empty when unknown.</summary>
        public static string Reset(DateTimeOffset? resetsAt, DateTimeOffset now)
        {
            if (resetsAt == null) return "";
            TimeSpan remaining = resetsAt.Value - now;
            if (remaining <= TimeSpan.Zero) return "reset";
            if (remaining < TimeSpan.FromHours(1)) return "resets in " + Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes)).ToString(CultureInfo.InvariantCulture) + "m";
            if (remaining < TimeSpan.FromHours(24))
                return "resets in " + ((int)remaining.TotalHours).ToString(CultureInfo.InvariantCulture) + "h " + remaining.Minutes.ToString(CultureInfo.InvariantCulture) + "m";
            return "resets " + resetsAt.Value.ToLocalTime().ToString("ddd MMM d, h:mm tt", CultureInfo.InvariantCulture);
        }

        /// <summary>How long before a saved sign-in ends that the desktop and CLI start warning about it.</summary>
        public static readonly TimeSpan SignInWarning = TimeSpan.FromDays(5);

        /// <summary>
        /// Warns that a sign-in will soon stop renewing, such as "Sign-in ends Tue Dec 22, 9:03 AM. Sign in again before then.".
        /// Empty when the end is unknown or more than <see cref="SignInWarning"/> away.
        /// </summary>
        public static string SignInEnds(UsageReport report, DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(report);
            if (report.SignInEndsUtc == null || report.SignInEndsUtc.Value - now > SignInWarning) return "";
            if (report.SignInEndsUtc.Value <= now) return "This sign-in can no longer be renewed. Sign in again.";
            return "Sign-in ends " + report.SignInEndsUtc.Value.ToLocalTime().ToString("ddd MMM d, h:mm tt", CultureInfo.InvariantCulture) + ". Sign in again before then.";
        }

        /// <summary>
        /// Names the signed-in account, such as "Signed in as name@example.com", and warns when a profile named with an
        /// email address is signed in to a different one. Empty when the account is unknown.
        /// </summary>
        public static string Account(UsageReport report)
        {
            ArgumentNullException.ThrowIfNull(report);
            if (report.Account.Length == 0) return "";
            if (WrongAccount(report)) return "Signed in as " + report.Account + ", not " + report.ProfileName + ". Sign in again with the right account.";
            return "Signed in as " + report.Account;
        }

        /// <summary>Whether a profile named with an email address is signed in to an account with a different address.</summary>
        public static bool WrongAccount(UsageReport report)
        {
            ArgumentNullException.ThrowIfNull(report);
            // Matches the names that sign-in passes to Claude Code as an --email hint.
            return report.Account.Length > 0 && MailAddress.TryCreate(report.ProfileName, out MailAddress? address)
                && string.Equals(address.Address, report.ProfileName, StringComparison.Ordinal)
                && !string.Equals(report.Account, report.ProfileName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Describes the source and age of a report, such as "Live, 3:12 PM" or "Last session, Nov 21 3:28 AM".</summary>
        public static string Observed(UsageReport report, DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(report);
            if (report.ObservedUtc == null || report.Source == UsageSource.None) return "";
            DateTimeOffset local = report.ObservedUtc.Value.ToLocalTime();
            string when = local.Date == now.ToLocalTime().Date
                ? local.ToString("h:mm tt", CultureInfo.InvariantCulture)
                : local.ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture);
            return (report.Source == UsageSource.Live ? "Live, " : "Last session, ") + when;
        }
    }
}
