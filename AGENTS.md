# Kameo contributor instructions

Kameo is a local C#/.NET 10 desktop and CLI application. Source and tests belong under `src/`.

- Preserve version `0.1.0` unless the user explicitly requests a version change. Alpha is indicated in documentation and the UI.
- Follow the coding model in `~/Code/Agents/requirements` where applicable to a local desktop application. The user explicitly chose source builds without installers, C#/Avalonia, plaintext local credentials, and independent Windows/WSL installations.
- Namespace blocks come first; usings go inside. System usings first, then other namespaces, alphabetically.
- Use explicit types, nullable reference types, one class/enum per file, XML documentation for public APIs, and `_PascalCase` private fields. Avoid tuples and em dashes.
- Async operations accept cancellation tokens except framework-mandated entry points. Use `ConfigureAwait(false)` outside UI code.
- Never log credentials, raw credential documents, child environments, or command lines containing secrets. Tests use synthetic credentials only.
- Keep metadata in structured SQLite tables. Vendor credential JSON is intentionally opaque and stored in native profile files. Do not introduce serialized metadata blobs.
- Do not read or change the developer's actual harness credentials while testing. Use `KAMEO_HOME` pointing to a disposable private directory.
- All shared tests are Touchstone descriptors in `Test.Shared`. Execute them through `Test.Automated`, `Test.Xunit3`, `Test.Nunit`, and `Test.Aot`. `Test.Harness` is the synthetic child-process fixture.
- Keep native UI coverage in `Test.Desktop`: headless control-tree workflows plus a separate native startup/tray/shutdown smoke test.
- Run relevant tests after changes. The full suite runs on every push and pull request on Linux, Windows, and macOS.
- Keep the desktop small. No Quit button or filesystem-storage footer in the main window. Appearance opens a palette picker; preserve native menu and tray Quit actions.
- Build a tray's native menu before attaching it. Update the existing `NativeMenu` instance; replacing a macOS tray's exported menu caused a native startup crash.
- Lifecycle scripts live under `scripts/{windows,macos,linux,wsl,wsl2}`. `update` rebuilds source without pulling Git changes. Startup registration is per user, reversible, and must preserve unrelated shell settings.
- No external messages, publication, installers, or live provider login are required for normal development and testing.
