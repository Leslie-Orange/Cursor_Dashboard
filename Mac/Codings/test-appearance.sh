#!/bin/zsh
set -euo pipefail

CODING_DIR="${0:A:h}"
SOURCE_FILE="$CODING_DIR/CursorQuotaPet.swift"
BRAND_SOURCE="$CODING_DIR/CursorBrand.swift"
APPEARANCE_SOURCE="$CODING_DIR/QuotaAppearance.swift"
TEST_SOURCE="$CODING_DIR/Tests/AppearanceTests.swift"
TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/cursor-appearance-tests.XXXXXX")"
trap 'rm -rf "$TEMP_DIR"' EXIT INT TERM

for source in "$SOURCE_FILE" "$BRAND_SOURCE" "$APPEARANCE_SOURCE" "$TEST_SOURCE"; do
  if [[ ! -f "$source" ]]; then
    print -u2 "缺少外观测试源码：$source"
    exit 1
  fi
done

python3 - "$SOURCE_FILE" "$TEMP_DIR/CursorQuotaPet.swift" <<'PY'
from pathlib import Path
import sys

source = Path(sys.argv[1]).read_text()
marker = "@main\nstruct CursorQuotaPetMain"
if source.count(marker) != 1:
    raise SystemExit("预期在 CursorQuotaPet.swift 中找到唯一的 @main\nstruct CursorQuotaPetMain 分隔标记")
Path(sys.argv[2]).write_text(source.split(marker, 1)[0])
PY

swiftc -parse-as-library \
  -module-cache-path "$TEMP_DIR/ModuleCache" \
  "$TEMP_DIR/CursorQuotaPet.swift" \
  "$BRAND_SOURCE" \
  "$APPEARANCE_SOURCE" \
  "$TEST_SOURCE" \
  -o "$TEMP_DIR/test-appearance" \
  -framework AppKit -framework Combine -framework SwiftUI -framework CryptoKit -lsqlite3

# Run the offline fixture harness without inheriting an explicit session token.
unset CURSOR_SESSION_TOKEN
"$TEMP_DIR/test-appearance"
