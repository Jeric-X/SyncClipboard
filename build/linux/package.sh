#!/usr/bin/env bash
set -euo pipefail
scripts=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
kind=''
rid=''
source_dir=''
build_type=self-contained
while getopts "k:r:s:t:" option; do
    case "$option" in
        k) kind="$OPTARG" ;;
        r) rid="$OPTARG" ;;
        s) source_dir="$OPTARG" ;;
        t) build_type="$OPTARG" ;;
        *) exit 1 ;;
    esac
done
if [[ -z "$kind" || -z "$rid" || -z "$source_dir" ]]; then
    echo 'Usage: package.sh -k <AppImage|deb|rpm> -r <linux-x64|linux-arm64> -s <binaries> [-t <self-contained|no-self-contained>]' >&2
    exit 1
fi
case "$kind" in
    AppImage|appimage) kind=AppImage ;;
    deb|rpm) ;;
    *) echo "Unsupported package kind: $kind" >&2; exit 1 ;;
esac
case "$rid" in
    linux-x64|linux-arm64) cpu="${rid#linux-}" ;;
    *) echo "Unsupported runtime: $rid" >&2; exit 1 ;;
esac
case "$build_type" in
    self-contained) suffix='' ;;
    no-self-contained) suffix=_no-dotnet-runtime ;;
    *) echo "Unsupported build type: $build_type" >&2; exit 1 ;;
esac
if [[ "$kind" == AppImage && "$build_type" != self-contained ]]; then
    echo 'AppImage requires self-contained binaries.' >&2
    exit 1
fi
source_dir=$(realpath "$source_dir")
output="$scripts/output"
name="SyncClipboard_linux_$cpu$suffix.$kind"
chmod +x "$source_dir/SyncClipboard.Updater"
bash "$scripts/../SetUpdateSource.sh" -m manual -s github -o "$source_dir" -n "$name"

if [[ "$kind" == AppImage ]]; then
    APPIMAGE_UPDATES=true bash "$scripts/appimage/package-appimage.sh" "$source_dir" "$cpu" "$output"
else
    # PupNet's post-publish script resolves paths from this directory.
    cd "$scripts/pupnet"
    bash "$scripts/pupnet/package-pupnet.sh" -k "$kind" -r "$rid" -s "$source_dir"
    case "$kind:$cpu" in
        rpm:x64) tail=.x86_64 ;;
        rpm:arm64) tail=.arm64 ;;
        deb:x64) tail=_amd64 ;;
        deb:arm64) tail=_arm64 ;;
    esac
    mv "$output"/syncclipboard_*-*"$tail.$kind" "$output/$name"
fi
if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
    echo "artifact-name=$name" >> "$GITHUB_OUTPUT"
    if [[ "$kind" == AppImage ]]; then
        echo "update-artifact-name=$name.zsync" >> "$GITHUB_OUTPUT"
    fi
fi
