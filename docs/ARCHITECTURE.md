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

There is no HTTP server, remote account service, database server, or web dashboard. The backend reference's HTTP, multitenancy, Docker, REST/MCP, Postman, and four database-provider requirements do not apply to this local filesystem application. Installers and package-manager distribution are explicitly excluded by the product requirements. Source version is `0.1.0`, with alpha status in documentation and the UI.

## Storage and concurrency

`kameo.db` contains typed columns for profile identity, harness, authentication kind, API key, executable override, creation time, selected profile per harness, and non-secret preferences. Foreign keys prevent selecting a profile belonging to another harness. Case-insensitive unique names are scoped to each harness. PrettyId supplies stable prefixed K-sortable IDs.

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

### Gemini CLI

`GEMINI_CLI_HOME` is the parent of `.gemini`, not `.gemini` itself. Kameo explicitly chooses `gemini-api-key` or `oauth-personal` in profile-local settings. It supplies `GEMINI_API_KEY` for API profiles, sets `GEMINI_FORCE_FILE_STORAGE=true`, and sets `GEMINI_FORCE_ENCRYPTED_FILE_STORAGE=false` so OAuth uses the profile's `oauth_creds.json`. Native login starts the interactive Gemini CLI.

References: [configuration](https://geminicli.com/docs/reference/configuration/), [authentication](https://geminicli.com/docs/get-started/authentication/), and upstream [OAuth storage implementation](https://github.com/google-gemini/gemini-cli/blob/main/packages/core/src/code_assist/oauth2.ts). Kameo does not decrypt or export Gemini's OS-only or encrypted credential stores; import a native credential export or create a new isolated login.

### Mux

The reference is the local `~/Code/Mux` implementation: `SettingsLoader.GetConfigDirectory`, `EndpointConfig`, `docs/CONFIG.md`, and CLI dispatch. Kameo supplies `MUX_CONFIG_DIR`. Imported native profiles preserve `endpoints.json`, resolving API-key and header environment references at import time so the saved identity is independent of the importing shell. API profiles store a selected adapter, base URL, and model in that native file, with `${KAMEO_MUX_API_KEY}` resolved only in the child environment.

Mux has no standalone OAuth or `setup` CLI command. Starting its normal interactive TUI invokes the first-run wizard; setup remains available through its F1 command menu. Kameo does not submit the word `setup` as an agent prompt. Mux's current adapters determine which authentication methods are available.

## Desktop

The compact shell uses the supplied chameleon assets, a harness sidebar, a small selected-profile launch card, and profile actions. Sixteen palettes provide coordinated light/dark colors across custom content and Fluent control resources. Appearance selections are saved locally. UI text is English in this alpha.

The macOS application menu is registered during application initialization, before Avalonia installs its default menu. Window menus are attached to their respective windows, including editing commands in profile dialogs. A tray's `NativeMenu` is initialized before attaching the tray and is updated in place. Replacing an exported tray menu caused the initial macOS crash and is now covered by the native lifecycle smoke test.

## Extending harness support

Add a harness catalog entry, credential validation, import-location mapping, and isolated launch configuration. Update the database harness constraint through an explicit migration. Add synthetic contract tests for key/native modes, inheritance cleanup, argument handling, token persistence, and profile boundaries. The current implementation keeps four concrete contracts together rather than exposing speculative plugin interfaces.
