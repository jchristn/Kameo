## Installing

> [!IMPORTANT]
> **These builds are not code-signed, so Windows and macOS block the first launch.** This is expected and does not
> mean the download is damaged. The steps to allow Kameo are below. To confirm a file is the one published here,
> check it against `SHA256SUMS`.

Kameo is free, open-source software and is not code-signed (signing certificates are an ongoing cost we choose not to
pass on). Kameo {version} is an **alpha** release.

### macOS: "Kameo" Not Opened

macOS shows *"Kameo" Not Opened. Apple could not verify "Kameo" is free of malware that may harm your Mac or
compromise your privacy.* with **Move to Trash** and **Done** buttons. To open it anyway:

1. Click **Done**. Do not click Move to Trash.
2. Open **System Settings > Privacy & Security** and scroll down to **Security**.
3. Next to *"Kameo" was blocked to protect your Mac*, click **Open Anyway**. The button only appears for about an
   hour after the blocked attempt; if it is missing, try to open Kameo again first.
4. Click **Open Anyway** again in the dialog that follows and enter your password. You only do this once.

Or skip the prompt in Terminal:

```bash
xattr -dr com.apple.quarantine ~/Downloads/kameo-{version}-osx-arm64.pkg   # before opening the .pkg
xattr -dr com.apple.quarantine /Applications/Kameo.app                     # after copying the app from the .dmg
sudo installer -pkg ~/Downloads/kameo-{version}-osx-arm64.pkg -target /    # installs the .pkg with no prompt at all
```

On macOS 15 and later, Control-click > **Open** no longer bypasses this prompt.

### Windows: "Windows protected your PC"

Click **More info**, then **Run anyway**. If your browser warns that the file *isn't commonly downloaded*, choose
**Keep** (in Edge: the **...** menu on the download, **Keep**, then **Show more > Keep anyway**). For the `.zip`, run
`Unblock-File` on it (or right-click > Properties > Unblock) before extracting.

### Linux

No prompt. `sudo apt install ./kameo_{version}_amd64.deb`, `sudo dnf install ./kameo-{version}-1.x86_64.rpm`, or
`chmod +x` the AppImage and run it. Keep the `./` in the apt command, or apt searches its repositories instead.

### Which file to download

| Platform | File |
| --- | --- |
| Windows (x64) | `kameo-{version}-win-x64-setup.exe` |
| Windows (ARM64) | `kameo-{version}-win-arm64-setup.exe` |
| macOS (Apple Silicon) | `kameo-{version}-osx-arm64.pkg` (or `.dmg`) |
| macOS (Intel) | `kameo-{version}-osx-x64.pkg` (or `.dmg`) |
| Debian / Ubuntu | `kameo_{version}_amd64.deb` (or `_arm64.deb`) |
| Fedora / RHEL | `kameo-{version}-1.x86_64.rpm` (or `.aarch64.rpm`) |
| Any Linux | `kameo-{version}-x86_64.AppImage` (or `-aarch64.AppImage`) |
| Scripts and containers | `kameo-{version}-<rid>.tar.gz` / `kameo-{version}-win-<arch>.zip` |

Not sure which Mac you have? Apple menu > **About This Mac**: "Chip: Apple M..." is Apple Silicon, "Processor:
Intel" is Intel.

The Windows installer and the macOS `.pkg` add the `kameo` command to `PATH` and start Kameo in the tray when you sign
in (on Windows this is a checkbox). The `.deb` and `.rpm` add `/usr/bin/kameo`, a menu entry, and a login autostart
entry. Your profiles stay in `~/.kameo` and are never removed by an uninstall.

Full instructions, including uninstalling: [INSTALL.md](https://github.com/jchristn/Kameo/blob/main/INSTALL.md). To
build from source, use the **Source code** archives below and follow
[Install from source](https://github.com/jchristn/Kameo#install-from-source).
