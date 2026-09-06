#!/bin/zsh
set -euo pipefail

ROOT_DIR="${0:A:h}"
APP_DIR="$ROOT_DIR/CursorQuotaPet.app"

if [[ ! -x "$APP_DIR/Contents/MacOS/CursorQuotaPet" ]]; then
  "$ROOT_DIR/macOS/build-mac.sh"
fi

open "$APP_DIR"
