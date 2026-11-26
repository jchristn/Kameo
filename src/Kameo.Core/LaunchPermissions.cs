namespace Kameo.Core
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The permission levels each harness offers at launch. Flags were checked against Claude Code 2.1.332, Codex 0.171.0,
    /// Antigravity CLI 1.5.18 and Mux 1.8.0 help output. The first level of every harness passes no arguments.
    /// </summary>
    public static class LaunchPermissions
    {
        /// <summary>The identifier of the level that leaves the harness's own permission settings in effect.</summary>
        public const string DefaultId = "default";

        private static readonly IReadOnlyList<LaunchPermission> _Claude = Array.AsReadOnly(new[]
        {
            Default("Claude Code asks before edits and commands, following this profile's settings."),
            new LaunchPermission("accept-edits", "Accept edits", "File edits are applied without asking. Commands still ask.", new[] { "--permission-mode", "acceptEdits" }, false),
            new LaunchPermission("plan", "Plan only", "Claude Code researches and proposes a plan without changing files.", new[] { "--permission-mode", "plan" }, false),
            new LaunchPermission("auto", "Auto mode", "A classifier approves routine actions and asks about risky ones.", new[] { "--permission-mode", "auto" }, false),
            new LaunchPermission("bypass", "Skip all permission prompts", "Runs every tool and command without asking (--dangerously-skip-permissions).", new[] { "--dangerously-skip-permissions" }, true)
        });

        private static readonly IReadOnlyList<LaunchPermission> _Codex = Array.AsReadOnly(new[]
        {
            Default("Codex uses this profile's approval and sandbox settings."),
            new LaunchPermission("read-only", "Read only", "Commands run in a read-only sandbox.", new[] { "--sandbox", "read-only" }, false),
            new LaunchPermission("approve-for-me", "Automatic review", "Approval requests go to automatic review inside the workspace-write sandbox.", new[] { "--approve-for-me" }, false),
            new LaunchPermission("bypass", "Bypass approvals and sandbox", "Runs every command without asking or sandboxing (--dangerously-bypass-approvals-and-sandbox).", new[] { "--dangerously-bypass-approvals-and-sandbox" }, true)
        });

        private static readonly IReadOnlyList<LaunchPermission> _Antigravity = Array.AsReadOnly(new[]
        {
            Default("Antigravity asks before tool use, following this profile's settings."),
            new LaunchPermission("accept-edits", "Accept edits", "File edits are applied without asking.", new[] { "--mode", "accept-edits" }, false),
            new LaunchPermission("plan", "Plan only", "The agent proposes a plan without changing files.", new[] { "--mode", "plan" }, false),
            new LaunchPermission("auto", "Auto mode", "The agent decides when to ask.", new[] { "--mode", "auto" }, false),
            new LaunchPermission("bypass", "Skip all permission prompts", "Approves every tool request without asking (--dangerously-skip-permissions).", new[] { "--dangerously-skip-permissions" }, true)
        });

        private static readonly IReadOnlyList<LaunchPermission> _Mux = Array.AsReadOnly(new[]
        {
            Default("Mux asks before tool calls, following this profile's settings."),
            new LaunchPermission("deny", "Deny tool calls", "Tool calls are refused without asking.", new[] { "--approval-policy", "deny" }, false),
            new LaunchPermission("bypass", "YOLO", "Approves every tool call without asking (--yolo).", new[] { "--yolo" }, true)
        });

        /// <summary>Returns a harness's permission levels in display order, starting with the default.</summary>
        public static IReadOnlyList<LaunchPermission> For(HarnessKind harness) => harness switch
        {
            HarnessKind.Claude => _Claude, HarnessKind.Codex => _Codex,
            HarnessKind.Antigravity => _Antigravity, HarnessKind.Mux => _Mux,
            _ => throw new ArgumentOutOfRangeException(nameof(harness))
        };

        /// <summary>Returns a harness's level by identifier, or its default level when the identifier is unknown or null.</summary>
        public static LaunchPermission Find(HarnessKind harness, string? id)
        {
            IReadOnlyList<LaunchPermission> levels = For(harness);
            foreach (LaunchPermission level in levels)
                if (string.Equals(level.Id, id, StringComparison.Ordinal)) return level;
            return levels[0];
        }

        /// <summary>The preference name that remembers a harness's last chosen level.</summary>
        public static string PreferenceName(HarnessKind harness) => "launch-permission-" + HarnessCatalog.Command(harness);

        private static LaunchPermission Default(string description) => new LaunchPermission(DefaultId, "Ask (harness default)", description, Array.Empty<string>(), false);
    }
}
