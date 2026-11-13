#!/bin/sh
set -eu
repo=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
if [ ! -x "$repo/artifacts/cli/kameo" ]; then
  printf '%s\n' 'Build Kameo first with scripts/<platform>/update.sh.' >&2
  exit 1
fi
exec "$repo/artifacts/cli/kameo" "$@"
