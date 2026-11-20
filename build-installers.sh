#!/bin/sh
# Builds every installer this host can produce (macOS: .pkg, .dmg, .tar.gz; Linux: .deb, .rpm, AppImage, .tar.gz)
# into installers/<version>/. Usage: ./build-installers.sh [version]   (default: the version in Directory.Build.props)
set -eu
repo=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$repo"
dotnet build src/Kameo.Publisher -c Release --nologo -v quiet
if [ "$#" -gt 0 ]; then
  dotnet run --project src/Kameo.Publisher -c Release --no-build -- --all --version "$1"
else
  dotnet run --project src/Kameo.Publisher -c Release --no-build -- --all
fi
