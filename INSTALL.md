# Installing Kameo

Kameo ships installers for Windows, macOS, and Linux on x64 and ARM64. Download them from the
[Releases page](https://github.com/jchristn/Kameo/releases/latest). Every 0.x release is an **alpha**. Each installer includes both the desktop and tray app and the `kameo` command-line tool. Neither
needs a separate .NET runtime. To build from source instead, see
[Install from source](README.md#install-from-source).

> [!IMPORTANT]
> **These builds are not code-signed, so Windows and macOS block the first launch.** This is expected and does not
> mean the download is damaged. The steps to allow Kameo are below. To confirm a file is the one published, check it
> against `SHA256SUMS`.

Kameo is free, open-source software and is not code-signed (signing certificates are an ongoing cost we choose not to
pass on). Your operating system may warn that the developer is unidentified. That warning doesn't mean the download is
damaged; to confirm a file is the one published, check it against `SHA256SUMS`.

Kameo does not install the agent harnesses (Claude Code, Codex, Antigravity, Mux). Install those separately and keep
their commands on `PATH`, or set an executable path in each profile.

## Which file to download

Replace `<version>` with the release version, for example `0.2.1`.

| Platform | File | What it installs |
| --- | --- | --- |
| Windows (x64) | `kameo-<version>-win-x64-setup.exe` | Kameo in Program Files, `kameo` on `PATH`, Start menu entry, optional tray at sign-in |
| Windows (ARM64) | `kameo-<version>-win-arm64-setup.exe` | Same, for Windows on ARM |
| macOS (Apple Silicon) | `kameo-<version>-osx-arm64.pkg` (recommended) or `.dmg` | `/Applications/Kameo.app`; the `.pkg` also adds `/usr/local/bin/kameo` and starts Kameo at login |
| macOS (Intel) | `kameo-<version>-osx-x64.pkg` or `.dmg` | Same, for Intel Macs |
| Debian, Ubuntu | `kameo_<version>_amd64.deb` or `_arm64.deb` | `/opt/kameo`, `/usr/bin/kameo`, menu entry, login autostart |
| Fedora, RHEL, openSUSE | `kameo-<version>-1.x86_64.rpm` or `.aarch64.rpm` | Same as the `.deb` |
| Any Linux | `kameo-<version>-x86_64.AppImage` or `-aarch64.AppImage` | One self-contained file; nothing is installed |
| Scripts and containers | `kameo-<version>-<rid>.tar.gz`, `kameo-<version>-win-<arch>.zip` | The bare `kameo` and `Kameo.Desktop` executables |

Not sure which Mac you have? Apple menu > **About This Mac**: "Chip: Apple M..." is Apple Silicon, "Processor:
Intel" is Intel.

Package-manager installs (`winget`, Homebrew, `apt` and `dnf` repositories) are planned but not available yet.

## Windows

1. Download the setup `.exe`. If your browser warns that the file *isn't commonly downloaded*, choose **Keep**. In
   Edge, open the **...** menu on the download, choose **Keep**, then **Show more > Keep anyway**.
2. Run it. Windows shows a blue dialog:

   > **Windows protected your PC**
   > Microsoft Defender SmartScreen prevented an unrecognized app from starting.

   Click **More info**, then **Run anyway**. The publisher shows as Unknown publisher.
3. Accept the license, choose whether Kameo should start in the tray when you sign in, and finish. The installer asks
   for administrator approval because it installs to Program Files and adds Kameo to the system `PATH`.
4. Open a **new** terminal and run `kameo --version`. Start the desktop from the Start menu entry **Kameo**.

The startup option adds a `Kameo Agent` entry under **Settings > Apps > Startup**, where you can turn it off.

**Portable `.zip`:** unblock the archive before extracting it. Otherwise Windows copies the download mark onto
every extracted file, so `kameo.exe` triggers SmartScreen or fails silently in a terminal:

```powershell
Unblock-File .\kameo-<version>-win-x64.zip
```

Or right-click the `.zip`, choose **Properties**, and select **Unblock**. Then extract it and run `kameo.exe` or
`Kameo.Desktop.exe` from the extracted folder.

If Microsoft Defender (not SmartScreen) quarantines a file as malware, that is a false positive that self-extracting
single-file executables sometimes trigger. Report it at <https://www.microsoft.com/en-us/wdsi/filesubmission>. Do not
turn Defender off.

**Uninstall:** quit Kameo from its tray menu, then open **Settings > Apps > Installed apps**, find **Kameo**, and
choose **Uninstall**. This removes the program, its `PATH` entry, and its startup entry. Your profiles in
`%USERPROFILE%\.kameo` are kept.

**Unattended install:** `kameo-<version>-win-x64-setup.exe /VERYSILENT /SUPPRESSMSGBOXES /TASKS="startup"` (omit
`startup` to skip sign-in startup).

## macOS

The `.pkg` is recommended because it also adds the `kameo` command. A `.dmg` can only copy the app.

1. Download the `.pkg` for your Mac and open it. macOS 15 (Sequoia) and later show:

   > **"Kameo" Not Opened**
   > Apple could not verify "Kameo" is free of malware that may harm your Mac or compromise your privacy.

   Older versions say *"Kameo" cannot be opened because it is from an unidentified developer*. The dialog may name
   the `.pkg` file instead of Kameo.
2. Click **Done**. Do not click Move to Trash.
3. Open **System Settings > Privacy & Security** and scroll down to **Security**.
4. Next to *"Kameo" was blocked to protect your Mac*, click **Open Anyway**. The button only appears for about an
   hour after the blocked attempt; if it is missing, try to open Kameo again first.
5. Click **Open Anyway** again in the dialog that follows and enter your password. You only do this once.
6. Follow the installer: read the welcome page, accept the license, and install.

On macOS 15 and later, Control-click > **Open** no longer bypasses this prompt, despite what older guides say.

Terminal users can skip the prompt entirely:

```sh
xattr -dr com.apple.quarantine ~/Downloads/kameo-<version>-osx-arm64.pkg   # the .pkg, before opening it
sudo installer -pkg ~/Downloads/kameo-<version>-osx-arm64.pkg -target /    # installs the .pkg with no prompt at all
```

The `.pkg` installs `/Applications/Kameo.app`, creates `/usr/local/bin/kameo`, and adds a LaunchAgent
(`ai.latticetech.kameo.agent`) that starts Kameo in the menu bar when you log in. It also starts Kameo right away.
Open a new terminal and run `kameo --version`.

**Disk image (`.dmg`):** open it and drag **Kameo** onto **Applications**. The image also contains
`If Kameo won't open.txt`, which repeats the steps above. To clear the prompt from Terminal after copying the app:

```sh
xattr -dr com.apple.quarantine /Applications/Kameo.app
```

The disk image does not add `kameo` to `PATH` or register login startup. To add the command:
`sudo mkdir -p /usr/local/bin && sudo ln -sf /Applications/Kameo.app/Contents/MacOS/kameo /usr/local/bin/kameo`.

**Portable `.tar.gz`:** a file downloaded with `curl` gets no quarantine attribute and no prompt. A browser download
needs `xattr -d com.apple.quarantine ./kameo` (and `./Kameo.Desktop`) after extracting.

**Uninstall:** quit Kameo from the menu bar, then run:

```sh
sudo /Applications/Kameo.app/Contents/Resources/uninstall.sh
```

This removes the app, `/usr/local/bin/kameo`, the LaunchAgent, and the installer receipt. If you installed from the
`.dmg`, quit Kameo and move `Kameo.app` to the Trash instead. Your profiles in `~/.kameo` are kept. Delete Claude
profiles in Kameo before uninstalling if you also want their scoped Keychain entries removed.

## Linux

Linux shows no security prompt for these packages.

```sh
sudo apt install ./kameo_<version>_amd64.deb          # Debian, Ubuntu; keep the ./ or apt searches its repositories
sudo dnf install ./kameo-<version>-1.x86_64.rpm       # Fedora, RHEL
```

Use `apt install` rather than `dpkg -i`, because only apt installs the declared dependencies (ICU, fontconfig, and
the X11 client libraries). Then run `kameo --version` and open **Kameo** from the application menu.

The packages install the executables under `/opt/kameo`, link `/usr/bin/kameo`, add a menu entry and icons, and add
`/etc/xdg/autostart/ai.latticetech.kameo.desktop`, which starts Kameo when any user signs in to a graphical session.
To turn that off for your account, disable Kameo in your desktop's startup-applications settings, or run:

```sh
mkdir -p ~/.config/autostart && printf '[Desktop Entry]\nHidden=true\n' > ~/.config/autostart/ai.latticetech.kameo.desktop
```

Tray support depends on the desktop's StatusNotifier/AppIndicator support. Without a tray host Kameo shows its
manager window, and closing that window exits Kameo.

**AppImage:** `chmod +x kameo-<version>-x86_64.AppImage`, then run it to open the desktop. Run
`./kameo-<version>-x86_64.AppImage --cli list` to use the command-line tool from inside the image. An AppImage does not
put `kameo` on `PATH`. Older distributions need `libfuse2` to mount it.

**Portable `.tar.gz`:** `curl -fsSL <url> | tar xz` gives the bare `kameo` and `Kameo.Desktop` executables.

**WSL:** install the `.deb` or `.rpm` inside the distribution. The CLI works headless; the desktop needs WSLg or an X
server. Windows and WSL installations are independent and keep separate profiles.

**Uninstall:** `sudo apt remove kameo` or `sudo dnf remove kameo`. Your profiles in `~/.kameo` are kept.

## Verify a download

Every release includes `SHA256SUMS`, covering every file on the release page. Put it in the same folder as your
download, then run:

```sh
shasum -a 256 -c SHA256SUMS --ignore-missing          # macOS
sha256sum -c SHA256SUMS --ignore-missing              # Linux
```

```powershell
(Get-FileHash .\kameo-<version>-win-x64-setup.exe -Algorithm SHA256).Hash.ToLower()   # compare with SHA256SUMS
```

## Moving from a source build

If you registered a source build for automatic startup, remove that registration first so Kameo does not start twice
at login: run `scripts\windows\remove-from-startup.bat` or `./scripts/<platform>/remove-from-startup.sh` from your
clone. Installed and source-built copies share the same profiles in `~/.kameo`. If you installed the CLI as a .NET
global tool, run `dotnet tool uninstall --global Kameo.Cli` so the installed `kameo` is the one on `PATH`.

## Your data

Installers never create, read, or delete your profiles. Kameo stores them in plaintext under `~/.kameo`
(`%USERPROFILE%\.kameo` on Windows), or in `KAMEO_HOME` when set. To erase them, delete profiles in Kameo, quit it, and
remove that folder. See [Local data](README.md#local-data).

## Building the installers

Maintainers build installers with `Kameo.Publisher`, which reads [`publisher.json`](publisher.json).
`./build-installers.sh` (macOS or Linux) or `build-installers.bat` (Windows) builds the formats the current OS can
produce into `installers/<version>/`. Pushing a `v<version>` tag builds every format on GitHub-hosted Windows, macOS,
and Linux runners and publishes one release with all files and `SHA256SUMS`. See [RELEASING.md](RELEASING.md).
