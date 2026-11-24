# Testing Kameo

Tests use Touchstone 1.0.0 and synthetic credentials. Every fixture owns a temporary directory and removes it after execution. No test reads the user's actual credential files or initiates provider authentication.

## Shared suites

- **Storage:** create/read/update/delete, reopen and persistence, redacted listing, duplicate and Unicode names, validation, independent harness selections, active-profile deletion, path traversal, leases, permissions, concurrent writers, preferences.
- **Credentials:** every native format, failed-import preservation, input bounds, malformed JSON redaction, unsupported shapes, profile isolation, Mux endpoint validation, file atomicity, links and broken links, scoped Keychain naming.
- **Launch:** every harness in API/native mode, stable per-profile paths, inherited key removal, literal argv boundaries, key rotation, native login commands, override rejection, executable resolution, real child environment and exit status, same-profile concurrent CLI sessions for every harness and credential type, joining a remaining session after another exits, preserved sibling sessions and leases on cancellation, exclusive login/import/edit/delete, and final lease release.
- **CLI:** real command execution, profile CRUD and selection, stdin keys, redacted listing and import failures, unknown commands, and child exit codes.
- **Shared Mux endpoints and migration:** persisted links, multiple profiles sharing a file, refresh on launch, missing/invalid sources, deletion preserving the source, lease enforcement, schema upgrades, and retained legacy Google login data.
- **Antigravity accounts:** API/account creation, isolated directories and provider settings, inherited-auth removal, SSH file-store selection, auto-update opt-out, token imports and validation, legacy-file preservation, symlink rejection, exact-version gating, failed/oversized/timed-out/canceled probes, lease release, concurrent refresh persistence, cancellation, relaunch, and deletion isolation. Compatibility aliases and configuration/updater overrides are covered.
- **Usage:** Claude windows, model limits, plan and request headers through an in-process HTTP handler; expired logins skip the network and are not modified; 401/403/429/500, malformed, non-object, oversized and network failures report safely without echoing response text; offline and signed-out states; API-key, Antigravity and Mux profiles are not applicable. Codex session logs pick the newest snapshot across files, ignore null and partial lines, zero windows that reset after the session, and anchor legacy relative resets. A synthetic `Test.Harness` app-server verifies isolated arguments, `CODEX_HOME`, working directory, protocol order, interleaved notifications, multi-bucket results, error and early-exit fallback to logs, timeout termination with lease release, missing executables, and exclusive-lease blocking. Formatting, harness filtering and ordering, and the `kameo usage` text, JSON, single-profile and error paths are covered. No provider is contacted.
- **Publisher:** the committed `publisher.json` (identifiers, six-runtime matrix, signing off, existing paths, square logo), manifest validation, architecture and file-name conventions, the expected 19-file release set, host channel order, stable Windows AppId, Inno Setup script (PATH, license, arm64, tray startup, CRLF), macOS Info.plist, distribution, LaunchAgent, postinstall/uninstall scripts and disk-image note, Linux desktop entries, fpm arguments and staging layout, changelog extraction, release-note composition, tag verification, PNG/ICO/ICNS/BMP encoding, artwork generation, portable archive contents and modes, and `SHA256SUMS`. Native packagers are not invoked; the release workflow exercises them on each OS.
- **Lifecycle:** startup registration round trips for all five platform directories, existing shell configuration preservation, idempotence, desktop singleton leases, and graceful stop handshake.

Run all shared descriptors through each required host:

```sh
dotnet build -c Release
mkdir -p artifacts/reports
dotnet run --project src/Test.Automated -c Release --no-build -- --skip fail --results artifacts/reports/core.json --html artifacts/reports/core.html
dotnet test src/Test.Xunit3 -c Release --no-build
dotnet test src/Test.Nunit -c Release --no-build
dotnet publish src/Test.Aot -c Release -r osx-arm64 -o artifacts/aot
./artifacts/aot/Test.Aot --skip fail --results artifacts/reports/aot.json
```

Adjust the runtime identifier and executable extension for your machine. `Test.Harness` is built as a dependency; when running published tests outside the checkout, set `KAMEO_TEST_PROBE` to the absolute path of its executable and `KAMEO_TEST_CLI` to the CLI executable.

## Desktop workflows

`Test.Desktop` uses Touchstone descriptors and Avalonia's headless dispatcher to exercise the actual control tree. It checks the empty state, compact layout constraints, native menu inventory, harness navigation, editor validation and creation, key masking/preservation, search, selection, delete confirmation, and palette persistence. Launch-in workflows inject a folder picker and terminal launcher to verify the selected profile and directory, cancellation, missing folders, picker/terminal failures, and preservation of the saved launch folder without invoking a real harness. Antigravity workflows cover API/account choices, account creation and status, immutable authentication kind during editing, and the profile and login mode passed by Sign in, Launch, and Launch in... actions. A per-profile launch workflow verifies that each card launches its own profile in the saved or chosen folder without changing the selection, and that the empty state offers no launch. A usage workflow injects a usage reader to verify that **Check usage** appears only for harnesses with native logins, checks each native login once, renders bars, percentages, reset text, plan summaries and failures per card, survives refresh, and that **View > Check Usage** explains unsupported harnesses.

```sh
dotnet run --project src/Test.Desktop -- --skip fail --results artifacts/reports/desktop.json
```

Additional desktop cases verify search/card alignment with scrolling and resizing, native submenu launches after selection changes and deletion, About content, a centered Close button, and contrasting logos across all 16 palettes in both modes, the tray menu (About, Launch, Launch in..., selection, in-place updates, and Quit), and two-second splash dismissal without closing the manager or About dialog.

Headless tests cannot prove platform tray integration. A separate native smoke test starts a real desktop against a temporary store, verifies it stays alive, changes profile state to exercise native tray updates, deletes a profile, and requests graceful shutdown:

```sh
dotnet run --project src/Test.Desktop -- --native-smoke artifacts/desktop/Kameo.Desktop
```

Use `.exe` on Windows. Linux CI uses `xvfb-run`. Native testing does not start any real agent harness. A failure or crash returns nonzero.

## Manual acceptance

Use an alternate `KAMEO_HOME` with synthetic keys to check layout and interaction. Exercise dark/light palettes, long profile names, narrow windows, keyboard navigation, search, modal save/cancel, native menus, tray selection, close/reopen, and source lifecycle scripts. Do not run synthetic credentials against a provider.

Real OAuth acceptance requires a user-controlled account and explicit sign-in. For each installed harness, create two profiles, authenticate through the harness, launch each through Kameo, check the account shown by the harness, allow a refresh, and relaunch. Verify that the default installation's login remains unchanged. Kameo's automated suite establishes local isolation and process contracts; it does not certify provider-side login acceptance or future credential format changes.

## Regression findings addressed

- **Native macOS startup crash:** attaching a replacement tray menu caused Avalonia's native exporter to reject its menu identity. The menu is now built before attachment and updated in place. Native smoke covers both initial creation and later changes.
- **Missing native application menus:** application and window menus are registered at the correct lifetime stages. Menu inventory is checked headlessly and was inspected in the running macOS application.
- **Oversized manager:** the shell is now 800 by 550 by default, with reduced spacing, no Quit button or storage footer, and a dedicated 16-palette appearance picker. A layout contract guards those requirements.

GitHub Actions runs all shared hosts, desktop workflows, native smoke, native AOT, and script syntax validation on every push to every branch and every pull request. CI reports are uploaded even if a job fails. Windows and Linux outcomes must be confirmed by those runners; a local macOS pass is not a claim that those operating systems were exercised locally.
