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
mkdir -p "$bin" "$appdir/usr/share/applications" "$appdir/usr/share/metainfo" "$appdir/usr/lib"
cp -a "$source_dir/." "$bin/"
chmod +x "$bin/SyncClipboard.Desktop.Default" "$bin/SyncClipboard.Updater"
cp "$scripts/../icons/icon.svg" "$appdir/xyz.jericx.desktop.syncclipboard.svg"
cp "$scripts/../../../LICENSE" "$appdir/LICENSE"
cp "$scripts/../metainfo.xml" "$appdir/usr/share/metainfo/xyz.jericx.desktop.syncclipboard.metainfo.xml"
desktop="$appdir/usr/share/applications/xyz.jericx.desktop.syncclipboard.desktop"
sed -e 's|${APP_FRIENDLY_NAME}|SyncClipboard|g' \
    -e 's|${APP_ID}|xyz.jericx.desktop.syncclipboard|g' \
    -e 's|${APP_SHORT_SUMMARY}|A clipboard syncing tool|g' \
    -e 's|${INSTALL_EXEC}|SyncClipboard.Desktop.Default|g' \
    -e 's|${DESKTOP_NODISPLAY}|false|g' \
    -e 's|${DESKTOP_INTEGRATE}|true|g' \
    -e 's|${DESKTOP_TERMINAL}|false|g' \
    -e 's|${PRIME_CATEGORY}|Utility;|g' \
    -e 's|${APP_BASE_NAME}|SyncClipboard.Desktop.Default|g' \
    "$scripts/../app.desktop" > "$desktop"
# The app's launcher integration replaces this absolute placeholder with the
# installed AppImage path. Keep its embedded template beside the managed files.
sed -E 's#^(Exec|TryExec)=.*#\1=/usr/bin/SyncClipboard.Desktop.Default#' \
    "$desktop" > "$bin/xyz.jericx.desktop.syncclipboard.desktop"

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

# Exercise only the generated updater payload, away from the main AppDir.
# Trace loaded libraries so host libraries cannot hide an incomplete manifest.
updater="$work/updater"
mkdir "$updater"
updater=$(realpath "$updater")
while IFS= read -r name; do
    cp -p "$bin/$name" "$updater/$name"
done < "$bin/appimage-updater.files"
(
    cd "$updater"
    xvfb-run -a timeout 30s env -u APPDIR -u APPIMAGE -u ARGV0 -u OWD \
        -u LD_LIBRARY_PATH -u LD_PRELOAD -u LD_AUDIT \
        LANG=C.UTF-8 LD_DEBUG=files LD_DEBUG_OUTPUT="$work/updater-loader" \
        ./SyncClipboard.Updater --smoke-test
) | tee "$work/updater-smoke.log"
grep -Fxq 'GUI_SMOKE=PASS' "$work/updater-smoke.log"
for trace in "$work"/updater-loader.*; do
    while IFS= read -r line; do
        case "$line" in
            *'calling init: '*)
                library="${line#*calling init: }"
                library=$(cd "$updater" && realpath -- "$library")
                if [[ "$library" != "$updater/"* ]]; then
                    echo "Updater loaded a library outside its workspace: $library" >&2
                    exit 1
                fi
                ;;
        esac
    done < "$trace"
done

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
