#!/bin/bash

function arg_info() {
    echo "Args:"
    echo "-m <manage_type>              management type ('manual' or 'manager')"
    echo "-s <update_src>               update source (e.g., 'github' for GitHub releases, or package manager name such as 'homebrew', 'aur', etc.)"
    echo "-n <package_name>             name of the package"
    echo "-o <output_dir>               output directory for update_info.json"
}

while getopts "m:s:n:o:" opt; do
    case $opt in
        m) manage_type="$OPTARG" ;;
        s) update_src="$OPTARG" ;;
        n) package_name="$OPTARG" ;;
        o) output_dir="$OPTARG" ;;
        *) arg_info; exit 1 ;;
    esac
done

if [[ "$1" == "-h" ]]; then
    arg_info
    exit 0
fi

missing_opts=()
[[ -z "$manage_type" ]] && missing_opts+=("-m manage_type")
[[ -z "$update_src" ]] && missing_opts+=("-s update_src")
[[ -z "$package_name" ]] && missing_opts+=("-p package_name")

if (( ${#missing_opts[@]} )); then
    echo "Missing required option(s): ${missing_opts[*]}" >&2
    arg_info
    exit 1
fi

if [[ -z "$output_dir" ]]; then
    output_dir="."
fi

if [[ "$manage_type" != "manual" && "$manage_type" != "manager" ]]; then
    echo "Error: manage_type must be either 'manual' or 'manager'." >&2
    exit 1
fi

mkdir -p "$output_dir"

cat > "$output_dir/update_info.json" <<EOF
{
    "manage_type": "$manage_type",
    "update_src": "$update_src",
    "package_name": "$package_name"
}
EOF