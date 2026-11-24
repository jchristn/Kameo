namespace Kameo.Core
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;

    /// <summary>Reads the most recent plan limits Codex recorded in a profile's session logs. Never modifies the logs.</summary>
    internal static class CodexSessionLog
    {
        private const int _FilesToScan = 10;
        private const int _TailBytes = 2 * 1024 * 1024;

        internal static UsageReport? Read(Profile profile, string directory, DateTimeOffset now)
        {
            string sessions = Path.Combine(directory, "sessions");
            if (!Directory.Exists(sessions)) return null;
            PrivateFiles.CheckPath(sessions);
            List<FileInfo> files;
            try
            {
                files = new DirectoryInfo(sessions).EnumerateFiles("*.jsonl", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                    .OrderByDescending(file => file.LastWriteTimeUtc).Take(_FilesToScan).ToList();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
            UsageReport? newest = null;
            foreach (FileInfo file in files)
            {
                UsageReport? report = ReadFile(profile, file.FullName, now);
                if (report != null && (newest == null || report.ObservedUtc > newest.ObservedUtc)) newest = report;
            }
            return newest;
        }

        private static UsageReport? ReadFile(Profile profile, string path, DateTimeOffset now)
        {
            string text;
            try
            {
                using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long start = Math.Max(0, stream.Length - _TailBytes);
                stream.Seek(start, SeekOrigin.Begin);
                byte[] buffer = new byte[stream.Length - start];
                stream.ReadExactly(buffer);
                text = Encoding.UTF8.GetString(buffer);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
            string[] lines = text.Split('\n');
            for (int index = lines.Length - 1; index >= 0; index--)
            {
                string line = lines[index];
                if (!line.Contains("\"rate_limits\":{", StringComparison.Ordinal)) continue;
                try
                {
                    using JsonDocument document = JsonDocument.Parse(line);
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("payload", out JsonElement payload) || payload.ValueKind != JsonValueKind.Object
                        || !payload.TryGetProperty("rate_limits", out JsonElement limits) || limits.ValueKind != JsonValueKind.Object) continue;
                    if (!root.TryGetProperty("timestamp", out JsonElement stamp) || stamp.ValueKind != JsonValueKind.String
                        || !DateTimeOffset.TryParse(stamp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset observed)) continue;
                    UsageReport report = CodexUsage.Report(profile, limits, UsageSource.SessionLog, observed.ToUniversalTime(), now);
                    if (report.Windows.Count > 0) return report;
                }
                catch (JsonException) { }
            }
            return null;
        }
    }
}
