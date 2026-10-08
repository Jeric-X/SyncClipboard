#!/usr/bin/env bash
set -euo pipefail
kind="${1:?Usage: install-dependencies.sh <AppImage|deb|rpm>}"

case "$kind" in
    AppImage|appimage)
        # Build-time packages for dependency discovery, including libraries loaded via
        # P/Invoke/dlopen instead of the ELF DT_NEEDED table. Run on the target architecture.
        sudo apt-get update
        sudo apt-get install --no-install-recommends -y \
            patchelf desktop-file-utils file binutils squashfs-tools zsync xvfb xauth \
            libicu74 libssl3t64 libfontconfig1 libx11-6 libice6 libsm6 \
            libxrandr2 libxi6 libxcursor1 libxrender1 libxext6 libxfixes3 \
            libglib2.0-0t64 libglib2.0-bin libgssapi-krb5-2 liblttng-ust1t64 \
            libinput10 libgbm1 libdrm2 libegl1 libgles2 libxkbcommon0 libwayland-client0

        tools=$(mktemp -d)
        trap 'rm -rf "$tools"' EXIT
        arch=$(uname -m)
        case "$arch" in
            x86_64) checksum=ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0 ;;
            aarch64) checksum=f0837e7448a0c1e4e650a93bb3e85802546e60654ef287576f46c71c126a9158 ;;
            *) echo "Unsupported AppImage build architecture: $arch" >&2; exit 1 ;;
        esac
        tool="$tools/appimagetool-$arch.AppImage"
        curl --fail --location --retry 3 "https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-$arch.AppImage" -o "$tool"
        echo "$checksum  $tool" | sha256sum --check
        # Pin source instead of a continuous-release asset that upstream can remove.
        GOBIN="$tools" go install github.com/probonopd/go-appimage/src/appimagetool@b7864b5e53d4d7a1d5f31fdc23d1e591d77b8a21
        mv "$tools/appimagetool" "$tools/go-appimagetool"
        # Upstream checks this helper even for `deploy`. Publishing belongs to our
        # release workflow; fail explicitly if the deployment tool ever tries it.
        printf '#!/bin/sh\necho "Uploads are handled by the release workflow." >&2\nexit 1\n' > "$tools/uploadtool"
        sudo install -m 755 "$tool" "$tools/go-appimagetool" "$tools/uploadtool" /usr/local/bin/
        ;;
    deb|rpm)
        dotnet --info
        dotnet tool install -g KuiperZone.PupNet --version 1.10.0
        ;;
    *) echo "Unsupported package kind: $kind" >&2; exit 1 ;;
esac
