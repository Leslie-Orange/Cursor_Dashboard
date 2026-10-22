#!/bin/zsh
set -euo pipefail

CODING_DIR="${0:A:h}"
SOURCE_FILE="$CODING_DIR/CursorQuotaPet.swift"
BRAND_SOURCE="$CODING_DIR/CursorBrand.swift"
APPEARANCE_SOURCE="$CODING_DIR/QuotaAppearance.swift"
ICON_SOURCE="$CODING_DIR/MakeAppIcon.swift"
INFO_FILE="$CODING_DIR/Info.plist"
ICON_FILE="$CODING_DIR/AppIcon.icns"
ICON_PNG="$CODING_DIR/icon/app-icon.png"
ICON_PREVIEWS="$CODING_DIR/icon"
APP_DIR="$CODING_DIR/CursorQuotaPet.app"
BUILD_DIR="$CODING_DIR/.build"
ICON_TOOL="$BUILD_DIR/MakeAppIcon"
MIN_OS="13.0"
UNIVERSAL="${UNIVERSAL:-0}"

if ! command -v swiftc >/dev/null 2>&1; then
  print -u2 "找不到 swiftc，请先安装 Xcode Command Line Tools。"
  exit 1
fi

mkdir -p "$BUILD_DIR" "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"

for source in "$BRAND_SOURCE" "$ICON_SOURCE" "$APPEARANCE_SOURCE" "$SOURCE_FILE"; do
  if [[ ! -f "$source" ]]; then
    print -u2 "缺少构建源码：$source"
    exit 1
  fi
done

print "生成共享矢量应用图标…"
swiftc -parse-as-library -O -module-cache-path "$BUILD_DIR/ModuleCache" \
  "$ICON_SOURCE" "$BRAND_SOURCE" -o "$ICON_TOOL" -framework AppKit
"$ICON_TOOL" "$ICON_PNG" "$ICON_FILE" "$ICON_PREVIEWS"

compile_one() {
  local arch="$1"
  local output="$2"
  swiftc -parse-as-library -O -module-cache-path "$BUILD_DIR/ModuleCache" \
    -target "${arch}-apple-macosx${MIN_OS}" \
    "$SOURCE_FILE" "$BRAND_SOURCE" "$APPEARANCE_SOURCE" -o "$output" \
    -framework AppKit -framework Combine -framework SwiftUI -lsqlite3
}

if [[ "$UNIVERSAL" == "1" ]]; then
  compile_one arm64 "$BUILD_DIR/CursorQuotaPet-arm64"
  compile_one x86_64 "$BUILD_DIR/CursorQuotaPet-x86_64"
  lipo -create -output "$BUILD_DIR/CursorQuotaPet" \
    "$BUILD_DIR/CursorQuotaPet-arm64" "$BUILD_DIR/CursorQuotaPet-x86_64"
else
  compile_one "$(uname -m)" "$BUILD_DIR/CursorQuotaPet"
fi

cp "$BUILD_DIR/CursorQuotaPet" "$APP_DIR/Contents/MacOS/CursorQuotaPet"
chmod +x "$APP_DIR/Contents/MacOS/CursorQuotaPet"
cp "$INFO_FILE" "$APP_DIR/Contents/Info.plist"
cp "$ICON_FILE" "$APP_DIR/Contents/Resources/AppIcon.icns"

if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - "$APP_DIR" >/dev/null 2>&1 || true
fi

print "已生成：$APP_DIR"
if [[ "$UNIVERSAL" == "1" ]]; then
  lipo -info "$APP_DIR/Contents/MacOS/CursorQuotaPet" || true
fi
