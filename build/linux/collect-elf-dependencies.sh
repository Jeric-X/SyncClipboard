#!/usr/bin/env bash
set -euo pipefail
# Resolve a dependency closure inside the flattened AppDir directory. Never fall
# back to host libraries: every listed file must travel with the updater.
bin=$(realpath "${1:?Usage: collect-elf-dependencies.sh <directory> <loader> <roots...>}")
loader="${2:?Missing loader filename}"
shift 2
loader_soname=$(patchelf --print-soname "$bin/$loader")
visited=''

collect() {
    local name="$1" dependencies dependency
    # libc can reference the loader by its original SONAME. The interpreter is
    # already loaded under that name, even though we ship it as appimage-ld.so.
    if [[ -n "$loader_soname" && "$name" == "$loader_soname" ]]; then
        name="$loader"
    fi
    case "$name" in
        ''|.|..|*/*) echo "Unsupported ELF dependency name: $name" >&2; exit 1 ;;
    esac
    if printf '%s' "$visited" | grep -Fxq -- "$name"; then
        return
    fi
    if [[ ! -f "$bin/$name" ]]; then
        echo "Missing updater dependency in AppDir: $name" >&2
        exit 1
    fi
    dependencies=$(patchelf --print-needed "$bin/$name")
    visited+="$name"$'\n'
    printf '%s\n' "$name"
    while IFS= read -r dependency; do
        if [[ -n "$dependency" ]]; then
            collect "$dependency"
        fi
    done <<< "$dependencies"
}

# Visit before recursion so shared dependencies and cycles are handled once.
collect "$loader"
for root in "$@"; do
    collect "$root"
done
