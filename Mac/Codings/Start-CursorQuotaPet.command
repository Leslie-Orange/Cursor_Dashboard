#!/bin/zsh
set -euo pipefail

CODING_DIR="${0:A:h}"
APP_DIR="$CODING_DIR/CursorQuotaPet.app"

if [[ ! -x "$APP_DIR/Contents/MacOS/CursorQuotaPet" ]]; then
  "$CODING_DIR/build-mac.sh"
fi

open "$APP_DIR"
