#!/usr/bin/env bash
# Publish the standalone updater; run on the target OS and architecture.
set -euo pipefail
shopt -s nullglob

fail() { echo "$*" >&2; exit 1; }
sha256() { openssl dgst -sha256 -r "$1" | cut -d ' ' -f 1; }

[[ $# == 2 && $1 == --rid ]] || fail "Usage: $0 --rid <rid>"
rid=$2
case "$rid" in
    win-x64|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64) ;;
    *) fail "Unsupported RID: $rid" ;;
esac
case "$(uname -s)" in
    Darwin) host_os=osx ;;
    Linux) host_os=linux ;;
    MINGW*|MSYS*) host_os=win ;;
    *) fail "Unsupported host OS" ;;
esac
case "${RUNNER_ARCH:-$(uname -m)}" in
    X64|x86_64|amd64) host_arch=x64 ;;
    ARM64|arm64|aarch64) host_arch=arm64 ;;
    *) fail "Unsupported host architecture" ;;
esac
[[ $rid == "$host_os-$host_arch" ]] || fail "Publish and GUI checks must run on the target OS and architecture."

root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"
out="$root/artifacts/updater/$rid"
publish="$out/publish"
delivery="$out/delivery"
mkdir -p "$out"
rm -f "$out/report.json"
rm -rf "$publish" "$delivery"
mkdir -p "$publish" "$delivery"
dotnet publish src/SyncClipboard.Updater/SyncClipboard.Updater.csproj \
    -r "$rid" -c Release -o "$publish" 2>&1 | tee "$out/publish.log"

executable=SyncClipboard.Updater
[[ $host_os != win ]] || executable+=.exe
cp "$publish/$executable" "$delivery/"
if [[ $host_os == win ]]; then
    # PE subsystem 2 is a GUI executable, without an extra console window.
    pe_offset=$(od -An -tu4 -j60 -N4 "$delivery/$executable" | tr -d '[:space:]')
    signature=$(od -An -tx1 -j"$pe_offset" -N4 "$delivery/$executable" | tr -d '[:space:]')
    [[ $signature == 50450000 ]] || fail "The Windows updater is not a PE executable."
    subsystem=$(od -An -tu2 -j"$((pe_offset + 24 + 68))" -N2 "$delivery/$executable" | tr -d '[:space:]')
    [[ $subsystem == 2 ]] || fail "Expected Windows GUI subsystem (2), got $subsystem."
fi
native=("$publish/"*.dll "$publish/"*.so "$publish/"*.dylib)
[[ ${#native[@]} -gt 0 ]] || fail "No Avalonia native libraries found in publish output."
excluded='[]'
for source in "${native[@]}"; do
    name=${source##*/}
    # Software rendering does not initialize ANGLE.
    if [[ $host_os == win && $name == av_libglesv2.dll ]]; then
        excluded='["av_libglesv2.dll"]'
        continue
    fi
    target="$delivery/$name"
    cp "$source" "$target"
    if [[ $host_os == osx ]]; then
        arch=arm64
        [[ $host_arch != x64 ]] || arch=x86_64
        archs=$(lipo -archs "$target")
        [[ " $archs " == *" $arch "* ]] || fail "$name does not contain $arch."
        if [[ $archs != "$arch" ]]; then
            lipo "$target" -thin "$arch" -output "$target.thin"
            mv "$target.thin" "$target"
        fi
    fi
done
if [[ $host_os == osx ]]; then
    for file in "$delivery/"*; do
        codesign --force --sign - --timestamp=none "$file"
        codesign --verify --strict "$file"
    done
fi

temporary=$(mktemp -d)
smoke_pid=
cleanup() {
    if [[ -n $smoke_pid ]]; then
        kill "$smoke_pid" 2>/dev/null || true
        wait "$smoke_pid" 2>/dev/null || true
    fi
    rm -rf "$temporary"
}
trap cleanup EXIT
copied="$temporary/更新 helper 含空格"
cp -R "$delivery" "$copied"
chmod +x "$copied/$executable"
command=("$copied/$executable" --smoke-test)
[[ $host_os != linux ]] || command=(xvfb-run -a "${command[@]}")
cd "$temporary"
"${command[@]}" > "$out/smoke-test.log" 2>&1 &
smoke_pid=$!
for ((second = 0; second < 90; second++)); do
    kill -0 "$smoke_pid" 2>/dev/null || break
    sleep 1
done
cat "$out/smoke-test.log"
if kill -0 "$smoke_pid" 2>/dev/null; then
    fail "GUI check timed out after 90 seconds."
fi
wait "$smoke_pid"
smoke_pid=
grep -q 'GUI_SMOKE=PASS' "$out/smoke-test.log" || fail "Missing GUI success marker."
cd "$root"

files='[]'
total=0
for file in "$delivery/"*; do
    bytes=$(wc -c < "$file" | tr -d '[:space:]')
    checksum=$(sha256 "$file")
    [[ $(sha256 "$copied/${file##*/}") == "$checksum" ]] || fail "Copied checksum mismatch: ${file##*/}"
    total=$((total + bytes))
    files=$(jq -c --arg name "${file##*/}" --argjson bytes "$bytes" --arg sha256 "$checksum" \
        '. + [{name: $name, bytes: $bytes, sha256: $sha256}]' <<< "$files")
done
jq -n --arg rid "$rid" --arg commit "$(git rev-parse HEAD)" --argjson bytes "$total" \
    --argjson files "$files" --argjson excluded "$excluded" \
    '{rid: $rid, commit: $commit, uncompressed_bytes: $bytes, files: $files,
      excluded_native_files: $excluded, checks: {"smoke-test": {passed: true}},
      settings: {aot: true, optimization: "Size", rendering: "Software"}}' > "$out/report.json"
summary=$(printf '### Updater %s\n\nPayload: %s bytes before application compression.\n\nGUI startup passed with default library loading from a copied directory. No installation was performed.\n' "$rid" "$total")
echo "$summary"
if [[ -n ${GITHUB_STEP_SUMMARY:-} ]]; then
    echo "$summary" >> "$GITHUB_STEP_SUMMARY"
fi
