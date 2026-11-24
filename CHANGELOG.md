# Changelog

All notable changes to Kameo are recorded here. Versions follow [Semantic Versioning](https://semver.org); every 0.x.y release is an alpha.

## [Unreleased]

## [0.1.0] - 2026-11-20

Alpha. Initial release.

- Plan usage for Claude Code and Codex native logins. Opening Claude Code or Codex in the desktop checks each login's 5-hour, weekly, and per-model limits and shows them as bars with the percentage used and reset times. Results are reused for 15 minutes; **Refresh usage** (or **View > Refresh Usage**) checks again now. `kameo usage [claude|codex|ID] [--offline] [--json]` prints the same in a terminal.
- **Launch lowest usage** starts the login with the most headroom: the one whose tightest limit has the lowest percentage used.
- Claude Code usage is read with the profile's saved sign-in and is never refreshed by Kameo. Codex usage comes from the profile's isolated `codex app-server`, falling back to limits recorded in the profile's last session. API-key profiles report no plan limits.
- Each profile card has its own **Launch** and **Launch in...** buttons, so any account can be launched without changing the selection. The separate launch card showing the `kameo claude`/`kameo codex` command is removed; **Use profile** still sets the profile used by terminal commands and the tray.
- **Import** is one menu with the current login and a credential file. Edit and delete are gear and trash icons at the right of each card.

- Allow multiple simultaneous harness sessions using the same profile and login. Keep sign-in and destructive profile changes exclusive, and avoid restoring stale Claude credentials when another session is running.
- When updating from an earlier 0.1.0 build, close existing harness sessions once to release their old exclusive locks. New sessions can then share the same profile.

- Initial C# and Avalonia desktop, tray, and command-line applications.
- API-key profiles for Claude Code, Codex, Antigravity, and Mux; native logins for Claude Code, Codex, and Antigravity; endpoint-file profiles for Mux.
- Antigravity Google account profiles with isolated renewable token files, browser-code sign-in through the CLI, imports, concurrent accounts, and preserved legacy login data.
- Antigravity account launches require verified CLI 1.5.7, disable its automatic updater, and reject authentication/directory overrides.
- Shared Mux endpoints-file selection, refreshed at each launch without modifying the source.
- Isolated harness directories, renewable native credentials, explicit imports, and launch wrappers.
- Compact profile manager, 16 coordinated palettes, light/dark modes, and native macOS menus.
- **Launch in...** chooses a directory for one session while preserving the saved launch folder.
- Profile launch submenus, search width aligned with profile cards, and a shared About/startup splash design with palette-aware logos and a repository link.
- The About and startup splash **Close** button centers its label.
- The tray menu has **About Kameo**, and each harness submenu launches the selected profile with **Launch** or **Launch in...**.
- Source-build lifecycle scripts for Windows, macOS, Linux, WSL, and WSL2.
- Optional local .NET global-tool packaging for the `kameo` command, with build, install, rebuild, PATH, and removal instructions.
- Unsigned installers for Windows, macOS, and Linux on x64 and ARM64: Inno Setup `.exe` and portable `.zip`; ad-hoc-signed `.pkg`, `.dmg`, and `.tar.gz`; `.deb`, `.rpm`, AppImage, and `.tar.gz`. Installers add `kameo` to `PATH` and register the tray app at sign-in. Built by `Kameo.Publisher` from `publisher.json` and published by a tag-triggered release workflow with `SHA256SUMS`.
- The desktop finds the CLI installed beside it before falling back to source-build locations and `PATH`.
- Shared Touchstone suites, xUnit v3, NUnit, desktop workflows, native startup smoke tests, and native AOT coverage.
- MIT-licensed source distribution with installation, usage, troubleshooting, and issue-reporting documentation.
