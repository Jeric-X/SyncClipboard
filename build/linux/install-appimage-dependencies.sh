#!/usr/bin/env bash
set -euo pipefail
# Build-time packages for dependency discovery, including libraries loaded via
# P/Invoke/dlopen instead of the ELF DT_NEEDED table. Run on the target architecture.
sudo apt-get update
sudo apt-get install --no-install-recommends -y \
    patchelf desktop-file-utils file binutils squashfs-tools zsync \
    libicu74 libssl3t64 libfontconfig1 libx11-6 libice6 libsm6 \
    libxrandr2 libxi6 libxcursor1 libxrender1 libxext6 libxfixes3 \
    libglib2.0-0t64 libglib2.0-bin libgssapi-krb5-2 liblttng-ust1t64 \
    libinput10 libgbm1 libdrm2 libegl1 libgles2 libxkbcommon0 libwayland-client0
