#!/usr/bin/env bash
set -euo pipefail
scripts=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
# An ELF fixture is enough; it is inspected and patched but never executed.
# Linux can use /bin/true; other hosts can pass any Linux executable/library.
fixture=$(realpath "${1:-/bin/true}")
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
bin="$work/bin with spaces"
mkdir "$bin"
cp "$fixture" "$work/template"
dependencies=$(patchelf --print-needed "$work/template")
while IFS= read -r dependency; do
    if [[ -n "$dependency" ]]; then
        patchelf --remove-needed "$dependency" "$work/template"
    fi
done <<< "$dependencies"

make_elf() {
    local name="$1"
    shift
    cp "$work/template" "$bin/$name"
    patchelf --set-soname "$name" "$bin/$name"
    for dependency in "$@"; do
        patchelf --add-needed "$dependency" "$bin/$name"
    done
}

make_elf appimage-ld.so
patchelf --set-soname ld-test.so "$bin/appimage-ld.so"
make_elf updater liba.so
make_elf liba.so plugin.bin
make_elf plugin.bin liba.so
make_elf dynamic.so liba.so ld-test.so
make_elf unrelated.so

bash "$scripts/collect-elf-dependencies.sh" "$bin" appimage-ld.so updater dynamic.so > "$work/actual"
printf '%s\n' appimage-ld.so updater liba.so plugin.bin dynamic.so | sort > "$work/expected"
sort "$work/actual" > "$work/sorted"
diff -u "$work/expected" "$work/sorted"
echo 'PASS: transitive dependencies, explicit dynamic roots, cycles, deduplication, loader SONAME and unrelated-library exclusion'

patchelf --add-needed absent.so "$bin/plugin.bin"
if bash "$scripts/collect-elf-dependencies.sh" "$bin" appimage-ld.so updater > "$work/actual" 2> "$work/error"; then
    echo 'Expected a missing transitive dependency to fail.' >&2
    exit 1
fi
grep -Fq 'Missing updater dependency in AppDir: absent.so' "$work/error"
echo 'PASS: missing transitive dependency fails instead of falling back to the host'
patchelf --remove-needed absent.so "$bin/plugin.bin"

if bash "$scripts/collect-elf-dependencies.sh" "$bin" appimage-ld.so missing-dynamic.so > "$work/actual" 2> "$work/error"; then
    echo 'Expected a missing dynamic root to fail.' >&2
    exit 1
fi
grep -Fq 'Missing updater dependency in AppDir: missing-dynamic.so' "$work/error"
echo 'PASS: missing dynamic root fails'

printf 'invalid ELF\n' > "$bin/plugin.bin"
if bash "$scripts/collect-elf-dependencies.sh" "$bin" appimage-ld.so updater > "$work/actual" 2> "$work/error"; then
    echo 'Expected invalid ELF metadata to fail.' >&2
    exit 1
fi
echo 'PASS: ELF inspection failures propagate'
