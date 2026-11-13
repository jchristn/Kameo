namespace Kameo.Core
{
    using System;
    using System.Collections.Generic;

    /// <summary>Harness names and native storage contracts.</summary>
    public static class HarnessCatalog
    {
        /// <summary>All supported harnesses in display order.</summary>
        public static IReadOnlyList<HarnessKind> All { get; } = Array.AsReadOnly(new[] { HarnessKind.Claude, HarnessKind.Codex, HarnessKind.Gemini, HarnessKind.Mux });
        /// <summary>Returns the display name.</summary>
        public static string Name(HarnessKind kind) => kind switch
        {
            HarnessKind.Claude => "Claude Code", HarnessKind.Codex => "Codex",
            HarnessKind.Gemini => "Gemini CLI", HarnessKind.Mux => "Mux",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        /// <summary>Returns the command name.</summary>
        public static string Command(HarnessKind kind) => kind switch
        {
            HarnessKind.Claude => "claude", HarnessKind.Codex => "codex",
            HarnessKind.Gemini => "gemini", HarnessKind.Mux => "mux",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        /// <summary>Parses a harness command name.</summary>
        /// <exception cref="ArgumentException">The name is unsupported.</exception>
        public static HarnessKind Parse(string value) => value.ToLowerInvariant() switch
        {
            "claude" => HarnessKind.Claude, "codex" => HarnessKind.Codex,
            "gemini" => HarnessKind.Gemini, "mux" => HarnessKind.Mux,
            _ => throw new ArgumentException("Harness must be claude, codex, gemini, or mux.", nameof(value))
        };
        /// <summary>Returns the profile-relative native credential filename.</summary>
        public static string CredentialFile(HarnessKind kind) => kind switch
        {
            HarnessKind.Claude => ".credentials.json", HarnessKind.Codex => "auth.json",
            HarnessKind.Gemini => ".gemini/oauth_creds.json", HarnessKind.Mux => "endpoints.json",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
