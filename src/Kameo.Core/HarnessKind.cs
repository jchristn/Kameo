namespace Kameo.Core
{
    /// <summary>Supported agent harnesses.</summary>
    public enum HarnessKind
    {
        /// <summary>Anthropic Claude Code.</summary>
        Claude,
        /// <summary>OpenAI Codex CLI.</summary>
        Codex,
        /// <summary>Google Antigravity CLI. Numeric value 2 preserves legacy Gemini profiles.</summary>
        Antigravity = 2,
        /// <summary>Mux CLI.</summary>
        Mux
    }
}
