#!/usr/bin/env bash
set -euo pipefail
scripts=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
kind="${1:?Usage: install-dependencies.sh <AppImage|deb|rpm>}"
tools="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/syncclipboard-packaging"

case "$kind" in
    AppImage|appimage)
        bash "$scripts/install-appimage-dependencies.sh"
        bash "$scripts/install-appimage-tools.sh" "$tools/appimage"
        ;;
    deb|rpm)
        dotnet --info
        dotnet tool install KuiperZone.PupNet --version 1.10.0 --tool-path "$tools/pupnet"
        if [[ -n "${GITHUB_PATH:-}" ]]; then
            printf '%s\n' "$tools/pupnet" >> "$GITHUB_PATH"
        fi
        ;;
    *) echo "Unsupported package kind: $kind" >&2; exit 1 ;;
esac
