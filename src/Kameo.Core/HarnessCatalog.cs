namespace Kameo.Core
{
    using System;
    using System.Collections.Generic;

    /// <summary>Harness names and native storage contracts.</summary>
    public static class HarnessCatalog
    {
        /// <summary>All supported harnesses in display order.</summary>
        public static IReadOnlyList<HarnessKind> All { get; } = Array.AsReadOnly(new[] { HarnessKind.Claude, HarnessKind.Codex, HarnessKind.Antigravity, HarnessKind.Mux });
        /// <summary>Returns the display name.</summary>
        public static string Name(HarnessKind kind) => kind switch
        {
            HarnessKind.Claude => "Claude Code", HarnessKind.Codex => "Codex",
            HarnessKind.Antigravity => "Antigravity", HarnessKind.Mux => "Mux",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        /// <summary>Returns the command name.</summary>
        public static string Command(HarnessKind kind) => kind switch
        {
            HarnessKind.Claude => "claude", HarnessKind.Codex => "codex",
            HarnessKind.Antigravity => "antigravity", HarnessKind.Mux => "mux",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        /// <summary>Returns the installed harness executable, which may differ from the Kameo subcommand.</summary>
        public static string Executable(HarnessKind kind) => kind == HarnessKind.Antigravity ? "agy" : Command(kind);
        /// <summary>Whether Kameo can isolate native account authentication for this harness.</summary>
        public static bool SupportsNativeLogin(HarnessKind kind) => Enum.IsDefined(kind);
        /// <summary>Parses a harness command name.</summary>
        /// <exception cref="ArgumentException">The name is unsupported.</exception>
        public static HarnessKind Parse(string value) => value.ToLowerInvariant() switch
        {
            "claude" => HarnessKind.Claude, "codex" => HarnessKind.Codex,
            "antigravity" or "agy" or "gemini" => HarnessKind.Antigravity, "mux" => HarnessKind.Mux,
            _ => throw new ArgumentException("Harness must be claude, codex, antigravity, or mux.", nameof(value))
        };
        /// <summary>Returns the profile-relative native credential filename.</summary>
        public static string CredentialFile(HarnessKind kind) => kind switch
        {
            HarnessKind.Claude => ".credentials.json", HarnessKind.Codex => "auth.json",
            HarnessKind.Antigravity => "antigravity-cli/antigravity-oauth-token", HarnessKind.Mux => "endpoints.json",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
