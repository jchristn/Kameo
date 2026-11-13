# Changelog

## v0.1.0 · Alpha · 2026-11-12

- Initial C# and Avalonia desktop, tray, and command-line applications.
- API-key profiles for Claude Code, Codex, Antigravity, and Mux; native logins for Claude Code/Codex and endpoint-file profiles for Mux.
- Antigravity replaces Gemini CLI for Google API-key profiles, preserving legacy native-login data without reusing its credentials.
- Shared Mux endpoints-file selection, refreshed at each launch without modifying the source.
- Isolated harness directories, renewable native credentials, explicit imports, and launch wrappers.
- Compact profile manager, 16 coordinated palettes, light/dark modes, and native macOS menus.
- **Launch in...** chooses a directory for one session while preserving the saved launch folder.
- Source-build lifecycle scripts for Windows, macOS, Linux, WSL, and WSL2.
- Shared Touchstone suites, xUnit v3, NUnit, desktop workflows, native startup smoke tests, and native AOT coverage.
- MIT-licensed source distribution with installation, usage, troubleshooting, and issue-reporting documentation.
