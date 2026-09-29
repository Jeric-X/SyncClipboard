#!/usr/bin/env python3
"""Inspect updater files in the finished desktop package without running the application."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import struct
import subprocess
import tarfile
import tempfile
import zipfile


def output(command, **kwargs):
    return subprocess.check_output(list(map(str, command)), **kwargs)


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def appimage_files(archive):
    # Inspect the SquashFS without running the image: arm64 packages are also
    # produced on x64 hosts. Validate candidates because ELF may contain the magic.
    data = archive.read_bytes()
    offset = data.find(b"hsqs")
    while offset >= 0:
        if offset + 96 <= len(data):
            major, minor = struct.unpack_from("<HH", data, offset + 28)
            size = struct.unpack_from("<Q", data, offset + 40)[0]
            if (major, minor) == (4, 0) and 96 <= size <= len(data) - offset:
                probe = subprocess.run(["unsquashfs", "-s", "-o", str(offset), str(archive)],
                                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                if probe.returncode == 0:
                    break
        offset = data.find(b"hsqs", offset + 4)
    if offset < 0:
        raise RuntimeError("No valid AppImage SquashFS found.")
    listing = output(["unsquashfs", "-ls", "-o", offset, archive]).decode("utf-8")
    members = [line.removeprefix("squashfs-root/") for line in listing.splitlines()
               if line.startswith("squashfs-root/") and "/Updater/" in line]
    if len({member.split("/Updater/", 1)[0] for member in members}) != 1:
        raise RuntimeError("Expected exactly one updater directory in the AppImage.")
    return {member.split("/Updater/", 1)[1]: sha256(output(["unsquashfs", "-cat", "-o", offset, archive, member]))
            for member in members}


def dmg_files(archive):
    with tempfile.TemporaryDirectory(prefix="syncclipboard-dmg-check-") as temporary:
        mount = Path(temporary) / "mount"
        mount.mkdir()
        output(["hdiutil", "attach", "-readonly", "-nobrowse", "-mountpoint", mount, archive])
        try:
            app = mount / "SyncClipboard.app"
            subprocess.run(["codesign", "--verify", "--deep", "--strict", str(app)], check=True)
            directory = app / "Contents/Resources/Updater"
            if not (directory / "SyncClipboard.Updater").stat().st_mode & 0o111:
                raise RuntimeError("The packaged updater is not executable.")
            return {path.name: sha256(path.read_bytes()) for path in directory.iterdir()}
        finally:
            subprocess.run(["hdiutil", "detach", str(mount)], check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", required=True, type=Path)
    parser.add_argument("--manifest", type=Path)
    args = parser.parse_args()
    archive = args.package.resolve()
    extension = archive.suffix.lower()
    if extension == ".deb":
        raw = output(["dpkg-deb", "--fsys-tarfile", archive])
        with tarfile.open(fileobj=io.BytesIO(raw)) as package:
            names = package.getnames()
    elif extension == ".rpm":
        raw = output(["rpm2cpio", archive])
        names = output(["cpio", "-it", "--quiet"], input=raw).decode("utf-8").splitlines()
    else:
        if args.manifest is None:
            parser.error("This package requires --manifest.")
        if extension == ".zip":
            with zipfile.ZipFile(archive) as package:
                files = {item.filename.removeprefix("Updater/"): sha256(package.read(item))
                         for item in package.infolist() if item.filename.startswith("Updater/") and not item.is_dir()}
        elif extension == ".appimage":
            files = appimage_files(archive)
        elif extension == ".dmg":
            files = dmg_files(archive)
        else:
            parser.error("Unsupported package format.")
        report = json.loads(args.manifest.read_text(encoding="utf-8"))
        expected = {item["name"]: item["sha256"] for item in report["files"]}
        if not expected or files != expected:
            raise RuntimeError("Packaged updater files differ from the verified build artifact.")
        print("Verified every updater file in the finished package.")
        return
    if any("Updater" in Path(name).parts for name in names):
        raise RuntimeError("This system-managed package must not include the updater.")
    print("Verified that the finished package does not contain the updater.")


if __name__ == "__main__":
    main()
