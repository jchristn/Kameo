# Changelog

## v0.1.0 · Alpha · 2026-11-12

- Initial C# and Avalonia desktop, tray, and command-line applications.
- API-key profiles for Claude Code, Codex, Antigravity, and Mux; native logins for Claude Code, Codex, and Antigravity; endpoint-file profiles for Mux.
- Antigravity Google account profiles with isolated renewable token files, browser-code sign-in through the CLI, imports, concurrent accounts, and preserved legacy login data.
- Antigravity account launches require verified CLI 1.5.7, disable its automatic updater, and reject authentication/directory overrides.
- Shared Mux endpoints-file selection, refreshed at each launch without modifying the source.
- Isolated harness directories, renewable native credentials, explicit imports, and launch wrappers.
- Compact profile manager, 16 coordinated palettes, light/dark modes, and native macOS menus.
- **Launch in...** chooses a directory for one session while preserving the saved launch folder.
- Profile launch submenus, search width aligned with profile cards, and a shared About/startup splash design with palette-aware logos and a repository link.
- Source-build lifecycle scripts for Windows, macOS, Linux, WSL, and WSL2.
- Optional local .NET global-tool packaging for the `kameo` command, with build, install, rebuild, PATH, and removal instructions.
- Shared Touchstone suites, xUnit v3, NUnit, desktop workflows, native startup smoke tests, and native AOT coverage.
- MIT-licensed source distribution with installation, usage, troubleshooting, and issue-reporting documentation.
