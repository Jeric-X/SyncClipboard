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

# A flat native library directory can also travel with the standalone updater.
manifest="$bin/appimage-updater.files"
printf '%s\n' "$loader_name" > "$manifest"
while IFS= read -r -d '' library; do
    name=$(basename "$library")
    if [[ ! -e "$bin/$name" ]]; then
        cp -pL "$library" "$bin/$name"
    fi
    printf '%s\n' "$name" >> "$manifest"
done < <(find "$appdir" -path "$bin" -prune -o -type f -name 'lib*.so*' -print0)
sort -u -o "$manifest" "$manifest"

# Linux resolves a relative PT_INTERP against the launcher's working directory.
# Execute the application itself so /proc/self/exe identifies the real program.
cp -pL "$appdir$loader" "$bin/$loader_name"
chmod +x "$bin/$loader_name"
for executable in SyncClipboard.Desktop.Default SyncClipboard.Updater; do
    patchelf --set-interpreter "./$loader_name" --force-rpath --set-rpath '$ORIGIN' "$bin/$executable"
done
cat > "$appdir/AppRun" <<EOF
#!/bin/sh
HERE=\${APPDIR:-\$(CDPATH= cd -- "\$(dirname -- "\$0")" && pwd)}
cd -- "\$HERE/usr/bin" || exit 1
exec ./SyncClipboard.Desktop.Default "\$@"
EOF
chmod +x "$appdir/AppRun"
