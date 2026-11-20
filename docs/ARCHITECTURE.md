# Architecture and integration contracts

## Components

| Project | Responsibility |
| --- | --- |
| `Kameo.Core` | Profile domain, structured SQLite storage, private files, native imports, launch planning, process supervision, startup registration |
| `Kameo.Cli` | Thin command composition and terminal process lifetime |
| `Kameo.Desktop` | Avalonia manager, tray, palettes, dialogs, native menus, terminal launching |
| `Test.Shared` | Runner-independent Touchstone contracts, filesystem and process integration tests |
| `Test.Automated`, `Test.Xunit3`, `Test.Nunit`, `Test.Aot` | Shared test runners |
| `Test.Desktop` | Touchstone UI workflows and native lifecycle smoke testing |
| `Test.Harness` | Disposable synthetic child executable for argument, environment, exit-code and cancellation tests |

There is no HTTP server, remote account service, database server, or web dashboard. The backend reference's HTTP, multitenancy, Docker, REST/MCP, Postman, and four database-provider requirements do not apply to this local filesystem application. Unsigned installers for Windows, macOS, and Linux (x64 and ARM64) are built by `Kameo.Publisher` from `publisher.json` and published on GitHub Releases; see `INSTALL.md` and `RELEASING.md`. Installed layouts put `kameo` beside the desktop executable (Program Files, `Kameo.app/Contents/MacOS`, `/opt/kameo`), and the desktop looks there first. Users can also build the CLI as a local `Kameo.Cli` .NET tool package and install the `kameo` command for their user; this does not install or update the desktop. Source version is `0.1.0`, with alpha status in documentation and the UI.

## Storage and concurrency

`kameo.db` contains typed columns for profile identity, harness, authentication kind, API key, executable override, shared Mux endpoints-file path, creation time, selected profile per harness, and non-secret preferences. Schema version 2 adds `mux_endpoints_file` transactionally with an empty default, preserving existing rows and selections. Foreign keys prevent selecting a profile belonging to another harness. Case-insensitive unique names are scoped to each harness. PrettyId supplies stable prefixed K-sortable IDs.

Connections are short-lived and independent; operations use SQL parameters and transactions. Reads return detached snapshots and redact keys by default. Native profile directories are stable across invocations. Profile leases serialize launches and prevent edits, imports, and deletion while a process owns its profile. Selection is independent of those leases.

Files written by Kameo use bounded reads, link/reparse-point rejection, sibling temporary files, flush and atomic replacement. The store requires a private local filesystem with SQLite and file-lock support. Unix permissions are owner-only. Credentials are not encrypted by Kameo. Imported files are copied, never moved or modified. Only the authentication artifact is imported; harness hooks, plugins, histories, and settings are not copied from a user's default installation.

Vendor-native JSON is intentionally unmanaged third-party payload data. Kameo validates a minimum authentication shape and retains additional fields. It does not use opaque JSON blobs for its own structured profile metadata. The harness can create further state within its own profile directory.

Deletion removes local profile data and selection. It does not revoke a provider token or sign out the original imported account. A failed filesystem deletion may require cleanup before retrying; SQLite rolls back the metadata deletion. Deleting native Claude profiles on macOS also deletes their directory-scoped Keychain entry.

## Harness contracts

### Claude Code

Use `CLAUDE_CONFIG_DIR` and `ANTHROPIC_API_KEY` for API profiles. Native login invokes `claude auth login`. Linux/Windows native credentials live in `.credentials.json`. On macOS, Claude uses a directory-scoped Keychain service; Kameo synchronizes that item with the profile file before and after a child process. Secrets are passed to the Keychain command through standard input in hexadecimal, never argv. A forced termination of the Kameo wrapper can interrupt the final snapshot, so an interrupted native login may need to be repeated.

The service naming contract is `Claude Code-credentials-` plus the first eight lowercase hexadecimal characters of SHA-256 over the normalized explicit config directory. The default login service is `Claude Code-credentials`. This was checked against the published Claude Code package and the installed CLI's login help. [Claude credential management](https://code.claude.com/docs/en/authentication) documents the directory-scoped behavior; [environment variables](https://code.claude.com/docs/en/env-vars) documents configuration and API-key inputs.

### Codex

Set `CODEX_HOME` and pass `-c cli_auth_credentials_store="file"`. API profiles write their key into the profile's `auth.json`; native profiles retain the CLI's renewable login document. Browser login invokes `codex login`, with optional `--device-auth` forwarded by the CLI. No third-party OAuth implementation is involved. [Official authentication documentation](https://developers.openai.com/codex/auth/) describes the file store and native refresh behavior.

### Antigravity

`kameo antigravity` invokes `agy` with `--gemini_dir` pointing to a stable profile directory. API profiles supply `GEMINI_API_KEY` through the child environment and enforce `--settings {"modelProvider":"gemini"}`. Account profiles remove inherited provider credentials, remove the persisted `modelProvider`, and override it with an empty value for the native provider. Other profile-local settings are retained.

**Google account support is restricted to CLI 1.5.7.** Its SSH-session detector selects file-only token load/save/refresh/remove operations instead of the shared OS keyring. Kameo supplies a nonempty `SSH_CONNECTION` in the account child environment, preserving a real existing value when present. The file is `<profile>/antigravity-cli/antigravity-oauth-token`. No parent environment is changed, but tools started by the CLI inherit the SSH indicator. This can affect tools that detect remote sessions.

Sign-in starts the normal interactive CLI. The user follows its browser URL and pastes the returned authorization code into its terminal. Antigravity performs the OAuth exchange and writes the renewable credential document itself; Kameo never handles the code. Imports require nested `token.access_token` and `token.refresh_token` strings with `auth_method` set to `consumer` or `gcp`, while retaining the rest of the document. `import-current` checks `~/.gemini/antigravity-cli/antigravity-oauth-token`; it does not access the shared keyring. Legacy `oauth_creds.json` is not an Antigravity token file.

Before each account session, a bounded five-second child `--version` probe must return exactly `1.5.7`. It uses the same isolated environment and directory; failures stop the launch before an account session starts. `AGY_CLI_DISABLE_AUTO_UPDATE=true` disables the CLI's automatic updater during managed account sessions. Directory/provider overrides, flag files, remote-control/remote-agent flags, and updater commands are rejected. Remote/background services are outside this integration. The version check is a compatibility gate, not executable signature verification. Users should point profiles at the official `agy` binary. External/manual CLI updates can make account launches unavailable until Kameo supports the new version.

Profile leases span the version probe and child process, so one profile cannot be edited, deleted, or launched twice concurrently. Different profiles can run concurrently. Tokens are saved directly by Antigravity, including refreshes, and are never restored from a stale snapshot. Kameo checks for links and enforces owner-only Unix token permissions before and after a session. Profile deletion removes only that profile's files and does not revoke the Google grant.

The [official installation guide](https://antigravity.google/docs/install?tab=cli) and [CLI reference](https://antigravity.google/docs/cli/reference) describe the product and sign-in flow. The directory flag and SSH-selected token store are internal implementation details, not a stable public contract. The official SHA-512-verified 1.5.7 binaries for macOS arm64, Linux amd64, and Windows amd64 were inspected for the same file-storage branch and path. A macOS runtime probe launched the real CLI through Kameo using two synthetic accounts, concurrent refreshes, and relaunches against a loopback-only mock OAuth server, with external network, actual home-directory reads, and keyring access denied. No real Google login or billable request was made. Fresh browser sign-in and actual Windows/Linux Antigravity execution still require platform acceptance; automated tests validate Kameo's contracts using a synthetic child executable.

Enum value 2 preserves existing Gemini metadata, keys, selections, and native profiles. Existing native profiles can sign in again to create the new token file; old `oauth_creds.json` files remain untouched. CLI names `gemini` and `agy` remain compatibility aliases. No database migration is required for account support.

### Mux

The reference is the local `~/Code/Mux` implementation: `SettingsLoader.GetConfigDirectory`, `EndpointConfig`, `docs/CONFIG.md`, and CLI dispatch. Kameo supplies `MUX_CONFIG_DIR`. Imported native profiles preserve `endpoints.json`, resolving API-key and header environment references at import time so the saved identity is independent of the importing shell. API profiles store a selected adapter, base URL, and model in that native file, with `${KAMEO_MUX_API_KEY}` resolved only in the child environment.

Mux has no standalone OAuth or `setup` CLI command. Starting its normal interactive TUI invokes the first-run wizard; setup remains available through its F1 command menu. Kameo does not submit the word `setup` as an agent prompt. Mux's current adapters determine which authentication methods are available.

Mux file profiles can instead persist a shared source path in `mux_endpoints_file`. Before each launch, under the profile lease, Kameo validates and resolves that file into the profile's local `endpoints.json`. Source changes affect future launches; no writes or deletions propagate to the shared source. This accommodates arbitrary filenames without changing Mux, which loads `endpoints.json` from its config directory. Links to managed Kameo profile files are rejected. An invalid or missing source fails the launch while preserving the previous snapshot. Clearing the link restores local setup/import behavior. Shared-file environment references resolve at launch time, while one-time imports resolve at import time.

## Desktop

The compact shell uses the supplied chameleon assets, a harness sidebar, a small selected-profile launch card, and profile actions. Sixteen palettes provide coordinated light/dark colors across custom content and Fluent control resources. Appearance selections are saved locally. UI text is English in this alpha.

The macOS application menu is registered during application initialization, before Avalonia installs its default menu. Window menus are attached to their respective windows, including editing commands in profile dialogs. A tray's `NativeMenu` is initialized before attaching the tray and is updated in place. Replacing an exported tray menu caused the initial macOS crash and is now covered by the native lifecycle smoke test.

## Extending harness support

Add a harness catalog entry, credential validation, import-location mapping, and isolated launch configuration. Update the database harness constraint through an explicit migration. Add synthetic contract tests for key/native modes, inheritance cleanup, argument handling, token persistence, and profile boundaries. The current implementation keeps four concrete contracts together rather than exposing speculative plugin interfaces.
