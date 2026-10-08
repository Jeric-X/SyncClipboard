#!/bin/bash
set -e
# This is a dummy bash script used for demonstration and test. It outputs a few variables
# and creates a dummy file in the application directory which will be detected by the program.

echo
echo ===========================
echo POST_PUBLISH BASH SCRIPT
echo ===========================
echo

# Some useful macros  environment variables
echo BUILD_ARCH ${BUILD_ARCH}
echo BUILD_TARGET ${BUILD_TARGET}
echo BUILD_SHARE ${BUILD_SHARE}
echo BUILD_APP_BIN ${BUILD_APP_BIN}
echo

echo Do work...
set -x #echo on
echo Copying files
# build on Windows first, put outputs in [../linux/] foleder
build_bin_dir=$(readlink -f './build_bin')
echo build_bin_dir full path : $bin_source_dir

cp -r ./build_bin/* "${BUILD_APP_BIN}/"

scripts=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
desktop=$(<"$scripts/../app.desktop")
for variable in APP_FRIENDLY_NAME APP_ID APP_SHORT_SUMMARY INSTALL_EXEC \
    DESKTOP_NODISPLAY DESKTOP_INTEGRATE DESKTOP_TERMINAL PRIME_CATEGORY APP_BASE_NAME; do
    desktop=${desktop//\$\{$variable\}/"${!variable}"}
done
printf '%s\n' "$desktop" > "${BUILD_APP_BIN}/xyz.jericx.desktop.syncclipboard.desktop"

set +x #echo off

echo
echo ===========================
echo POST_PUBLISH END
echo ===========================
echo