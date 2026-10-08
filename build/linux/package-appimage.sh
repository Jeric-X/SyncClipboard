#!/usr/bin/env bash
set -euo pipefail
scripts=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
source_dir=$(realpath "$1")
cpu="$2"
output=$(realpath -m "$3")
case "$cpu:$(uname -m)" in
    x64:x86_64) arch=x86_64 ;;
    arm64:aarch64) arch=aarch64 ;;
    *) echo 'Build portable AppImages on a runner matching the target architecture.' >&2; exit 1 ;;
esac
test -f "$source_dir/libcoreclr.so"
mkdir -p "$output"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
appdir="$work/SyncClipboard.AppDir"
bin="$appdir/usr/bin"
mkdir -p "$bin" "$appdir/usr/share/applications" "$appdir/usr/lib"
cp -a "$source_dir/." "$bin/"
chmod +x "$bin/SyncClipboard.Desktop.Default" "$bin/SyncClipboard.Updater"
cp "$scripts/icons/icon.svg" "$appdir/xyz.jericx.desktop.syncclipboard.svg"
cp "$scripts/../../LICENSE" "$appdir/LICENSE"
desktop="$appdir/usr/share/applications/xyz.jericx.desktop.syncclipboard.desktop"
cat > "$desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=SyncClipboard
Icon=xyz.jericx.desktop.syncclipboard
Comment=A clipboard syncing tool
Exec=SyncClipboard.Desktop.Default
Terminal=false
Categories=Utility;
StartupWMClass=SyncClipboard.Desktop.Default
EOF
# The app's launcher integration replaces this absolute placeholder with the
# installed AppImage path. Keep its embedded template beside the managed files.
sed 's|^Exec=.*|Exec=/usr/bin/SyncClipboard.Desktop.Default|' "$desktop" > "$bin/xyz.jericx.desktop.syncclipboard.desktop"

for library in libicuuc.so.74 libicui18n.so.74 libicudata.so.74 libssl.so.3 libcrypto.so.3 \
    libfontconfig.so.1 libX11.so.6 libICE.so.6 libSM.so.6 libXrandr.so.2 libXi.so.6 \
    libXcursor.so.1 libXrender.so.1 libXext.so.6 libXfixes.so.3 libglib-2.0.so.0 libgssapi_krb5.so.2; do
    path=$(ldconfig -p | awk -v name="$library" '$1 == name && !found++ { print $NF }')
    test -n "$path"
    cp -pL "$path" "$appdir/usr/lib/"
done
# Retain the LTTng 2.13 provider; the legacy 2.12 provider needs a different ABI.
rm -f "$bin/libcoreclrtraceptprovider.so"
go-appimagetool -s deploy "$desktop"
bash "$scripts/prepare-portable-appdir.sh" "$appdir"

image="$output/SyncClipboard_linux_$cpu.AppImage"
args=()
if [[ "${APPIMAGE_UPDATES:-false}" == true ]]; then
    update_info="gh-releases-zsync|Jeric-X|SyncClipboard|latest|SyncClipboard_linux_$cpu.AppImage.zsync"
    args+=(-u "$update_info")
fi
ARCH="$arch" APPIMAGE_EXTRACT_AND_RUN=1 "appimagetool-$arch.AppImage" "${args[@]}" "$appdir" "$image"

if [[ "${APPIMAGE_UPDATES:-false}" == true ]]; then
    cd "$output"
    name=$(basename "$image")
    zsyncmake -u "$name" "$name"
    test -s "$name.zsync"
    readelf --string-dump=.upd_info "$name" | grep -F -- "$update_info"
    grep -aFx "Filename: $name" "$name.zsync"
    grep -aFx "URL: $name" "$name.zsync"
    checksum=$(sha1sum "$name" | cut -d ' ' -f 1)
    grep -aFx "SHA-1: $checksum" "$name.zsync"
fi
