#!/bin/zsh
set -euo pipefail

ROOT_DIR="${0:A:h}/.."
SOURCE_FILE="$ROOT_DIR/macOS/CursorQuotaPet.swift"
INFO_FILE="$ROOT_DIR/macOS/Info.plist"
ICON_FILE="$ROOT_DIR/macOS/AppIcon.icns"
APP_DIR="$ROOT_DIR/CursorQuotaPet.app"
BUILD_DIR="$ROOT_DIR/macOS/.build"
MIN_OS="13.0"
UNIVERSAL="${UNIVERSAL:-0}"

if ! command -v swiftc >/dev/null 2>&1; then
  print -u2 "找不到 swiftc，请先安装 Xcode Command Line Tools。"
  exit 1
fi

mkdir -p "$BUILD_DIR" "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"

compile_one() {
  local arch="$1"
  local output="$2"
  swiftc -parse-as-library -O -target "${arch}-apple-macosx${MIN_OS}" "$SOURCE_FILE" -o "$output" \
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

if [[ -f "$ICON_FILE" ]]; then
  cp "$ICON_FILE" "$APP_DIR/Contents/Resources/AppIcon.icns"
fi

if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - "$APP_DIR" >/dev/null 2>&1 || true
fi

print "已生成：$APP_DIR"
if [[ "$UNIVERSAL" == "1" ]]; then
  lipo -info "$APP_DIR/Contents/MacOS/CursorQuotaPet" || true
fi
