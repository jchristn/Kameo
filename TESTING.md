# Testing Kameo

Tests use Touchstone 1.0.0 and synthetic credentials. Every fixture owns a temporary directory and removes it after execution. No test reads the user's actual credential files or initiates provider authentication.

## Shared suites

- **Storage:** create/read/update/delete, reopen and persistence, redacted listing, duplicate and Unicode names, validation, independent harness selections, active-profile deletion, path traversal, leases, permissions, concurrent writers, preferences.
- **Credentials:** every native format, failed-import preservation, input bounds, malformed JSON redaction, unsupported shapes, profile isolation, Mux endpoint validation, file atomicity, links and broken links, scoped Keychain naming.
- **Launch:** every harness in API/native mode, stable per-profile paths, inherited key removal, literal argv boundaries, key rotation, native login commands, override rejection, executable resolution, real child environment and exit status, cancellation and lease release.
- **CLI:** real command execution, profile CRUD and selection, stdin keys, redacted listing and import failures, unknown commands, and child exit codes.
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

`Test.Desktop` uses Touchstone descriptors and Avalonia's headless dispatcher to exercise the actual control tree. It checks the empty state, compact layout constraints, native menu inventory, harness navigation, editor validation and creation, key masking/preservation, search, selection, delete confirmation, and palette persistence.

```sh
dotnet run --project src/Test.Desktop -- --skip fail --results artifacts/reports/desktop.json
```

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
