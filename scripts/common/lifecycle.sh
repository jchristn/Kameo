#!/bin/sh
set -eu
action=${1:?Missing action}
platform=${2:?Missing platform}
shift 2
repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
cli="$repo/artifacts/cli/kameo"
desktop="$repo/artifacts/desktop/Kameo.Desktop"
if [ "$platform" = macos ]; then desktop="$repo/artifacts/Kameo.app/Contents/MacOS/Kameo.Desktop"; fi

case "$action" in
  update)
    restart=false
    if [ -x "$cli" ]; then
      if "$cli" desktop-status; then restart=true; fi
      "$cli" desktop-stop
    fi
    case "$(uname -s):$(uname -m)" in
      Darwin:arm64) rid=osx-arm64 ;; Darwin:x86_64) rid=osx-x64 ;;
      Linux:aarch64|Linux:arm64) rid=linux-arm64 ;; Linux:x86_64) rid=linux-x64 ;;
      *) printf '%s\n' 'Unsupported architecture. Build with dotnet publish and an explicit runtime identifier.' >&2; exit 1 ;;
    esac
    rm -rf "$repo/artifacts/cli" "$repo/artifacts/desktop"
    dotnet publish "$repo/src/Kameo.Cli" -c Release -r "$rid" --self-contained false -o "$repo/artifacts/cli"
    dotnet publish "$repo/src/Kameo.Desktop" -c Release -r "$rid" --self-contained false -o "$repo/artifacts/desktop"
    if [ "$platform" = macos ]; then
      rm -rf "$repo/artifacts/Kameo.app"
      mkdir -p "$repo/artifacts/Kameo.app/Contents/MacOS" "$repo/artifacts/Kameo.app/Contents/Resources"
      cp -R "$repo/artifacts/desktop/." "$repo/artifacts/Kameo.app/Contents/MacOS/"
      cp "$repo/assets/Info.plist" "$repo/artifacts/Kameo.app/Contents/Info.plist"
      icons="$repo/artifacts/Kameo.iconset"
      mkdir -p "$icons"
      for size in 16 32 128 256 512; do
        sips -z "$size" "$size" "$repo/assets/logo-dark.png" --out "$icons/icon_${size}x${size}.png" >/dev/null
        double=$((size * 2))
        sips -z "$double" "$double" "$repo/assets/logo-dark.png" --out "$icons/icon_${size}x${size}@2x.png" >/dev/null
      done
      iconutil -c icns "$icons" -o "$repo/artifacts/Kameo.app/Contents/Resources/Kameo.icns"
      codesign --force --deep --sign - "$repo/artifacts/Kameo.app"
    fi
    if [ "$restart" = true ]; then "$0" start "$platform" --tray; fi
    ;;
  start)
    if [ ! -x "$desktop" ] || [ ! -x "$cli" ]; then "$0" update "$platform"; fi
    if [ "$platform" != macos ] && [ -z "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]; then
      printf '%s\n' 'A graphical display is required. Use scripts/common/kameo.sh for headless CLI access.' >&2
      exit 1
    fi
    nohup "$desktop" "$@" >/dev/null 2>&1 &
    ;;
  stop)
    if [ -x "$cli" ]; then "$cli" desktop-stop; fi
    ;;
  run-at-startup)
    if [ ! -x "$desktop" ] || [ ! -x "$cli" ]; then "$0" update "$platform"; fi
    "$cli" startup "$platform" "$desktop"
    printf '%s\n' 'Kameo will start at your next login (WSL: next interactive shell with a display).'
    ;;
  remove-from-startup)
    if [ ! -x "$cli" ]; then "$0" update "$platform"; fi
    "$cli" remove-startup "$platform"
    printf '%s\n' 'Kameo startup registration removed.'
    ;;
  *) printf '%s\n' 'Unknown lifecycle action.' >&2; exit 2 ;;
esac
