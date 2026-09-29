#!/usr/bin/env python3
"""Publish the standalone updater, package sibling native libraries and verify the ZIP in CI."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / "src/SyncClipboard.Updater/SyncClipboard.Updater.csproj"
NAME = "SyncClipboard.Updater"


def run(command, log=None, cwd=ROOT, timeout=1800):
    print("+ " + " ".join(map(str, command)), flush=True)
    result = subprocess.run(list(map(str, command)), cwd=cwd, text=True,
                            encoding="utf-8", errors="replace", stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, timeout=timeout)
    print(result.stdout, flush=True)
    if log:
        log.write_text(result.stdout, encoding="utf-8")
    result.check_returncode()
    return result.stdout


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True, choices=[
        "win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"])
    args = parser.parse_args()
    os_name, architecture = args.rid.split("-")
    expected = {"win": "Windows", "linux": "Linux", "osx": "Darwin"}[os_name]
    host_arch = {"amd64": "x64", "x86_64": "x64", "aarch64": "arm64", "arm64": "arm64"}.get(
        platform.machine().lower())
    if platform.system() != expected or host_arch != architecture:
        parser.error("Publish and GUI checks must run on the target OS and architecture.")
    out = ROOT / "artifacts/updater" / args.rid
    out.mkdir(parents=True, exist_ok=True)
    publish = out / "publish"
    delivery = out / "delivery"
    for directory in (publish, delivery):
        shutil.rmtree(directory, ignore_errors=True)
        directory.mkdir()
    run(["dotnet", "publish", PROJECT, "-r", args.rid, "-c", "Release", "-o", publish], out / "publish.log")
    executable = NAME + (".exe" if os_name == "win" else "")
    shutil.copy2(publish / executable, delivery / executable)
    native = [p for p in publish.iterdir() if p.suffix in (".dll", ".so", ".dylib")]
    if not native:
        raise RuntimeError("No Avalonia native libraries found in publish output.")
    excluded = []
    for source in native:
        # Software rendering does not initialize ANGLE; verified against the actual delivery below.
        if os_name == "win" and source.name == "av_libglesv2.dll":
            excluded.append(source.name)
            continue
        target = delivery / source.name
        shutil.copy2(source, target)
        if os_name == "osx":
            arch = "x86_64" if architecture == "x64" else "arm64"
            archs = run(["lipo", "-archs", target]).strip().split()
            if arch not in archs:
                raise RuntimeError(f"{target.name} does not contain {arch}.")
            if len(archs) > 1:
                thin = target.with_suffix(".thin")
                run(["lipo", target, "-thin", arch, "-output", thin])
                thin.replace(target)
                run(["codesign", "--force", "--sign", "-", target])
    files = [{"name": path.name, "bytes": path.stat().st_size, "sha256": sha256(path)}
             for path in sorted(delivery.iterdir())]
    archive = out / f"SyncClipboard.Updater-{args.rid}.zip"
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as package:
        for path in sorted(delivery.iterdir()):
            info = zipfile.ZipInfo(path.name, date_time=(2020, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 3
            info.external_attr = 0o100755 << 16
            package.writestr(info, path.read_bytes(), compresslevel=9)

    checks = {}
    with tempfile.TemporaryDirectory(prefix="syncclipboard-updater-") as temporary:
        extracted = Path(temporary) / "更新 helper 含空格"
        with zipfile.ZipFile(archive) as package:
            package.extractall(extracted)
        binary = extracted / executable
        binary.chmod(0o755)
        for item in files:
            if sha256(extracted / item["name"]) != item["sha256"]:
                raise RuntimeError("Extracted checksum mismatch: " + item["name"])
        for mode, marker in [("--self-test", "SELF_TEST=PASS"), ("--smoke-test", "GUI_SMOKE=PASS")]:
            command = [binary, mode]
            if os_name == "linux":
                command = ["xvfb-run", "-a", *command]
            output = run(command, out / (mode[2:] + ".log"), cwd=temporary, timeout=90)
            if marker not in output:
                raise RuntimeError("Missing success marker: " + marker)
            loaded = sorted(set(line.split("=", 1)[1] for line in output.splitlines()
                                if line.startswith("NATIVE_LIBRARY=")))
            expected_libs = sorted(item["name"] for item in files if item["name"] != executable)
            if mode == "--smoke-test" and loaded != expected_libs:
                raise RuntimeError(f"Expected sibling libraries {expected_libs}, loaded {loaded}.")
            checks[mode[2:]] = {"passed": True, "loaded_libraries": loaded}

    report = {
        "rid": args.rid, "commit": run(["git", "rev-parse", "HEAD"]).strip(),
        "zip_bytes": archive.stat().st_size, "zip_sha256": sha256(archive),
        "uncompressed_bytes": sum(item["bytes"] for item in files),
        "files": files, "excluded_native_files": excluded, "checks": checks,
        "settings": {"aot": True, "optimization": "Size", "rendering": "Software"},
    }
    (out / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    summary = (f"### Updater {args.rid}\n\n"
               f"ZIP: {report['zip_bytes'] / 1048576:.2f} MiB; "
               f"extracted: {report['uncompressed_bytes'] / 1048576:.2f} MiB.\n\n"
               "Generated JSON and GUI checks passed from the extracted ZIP. No installation was performed.\n")
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as stream:
            stream.write(summary)
    print(summary)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    main()
