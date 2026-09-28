#!/bin/bash
set -e

SCRIPT_DIR=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
BIN_DIR=$SCRIPT_DIR/build_bin
CONF_PATH=$SCRIPT_DIR/linux.pupnet.conf
CHANGES_MD=$SCRIPT_DIR/../../Changes.md

chmod +x $SCRIPT_DIR/PostPublish.sh

package_kind=''
rid=''
bin_source_dir=''

function arg_info() {
    echo "Args:"
    echo "-k <package_kind>             package kind in deb/rpm/AppImage"
    echo "-r <rid>                      .NET Runtime IDentifier"
    echo "-s <bin_source_dir>           source directory of the binaries"
}

while getopts "k:r:s:" option;
do
    case "$option" in
        k)
            package_kind=$OPTARG
            echo "package_kind : $package_kind";;
        r)
            rid=$OPTARG
            echo "rid : $rid";;
        s)
            bin_source_dir=$OPTARG
            echo "bin_source_dir : $bin_source_dir";;
        \?)
            arg_info
            exit 1;;
    esac
done

if [[ "$package_kind" == ""
        || "$rid" == ""
        || "$bin_source_dir" == ""
    ]]; then
    echo A required parameter is needed but not set
    arg_info
    exit 1
fi

if [[ ! -d $bin_source_dir ]]; then
    echo "bin_source_dir $bin_source_dir is not a directory or not exist"
    exit 1
fi
bin_source_dir=$(readlink -f $bin_source_dir)
echo "bin_source_dir full path : $bin_source_dir"
ls -l $bin_source_dir

if [[ $bin_source_dir != $BIN_DIR ]]; then
    if [ -e $BIN_DIR ]; then
        rm -rf $BIN_DIR
    fi
    mkdir $BIN_DIR
    cp -r $bin_source_dir/* $BIN_DIR/
fi

read -r version < $CHANGES_MD

beta_version='10000'
if [[ $version == *"-beta"* ]]; then
    beta_version=$(echo "$version" | sed -nE 's/.*-beta([0-9]+).*/\1/p')
    base_version=$(echo $version | sed -E 's/^v|(-beta[0-9]+)//g')
else
    base_version=$(echo $version | sed -E 's/^v//g')
fi
version=$base_version\[$beta_version\]

echo "beta_version : $beta_version"
echo "base_version : $base_version"
echo "version : $version"

if [[ "$package_kind" == "AppImage" || "$package_kind" == "appimage" ]]; then
    # Render beside the source configuration so relative paths keep working.
    runtime_conf=$(mktemp "$SCRIPT_DIR/.appimage.XXXXXX.pupnet.conf")
    trap 'rm -f "$runtime_conf"' EXIT
    if [[ "${APPIMAGE_UPDATES:-false}" == "true" ]]; then
        sed "s#@UPDATE_ARCH@#${rid#linux-}#g" "$CONF_PATH" > "$runtime_conf"
        if [[ -n "${GITHUB_ENV:-}" ]]; then
            update_info=$(sed -n 's/^AppImageArgs = -u "\(.*\)"$/\1/p' "$runtime_conf")
            echo "APPIMAGE_UPDATE_INFORMATION=$update_info" >> "$GITHUB_ENV"
        fi
    else
        sed 's/^AppImageArgs = .*/AppImageArgs =/' "$CONF_PATH" > "$runtime_conf"
    fi
    CONF_PATH=$runtime_conf
fi

pupnet "$CONF_PATH" --app-version "$version" --kind "$package_kind" -r "$rid" -y
