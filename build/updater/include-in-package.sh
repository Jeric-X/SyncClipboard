#!/usr/bin/env bash
# Copy the matching updater into packages that use file replacement.
set -euo pipefail
shopt -s nullglob dotglob

fail() { echo "$*" >&2; exit 1; }
sha256() { openssl dgst -sha256 -r "$1" | cut -d ' ' -f 1; }

rid= package_type= app_dir= artifact_dir=
while [[ $# -gt 0 ]]; do
    [[ $# -ge 2 ]] || fail "Missing value for $1"
    case "$1" in
        --rid) rid=$2 ;;
        --package-type) package_type=$2 ;;
        --app-dir) app_dir=$2 ;;
        --artifact-dir) artifact_dir=$2 ;;
        *) fail "Unknown option: $1" ;;
    esac
    shift 2
done
[[ -d $app_dir ]] || fail "The application's packaging directory does not exist."
case "$package_type" in
    portable|installer) expected_os=win ;;
    dmg) expected_os=osx ;;
    AppImage|deb|rpm) expected_os=linux ;;
    *) fail "Unsupported package type: $package_type" ;;
esac
[[ $rid == "$expected_os-x64" || $rid == "$expected_os-arm64" ]] || fail "RID does not match package type."
destination="$app_dir/Updater"
[[ ! -e $destination && ! -L $destination ]] || fail "The packaging directory already contains an updater."
case "$package_type" in
    installer|deb|rpm)
        echo "This package does not include an updater."
        exit 0 ;;
esac
[[ -n $artifact_dir && -d $artifact_dir/delivery ]] || fail "This package requires an updater artifact directory."
report="$artifact_dir/report.json"
jq -e --arg rid "$rid" --arg commit "$(git rev-parse HEAD)" \
    '.rid == $rid and .commit == $commit and .checks["smoke-test"].passed == true' "$report" > /dev/null \
    || fail "Updater RID, source commit or GUI check does not match the application build."
source="$artifact_dir/delivery"
files=("$source/"*)
executable=SyncClipboard.Updater
[[ $expected_os != win ]] || executable+=.exe
[[ -f $source/$executable ]] || fail "Updater executable is missing."
jq -e --argjson count "${#files[@]}" '.files | length == $count' "$report" > /dev/null \
    || fail "Updater directory contents do not match the manifest."
for file in "${files[@]}"; do
    [[ -f $file && ! -L $file ]] || fail "Invalid updater file: $file"
    jq -e --arg name "${file##*/}" --arg checksum "$(sha256 "$file")" \
        '[.files[] | select(.name == $name and .sha256 == $checksum)] | length == 1' "$report" > /dev/null \
        || fail "Updater checksum mismatch: $file"
done
mkdir "$destination"
for file in "${files[@]}"; do
    target="$destination/${file##*/}"
    cp "$file" "$target"
    # Artifact transport does not preserve Unix executable permissions.
    chmod 644 "$target"
done
chmod 755 "$destination/$executable"
echo "Included $rid updater in $package_type."
