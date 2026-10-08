#!/usr/bin/env bash
set -euo pipefail
appdir=$(realpath "$1")
bin="$appdir/usr/bin"
loader=$(patchelf --print-interpreter "$bin/SyncClipboard.Desktop.Default")
loader_name=appimage-ld.so

# Libraries must also resolve their transitive dependencies beside themselves
# after being copied to the standalone updater workspace. Normalize before
# copying so SquashFS can still deduplicate the original and flat library copies.
while IFS= read -r -d '' library; do
    [[ "$(basename "$library")" == libc.so.* ]] && continue
    patchelf --force-rpath --set-rpath '$ORIGIN' "$library"
done < <(find "$appdir" -type f -name 'lib*.so*' -print0)

# Keep the main application's native libraries together for $ORIGIN lookup.
while IFS= read -r -d '' library; do
    name=$(basename "$library")
    if [[ ! -e "$bin/$name" ]]; then
        cp -pL "$library" "$bin/$name"
    fi
done < <(find "$appdir" -path "$bin" -prune -o -type f -name 'lib*.so*' -print0)

# Linux resolves a relative PT_INTERP against the launcher's working directory.
# Execute the application itself so /proc/self/exe identifies the real program.
cp -pL "$appdir$loader" "$bin/$loader_name"
chmod +x "$bin/$loader_name"
for executable in SyncClipboard.Desktop.Default SyncClipboard.Updater; do
    patchelf --set-interpreter "./$loader_name" --force-rpath --set-rpath '$ORIGIN' "$bin/$executable"
done
# NativeAOT and Avalonia also load libraries dynamically. These roots cover the
# updater's X11 software renderer, session management, fonts and cryptography.
# GTK dialogs, GPU rendering and ICU are not used by this updater (see Updater.props
# and Program.cs); transitive ELF dependencies are discovered below.
bash "$(dirname -- "${BASH_SOURCE[0]}")/collect-elf-dependencies.sh" "$bin" "$loader_name" \
    SyncClipboard.Updater libSkiaSharp.so libHarfBuzzSharp.so \
    libX11.so.6 libXrandr.so.2 libXext.so.6 libXi.so.6 libXcursor.so.1 libXfixes.so.3 \
    libSM.so.6 libICE.so.6 libfontconfig.so.1 libglib-2.0.so.0 libssl.so.3 libz.so.1 \
    > "$bin/appimage-updater.files"
cat > "$appdir/AppRun" <<EOF
#!/bin/sh
HERE=\${APPDIR:-\$(CDPATH= cd -- "\$(dirname -- "\$0")" && pwd)}
cd -- "\$HERE/usr/bin" || exit 1
exec ./SyncClipboard.Desktop.Default "\$@"
EOF
chmod +x "$appdir/AppRun"
