namespace Kameo.Core
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Reads subscription plan limits for native Claude Code and Codex profiles, and never logs credentials. A Claude Code
    /// sign-in that is about to expire is renewed when no session is using its profile; Codex renews its own. Instances
    /// are safe for concurrent callers.
    /// </summary>
    public sealed class UsageService
    {
        private static readonly Lazy<HttpClient> _SharedClient = new Lazy<HttpClient>(() => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
        private readonly ProfileStore _Store;
        private readonly HttpClient _Client;
        private readonly Uri _ClaudeEndpoint;
        private readonly Uri _TokenEndpoint;
        private readonly IClaudeKeychain? _Keychain;
        private readonly TimeProvider _Clock;
        private readonly TimeSpan _CodexTimeout;

        /// <summary>Creates a usage reader for a profile store.</summary>
        public UsageService(ProfileStore store) : this(store, null, null, null, null, null, OperatingSystem.IsMacOS() ? new SecurityKeychain() : null) { }

        /// <summary>Creates a usage reader with substitutes. A null keychain reads and renews Claude logins in the profile file only.</summary>
        internal UsageService(ProfileStore store, HttpMessageHandler? handler, Uri? claudeEndpoint, TimeProvider? clock, TimeSpan? codexTimeout,
            Uri? tokenEndpoint = null, IClaudeKeychain? keychain = null)
        {
            _Store = store ?? throw new ArgumentNullException(nameof(store));
            _Client = handler == null ? _SharedClient.Value : new HttpClient(handler, false) { Timeout = Timeout.InfiniteTimeSpan };
            _ClaudeEndpoint = claudeEndpoint ?? ClaudeUsage.DefaultEndpoint;
            _TokenEndpoint = tokenEndpoint ?? ClaudeTokenRefresh.DefaultEndpoint;
            _Keychain = keychain;
            _Clock = clock ?? TimeProvider.System;
            _CodexTimeout = codexTimeout ?? CodexUsage.LiveTimeout;
        }

        /// <summary>Whether a profile can report plan usage: a native Claude Code or Codex login.</summary>
        public static bool Supports(Profile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            return HarnessCatalog.SupportsUsage(profile.Harness) && profile.Kind == CredentialKind.Native;
        }

        /// <summary>
        /// Chooses the login with the most headroom: the available report whose tightest limit (<see cref="UsageReport.PeakPercent"/>)
        /// is lowest. Ties go to the earlier report. Returns null when no report has usage windows.
        /// </summary>
        public static UsageReport? LeastUsed(IEnumerable<UsageReport> reports)
        {
            ArgumentNullException.ThrowIfNull(reports);
            UsageReport? best = null;
            foreach (UsageReport report in reports)
            {
                if (report.Status != UsageStatus.Available || report.Windows.Count == 0) continue;
                if (best == null || report.PeakPercent < best.PeakPercent) best = report;
            }
            return best;
        }

        /// <summary>Checks one profile. Failures are returned as reports with a display-safe message rather than thrown.</summary>
        /// <param name="id">Profile identifier.</param>
        /// <param name="offline">True to use only local data: Codex session logs. No network or child process is used, and nothing is renewed.</param>
        /// <param name="cancellationToken">Cancels the check.</param>
        /// <exception cref="KeyNotFoundException">The profile does not exist.</exception>
        public async Task<UsageReport> ReadAsync(string id, bool offline, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Profile profile = _Store.Get(id);
            if (!HarnessCatalog.SupportsUsage(profile.Harness))
                return UsageReport.For(profile, UsageStatus.NotApplicable, "Plan usage is available for Claude Code and Codex.");
            if (profile.Kind != CredentialKind.Native)
                return UsageReport.For(profile, UsageStatus.NotApplicable, "API keys are billed per token and have no plan limits.");
            DateTimeOffset now = _Clock.GetUtcNow();
            try
            {
                return profile.Harness == HarnessKind.Claude
                    ? await ClaudeUsage.ReadAsync(_Store, profile, _Client, _ClaudeEndpoint, _TokenEndpoint, _Keychain, _Clock, offline, cancellationToken).ConfigureAwait(false)
                    : await CodexUsage.ReadAsync(_Store, profile, now, offline, _CodexTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException
                or ArgumentException or System.Text.Json.JsonException or HttpRequestException or OperationCanceledException or System.ComponentModel.Win32Exception)
            {
                // Exception text from vendor JSON or child processes can contain credential material, so it is not shown.
                return UsageReport.For(profile, UsageStatus.Unavailable, "Unable to read usage for this profile.");
            }
        }

        /// <summary>Checks every Claude Code and Codex profile, optionally for one harness, in profile-list order.</summary>
        /// <param name="harness">Limits the check to one harness, or null for all.</param>
        /// <param name="offline">True to use only local data.</param>
        /// <param name="cancellationToken">Cancels the checks.</param>
        public async Task<IReadOnlyList<UsageReport>> ReadAllAsync(HarnessKind? harness, bool offline, CancellationToken cancellationToken)
        {
            List<Profile> profiles = _Store.List().Where(profile => HarnessCatalog.SupportsUsage(profile.Harness) && (harness == null || profile.Harness == harness)).ToList();
            using SemaphoreSlim gate = new SemaphoreSlim(4);
            Task<UsageReport>[] checks = profiles.Select(async profile =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try { return await ReadAsync(profile.Id, offline, cancellationToken).ConfigureAwait(false); }
                catch (KeyNotFoundException) { return UsageReport.For(profile, UsageStatus.Unavailable, "The profile was deleted."); }
                finally { gate.Release(); }
            }).ToArray();
            return await Task.WhenAll(checks).ConfigureAwait(false);
        }
    }
}
