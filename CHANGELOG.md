# Changelog

All notable changes to Kameo are recorded here. Versions follow [Semantic Versioning](https://semver.org); every 0.x.y release is an alpha.

## [Unreleased]

- macOS: Kameo reads a profile's Keychain login stored under your user name first, as Claude Code does. A stale item Claude had saved under another name (such as `unknown`) with the same service could hide a fresh sign-in, so usage kept saying the saved login had no access token. Deleting a profile removes every item with its service.

## [0.2.3] - 2026-11-28

Alpha. Claude sign-ins you can recover without closing sessions.

- Claude Code sign-in works while sessions for that profile are open. When Claude clears a profile's login after a rejected renewal, **Sign in** no longer says the profile is in use; launches of that profile wait until the sign-in finishes, and sessions for other profiles are unaffected. Codex, Antigravity and Mux sign-in still need the profile's sessions closed.
- On macOS, every Claude usage check, including the desktop's 15-minute background check, saves the Keychain login to the profile file while sessions run, and puts back a login Claude cleared (once per saved version), instead of only at launch and exit.
- Claude profile cards and `kameo usage` show the signed-in account, flag a profile named with an email address that is signed in to a different account, and warn five days before a saved sign-in can no longer be renewed. `kameo login` prints the signed-in account and warns when it changed. `UsageReport` gains `Account` and `SignInEndsUtc`, also in `kameo usage --json`.
- Tests: the desktop test runner exits after its suite finishes. It disposed the headless Avalonia session on that session's own dispatcher thread and waited on itself.

## [0.2.2] - 2026-11-27

Alpha. Antigravity version support.

- Antigravity Google account profiles accept any CLI release from 1.5.7 on, including new minor and major versions such as 1.6 and 2.0. Only releases older than 1.5.7 are rejected. Kameo also finds the version number inside decorated output such as `agy version 1.6.3 (build abc123)`. The minimum is `AntigravityCompatibility.MinimumVersion`.

## [0.2.1] - 2026-11-27

Alpha. Code-standard compliance; no behavior changes for users.

- The version shown by `kameo --version`, the desktop About window and sidebar, and Kameo's outbound user agents now comes from the build, so a release changes it in one place.
- Removed every `partial` type. Regular expressions are constructed once at startup instead of source-generated, and `publisher.json` is read by a hand-written mapper that keeps native AOT support and now names the exact property when a value is unknown or has the wrong type.
- `UsageService` is disposable and releases any HTTP client it creates; the CLI, desktop, and tests dispose it. `UsageService.MaxConcurrentReads` (default 4, range 1 to 64) sets how many profiles a full usage check reads at once.
- Asynchronous helpers accept cancellation tokens, a bare `catch` was replaced with `try`/`finally`, test assertions throw a dedicated `TestFailureException` so a test can no longer swallow its own failure, and private members no longer carry XML documentation.
- Documentation: Antigravity Google account profiles accept CLI 1.5.7 or any later 1.5.x release, as they have since 0.1.1.

## [0.2.0] - 2026-11-26

Alpha. Sign-ins that stay valid, and launch options.

- Idle Claude Code sign-ins no longer expire between sessions. When a usage check finds a sign-in within 30 minutes of expiring, or Anthropic rejects it, Kameo renews it as Claude Code does and saves the rotated tokens to the profile file and its macOS Keychain entry. Profiles with an open session are left to Claude Code, so renewal never invalidates a running session's tokens. A revoked sign-in now says to sign in again.
- Launching opens a dialog with the folder and a permission level, replacing **Launch in...**. Levels map to each harness's flags, including Claude Code's `--dangerously-skip-permissions`, Codex's `--dangerously-bypass-approvals-and-sandbox`, and Mux's `--yolo`, with a warning for the bypass levels. The last level per harness is remembered, and the folder can be saved as the new default. Cards, the Profiles menu, **Launch lowest usage**, and the tray's **Launch...** and **Lowest usage...** all use it; tray launches show only the dialog.
- Launching from the tray brings only the launch dialog forward. The manager window no longer comes to the front when the app activates; clicking the Dock icon still opens it.

## [0.1.2] - 2026-11-25

Alpha. Automatic usage refresh.

- The desktop refreshes plan usage for every Claude Code and Codex native login in the background every 15 minutes, matching the usage cache, including while the window is hidden in the tray. Usage is current when you return, and a login that needs to sign in again shows up without clicking **Refresh usage**.
- The usage status line now reads "{harness} usage checked at {time}" in local time.

## [0.1.1] - 2026-11-24

Alpha. Windows sign-in fixes.

- Windows: the npm-shim launcher now targets a shim's actual invocation line, so `codex` (whose `.cmd` probes for a bundled `node.exe` before the exec line) is no longer mistaken for Node and run as a syntax check.
- Windows: harness executables are also resolved from the persisted user and machine PATH, not only the running process's copy, so a CLI added to PATH after Kameo started (such as Antigravity's `agy`) is found without restarting.
- Antigravity Google account profiles accept CLI 1.5.7 or any later 1.5.x release, instead of requiring exactly 1.5.7.

## [0.1.0] - 2026-11-24

Alpha. Initial release.

- Windows: show the Kameo logo in the main window's taskbar entry and title bar.
- Windows: Sign in and Launch open through a `.cmd` launcher that keeps the console open after the session ends, matching the macOS behaviour. Errors are no longer flashed away before you can read them.
- Windows: resolve harness executables by probing `.exe`, `.cmd`, and `.bat` before the extensionless filename, so npm global bins (where `claude` is an sh script and `claude.cmd` is the real shim) launch correctly. The npm-shim parser also accepts shims that invoke a sibling `.exe`, which is how current Claude Code is packaged.

- Claude Code logins on macOS persist across new terminal windows. The profile's Keychain login is now authoritative: Kameo saves it at every launch and exit and restores its saved copy only when the Keychain login is missing, cleared, or older. Previously a launch restored the saved copy unconditionally, replacing refreshed tokens and forcing `/login`. Closing a terminal window now ends the session cleanly instead of skipping the save, and importing a Claude credential file replaces the profile's Keychain login.

- Verify simultaneous independent profiles for all four harnesses in API-key and native/file mode, including sign-in to one profile while another remains open. Clarify that busy-profile restrictions apply only to that profile.

- Pin Claude's separate credential and Anthropic fallback stores to the selected profile, and remove inherited profile and federation selectors that could choose another account.
- Use email-shaped Claude profile names as sign-in hints, with recovery guidance for a browser authorizing the wrong account.

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
- The tray menu has **About Kameo**. Each harness lists its profiles, ticking the selected one, and each profile has a submenu with **Launch**, **Launch in...**, and **Use as default**. Claude Code and Codex with two or more native logins also have **Lowest usage** (**Launch** or **Launch in...**) at the top, using the same 15-minute usage cache as the manager.
- Source-build lifecycle scripts for Windows, macOS, Linux, WSL, and WSL2.
- Optional local .NET global-tool packaging for the `kameo` command, with build, install, rebuild, PATH, and removal instructions.
- Unsigned installers for Windows, macOS, and Linux on x64 and ARM64: Inno Setup `.exe` and portable `.zip`; ad-hoc-signed `.pkg`, `.dmg`, and `.tar.gz`; `.deb`, `.rpm`, AppImage, and `.tar.gz`. Installers add `kameo` to `PATH` and register the tray app at sign-in. Built by `Kameo.Publisher` from `publisher.json` and published by a tag-triggered release workflow with `SHA256SUMS`.
- The desktop finds the CLI installed beside it before falling back to source-build locations and `PATH`.
- Shared Touchstone suites, xUnit v3, NUnit, desktop workflows, native startup smoke tests, and native AOT coverage.
- MIT-licensed source distribution with installation, usage, troubleshooting, and issue-reporting documentation.
