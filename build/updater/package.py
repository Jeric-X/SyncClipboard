#!/usr/bin/env python3
"""Publish the standalone updater and verify its distributable directory in CI."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import struct
import sys
import tempfile

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
    if os_name == "win":
        # NativeAOT must produce a GUI executable, without an extra console window.
        binary = (delivery / executable).read_bytes()
        pe_offset = struct.unpack_from("<I", binary, 0x3C)[0]
        if binary[pe_offset:pe_offset + 4] != b"PE\0\0":
            raise RuntimeError("The Windows updater is not a PE executable.")
        subsystem = struct.unpack_from("<H", binary, pe_offset + 24 + 68)[0]
        if subsystem != 2:
            raise RuntimeError(f"Expected Windows GUI subsystem (2), got {subsystem}.")
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
    if os_name == "osx":
        for path in delivery.iterdir():
            run(["codesign", "--force", "--sign", "-", "--timestamp=none", path])
            run(["codesign", "--verify", "--strict", path])
    files = [{"name": path.name, "bytes": path.stat().st_size, "sha256": sha256(path)}
             for path in sorted(delivery.iterdir())]
    checks = {}
    with tempfile.TemporaryDirectory(prefix="syncclipboard-updater-") as temporary:
        copied = Path(temporary) / "更新 helper 含空格"
        shutil.copytree(delivery, copied)
        binary = copied / executable
        binary.chmod(0o755)
        for item in files:
            if sha256(copied / item["name"]) != item["sha256"]:
                raise RuntimeError("Copied checksum mismatch: " + item["name"])
        command = [binary, "--smoke-test"]
        if os_name == "linux":
            command = ["xvfb-run", "-a", *command]
        output = run(command, out / "smoke-test.log", cwd=temporary, timeout=90)
        if "GUI_SMOKE=PASS" not in output:
            raise RuntimeError("Missing GUI success marker.")
        checks["smoke-test"] = {"passed": True}

    report = {
        "rid": args.rid, "commit": run(["git", "rev-parse", "HEAD"]).strip(),
        "uncompressed_bytes": sum(item["bytes"] for item in files),
        "files": files, "excluded_native_files": excluded, "checks": checks,
        "settings": {"aot": True, "optimization": "Size", "rendering": "Software"},
    }
    (out / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    summary = (f"### Updater {args.rid}\n\n"
               f"Payload: {report['uncompressed_bytes'] / 1048576:.2f} MiB before the application's compression.\n\n"
               "GUI startup passed with default library loading from a copied directory. No installation was performed.\n")
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as stream:
            stream.write(summary)
    print(summary)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    main()
