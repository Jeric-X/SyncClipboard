#!/usr/bin/env python3
"""Include the matching updater directory only in packages that use file replacement."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True)
    parser.add_argument("--package-type", required=True, choices=["portable", "installer", "dmg", "AppImage", "deb", "rpm"])
    parser.add_argument("--app-dir", required=True, type=Path)
    parser.add_argument("--artifact-dir", type=Path)
    args = parser.parse_args()
    if not args.app_dir.is_dir():
        parser.error("The application's packaging directory does not exist.")
    expected_os = {"portable": "win", "installer": "win", "dmg": "osx", "AppImage": "linux", "deb": "linux", "rpm": "linux"}
    if args.rid not in [expected_os[args.package_type] + "-x64", expected_os[args.package_type] + "-arm64"]:
        parser.error("The RID does not match the package type.")
    destination = args.app_dir / "Updater"
    if destination.exists():
        raise RuntimeError("The packaging directory already contains an updater; use a clean staging directory.")
    if args.package_type in ("installer", "deb", "rpm"):
        print("Confirmed: this package does not include an updater.")
        return
    if args.artifact_dir is None:
        parser.error("This package requires --artifact-dir.")
    report = json.loads((args.artifact_dir / "report.json").read_text(encoding="utf-8"))
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
    if report["rid"] != args.rid or report["commit"] != revision:
        raise RuntimeError("Updater RID or source commit does not match the application build.")
    if not report["checks"]["smoke-test"]["passed"]:
        raise RuntimeError("The updater has not passed its standalone GUI check.")
    source = args.artifact_dir / "delivery"
    expected = {item["name"]: item["sha256"] for item in report["files"]}
    if set(path.name for path in source.iterdir()) != set(expected):
        raise RuntimeError("Updater directory contents do not match the manifest.")
    executable = "SyncClipboard.Updater" + (".exe" if args.rid.startswith("win-") else "")
    if executable not in expected:
        raise RuntimeError("The updater executable is missing from the manifest.")
    for name, checksum in expected.items():
        if Path(name).name != name or hashlib.sha256((source / name).read_bytes()).hexdigest() != checksum:
            raise RuntimeError("Invalid updater file: " + name)
    destination.mkdir()
    for name in expected:
        target = destination / name
        shutil.copyfile(source / name, target)
        # Artifact transport does not preserve Unix executable permissions.
        target.chmod(0o755 if name == executable else 0o644)
    print(f"Included {args.rid} updater in {args.package_type}.")


if __name__ == "__main__":
    main()
