#!/usr/bin/env bash

set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_BUNDLE="${1:-$SCRIPT_DIR/SyncClipboard.app}"
INFO_PLIST="$SCRIPT_DIR/Info.plist"
ICON_FILE="$SCRIPT_DIR/icon.icns"

[[ -d "$APP_BUNDLE/Contents" ]] || {
    echo "错误: 无效的 app bundle: $APP_BUNDLE" >&2
    exit 1
}
[[ -f "$INFO_PLIST" ]] || {
    echo "错误: 找不到 Info.plist: $INFO_PLIST" >&2
    exit 1
}
[[ -f "$ICON_FILE" ]] || {
    echo "错误: 找不到应用图标: $ICON_FILE" >&2
    exit 1
}

mkdir -p "$APP_BUNDLE/Contents/Resources"
cp -f "$INFO_PLIST" "$APP_BUNDLE/Contents/Info.plist"
cp -f "$ICON_FILE" "$APP_BUNDLE/Contents/Resources/icon.icns"
plutil -lint "$APP_BUNDLE/Contents/Info.plist"
