#!/usr/bin/env bash
set -euo pipefail
tools=$(realpath -m "$1")
mkdir -p "$tools"
arch=$(uname -m)
case "$arch" in
    x86_64) checksum=ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0 ;;
    aarch64) checksum=f0837e7448a0c1e4e650a93bb3e85802546e60654ef287576f46c71c126a9158 ;;
    *) echo "Unsupported AppImage build architecture: $arch" >&2; exit 1 ;;
esac
tool="$tools/appimagetool-$arch.AppImage"
curl --fail --location --retry 3 "https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-$arch.AppImage" -o "$tool"
echo "$checksum  $tool" | sha256sum --check
chmod +x "$tool"
# Pin source instead of a continuous-release asset that upstream can remove.
GOBIN="$tools" go install github.com/probonopd/go-appimage/src/appimagetool@b7864b5e53d4d7a1d5f31fdc23d1e591d77b8a21
mv "$tools/appimagetool" "$tools/go-appimagetool"
# Upstream checks this helper even for `deploy`. Publishing belongs to our
# release workflow; fail explicitly if the deployment tool ever tries it.
printf '#!/bin/sh\necho "Uploads are handled by the release workflow." >&2\nexit 1\n' > "$tools/uploadtool"
chmod +x "$tools/uploadtool"
if [[ -n "${GITHUB_PATH:-}" ]]; then
    echo "$tools" >> "$GITHUB_PATH"
fi
