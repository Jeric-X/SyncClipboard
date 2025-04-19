#!/bin/bash


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

while getopts "o:v:k:r:s:" option;
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
if [[ ${version:0:1} == "v" ]]; then
    version=${version:1}
fi
echo "version : $version"

pupnet $CONF_PATH --app-version $version --kind $package_kind -r $rid -y
if [ $? -ne 0 ]; then
    exit 1
fi