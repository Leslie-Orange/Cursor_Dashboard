#!/bin/zsh
set -euo pipefail

CODING_DIR="${0:A:h}"
MAC_DIR="${CODING_DIR:h}"

APP_DISPLAY_NAME="Cursor额度仪表盘"
VERSION="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$CODING_DIR/Info.plist" 2>/dev/null || echo 1.0.0)"
PACKAGES_DIR="$MAC_DIR/Packages"
STAGE_DIR="$PACKAGES_DIR/dmg-root"
SCRATCH_DIR="$CODING_DIR/.build/dmg-scratch"
DMG_NAME="${APP_DISPLAY_NAME}-${VERSION}.dmg"
RW_DMG="$PACKAGES_DIR/${APP_DISPLAY_NAME}-rw.dmg"
FINAL_DMG="$PACKAGES_DIR/$DMG_NAME"
VOLUME_NAME="$APP_DISPLAY_NAME"
SOURCE_APP="$CODING_DIR/CursorQuotaPet.app"
STAGED_APP="$STAGE_DIR/${APP_DISPLAY_NAME}.app"
ICON_FILE="$CODING_DIR/AppIcon.icns"

SOURCE_PNG="$CODING_DIR/icon/app-icon.png"
if [[ ! -f "$SOURCE_PNG" ]]; then
  print -u2 "找不到已确认的图标：$SOURCE_PNG"
  exit 1
fi

print "将应用图标封装为 icns…"
ICONSET="$CODING_DIR/AppIcon.iconset"
rm -rf "$ICONSET"
mkdir -p "$ICONSET"
resize_icon() {
  local pixels="$1"
  local name="$2"
  sips -z "$pixels" "$pixels" "$SOURCE_PNG" --out "$ICONSET/$name" >/dev/null
}
resize_icon 16 icon_16x16.png
resize_icon 32 icon_16x16@2x.png
resize_icon 32 icon_32x32.png
resize_icon 64 icon_32x32@2x.png
resize_icon 128 icon_128x128.png
resize_icon 256 icon_128x128@2x.png
resize_icon 256 icon_256x256.png
resize_icon 512 icon_256x256@2x.png
resize_icon 512 icon_512x512.png
resize_icon 1024 icon_512x512@2x.png
iconutil -c icns -o "$ICON_FILE" "$ICONSET"
rm -rf "$ICONSET"

print "编译通用应用…"
UNIVERSAL=1 "$CODING_DIR/build-mac.sh"

rm -rf "$STAGE_DIR" "$SCRATCH_DIR"
mkdir -p "$STAGE_DIR" "$SCRATCH_DIR"

ditto "$SOURCE_APP" "$STAGED_APP"
ln -s /Applications "$STAGE_DIR/Applications"
cp "$ICON_FILE" "$STAGE_DIR/.VolumeIcon.icns"

cat > "$STAGE_DIR/安装说明.txt" <<'EOF'
安装
1. 把「Cursor额度仪表盘」拖到旁边的「应用程序」文件夹。
2. 打开「启动台」或「应用程序」，启动「Cursor额度仪表盘」。
3. 这是菜单栏应用，启动后请看屏幕右上角状态栏，Dock 里不会常驻图标。

首次打开
若系统提示无法验证开发者：按住 Control 单击应用，选择「打开」，
或到「系统设置 → 隐私与安全性」中允许打开。

使用
保持 Cursor 已登录即可读取额度。左键看详情，右键可刷新或退出。
EOF

xattr -cr "$STAGED_APP" >/dev/null 2>&1 || true
if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - "$STAGED_APP" >/dev/null 2>&1 || true
fi

set_finder_icon() {
  local target="$1"
  local icon="$2"
  ICON_PATH="$icon" TARGET_PATH="$target" swift -e '
import AppKit
import Foundation
let icon = ProcessInfo.processInfo.environment["ICON_PATH"] ?? ""
let target = ProcessInfo.processInfo.environment["TARGET_PATH"] ?? ""
guard let image = NSImage(contentsOfFile: icon) else {
    FileHandle.standardError.write(Data("无法读取图标\n".utf8))
    exit(1)
}
let ok = NSWorkspace.shared.setIcon(image, forFile: target, options: [])
exit(ok ? 0 : 1)
'
}

set_finder_icon "$STAGED_APP" "$ICON_FILE"

rm -f "$RW_DMG" "$FINAL_DMG"
hdiutil create \
  -volname "$VOLUME_NAME" \
  -srcfolder "$STAGE_DIR" \
  -ov -fs HFS+ -format UDRW \
  "$RW_DMG" >/dev/null

MOUNT_DIR="$(hdiutil attach -readwrite -noverify -noautoopen "$RW_DMG" | awk '/\/Volumes\//{print $3; exit}')"
if [[ -z "${MOUNT_DIR:-}" || ! -d "$MOUNT_DIR" ]]; then
  print -u2 "挂载临时磁盘映像失败。"
  exit 1
fi

cleanup() {
  hdiutil detach "$MOUNT_DIR" -quiet >/dev/null 2>&1 || true
}
trap cleanup EXIT

osascript <<EOF >/dev/null
tell application "Finder"
  tell disk "$VOLUME_NAME"
    open
    set current view of container window to icon view
    set toolbar visible of container window to false
    set statusbar visible of container window to false
    set bounds of container window to {120, 120, 740, 480}
    set theViewOptions to the icon view options of container window
    set arrangement of theViewOptions to not arranged
    set icon size of theViewOptions to 96
    delay 0.4
    try
      set position of item "${APP_DISPLAY_NAME}.app" to {160, 180}
    end try
    try
      set position of item "Applications" to {460, 180}
    end try
    try
      set position of item "安装说明.txt" to {310, 340}
    end try
    update without registering applications
    delay 0.6
    close
  end tell
end tell
EOF

if command -v SetFile >/dev/null 2>&1; then
  SetFile -a C "$MOUNT_DIR" >/dev/null 2>&1 || true
  SetFile -a C "$MOUNT_DIR/.VolumeIcon.icns" >/dev/null 2>&1 || true
fi
set_finder_icon "$MOUNT_DIR" "$ICON_FILE" || true

sync
hdiutil detach "$MOUNT_DIR" -quiet
trap - EXIT

hdiutil convert "$RW_DMG" -format UDZO -imagekey zlib-level=9 -o "$FINAL_DMG" >/dev/null
rm -f "$RW_DMG"
rm -rf "$STAGE_DIR" "$SCRATCH_DIR"

# 先清构建机隔离属性，再写入 .dmg 文件自己的自定义图标（顺序不能反）。
xattr -c "$FINAL_DMG" >/dev/null 2>&1 || true
set_finder_icon "$FINAL_DMG" "$ICON_FILE"

print "已生成：$FINAL_DMG"
ls -lh "$FINAL_DMG"
