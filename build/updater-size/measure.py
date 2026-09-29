#!/usr/bin/env python3
"""Publish, bundle and exercise the isolated NativeAOT updater size probe."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import tempfile
import zipfile


ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / "src/SyncClipboard.Updater.SizeProbe/SyncClipboard.Updater.SizeProbe.csproj"
NAME = "SyncClipboard.Updater.SizeProbe"


def run(command, log=None, cwd=ROOT, timeout=1800):
    print("+ " + " ".join(map(str, command)), flush=True)
    result = subprocess.run(list(map(str, command)), cwd=cwd, text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=timeout)
    print(result.stdout, flush=True)
    if log:
        log.write_text(result.stdout, encoding="utf-8")
    result.check_returncode()
    return result.stdout


def archive_files(destination, files):
    # Fixed order and timestamps make the compression measurement repeatable.
    with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(files):
            info = zipfile.ZipInfo(path.name, date_time=(2020, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100755 << 16
            archive.writestr(info, path.read_bytes(), compresslevel=9)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True, choices=["osx-arm64", "linux-x64", "win-x64"])
    parser.add_argument("--reuse-baseline", action="store_true")
    args = parser.parse_args()
    expected_os = {"osx-arm64": "Darwin", "linux-x64": "Linux", "win-x64": "Windows"}[args.rid]
    if platform.system() != expected_os:
        parser.error("Run on the target OS: NativeAOT does not support cross-OS publishing.")
    out = ROOT / "artifacts/updater-size" / args.rid
    out.mkdir(parents=True, exist_ok=True)
    baseline = out / "baseline"
    bundled = out / "bundled"
    native = out / "native"
    delivery = out / "delivery"
    extension = ".exe" if args.rid.startswith("win") else ""
    executable_name = NAME + extension
    publish = ["dotnet", "publish", PROJECT, "-r", args.rid, "-c", "Release"]
    if not args.reuse_baseline:
        shutil.rmtree(baseline, ignore_errors=True)
        run(publish + ["-o", baseline], out / "baseline-publish.log")
    if not (baseline / executable_name).is_file():
        raise RuntimeError("Baseline executable is missing.")

    for directory in (native, bundled, delivery):
        shutil.rmtree(directory, ignore_errors=True)
        directory.mkdir()
    native_sources = [p for p in baseline.iterdir() if p.suffix in (".dylib", ".so", ".dll")]
    if not native_sources:
        raise RuntimeError("Expected Avalonia native libraries in publish output.")
    # Software-only rendering never initializes ANGLE. The isolated GUI smoke test
    # must still pass after removing its native binary from the delivery payload.
    excluded = [p for p in native_sources if args.rid == "win-x64" and p.name == "av_libglesv2.dll"]
    for source in native_sources:
        if source in excluded:
            continue
        target = native / source.name
        shutil.copy2(source, target)
        if args.rid == "osx-arm64":
            archs = run(["lipo", "-archs", target]).strip().split()
            if len(archs) > 1:
                thin = target.with_suffix(".thin")
                run(["lipo", target, "-thin", "arm64", "-output", thin])
                thin.replace(target)
                run(["codesign", "--force", "--sign", "-", target])

    payload = out / "native.zip"
    archive_files(payload, native.iterdir())
    run(publish + ["-o", bundled, f"-p:NativePayload={payload}"], out / "bundled-publish.log")
    binary = delivery / executable_name
    shutil.copy2(bundled / executable_name, binary)
    if list(delivery.iterdir()) != [binary]:
        raise RuntimeError("Delivery must contain exactly one file.")

    results = {}
    # Exercise from a clean location, away from publish output and original native libraries.
    with tempfile.TemporaryDirectory(prefix="syncclipboard-clean-room-") as clean:
        clean_binary = Path(clean) / executable_name
        shutil.copy2(binary, clean_binary)
        for mode, marker in [("--self-test", "SELF_TEST=PASS"), ("--smoke-test", "GUI_SMOKE=PASS")]:
            command = [clean_binary, mode]
            if args.rid.startswith("linux"):
                command = ["xvfb-run", "-a"] + command
            output = run(command, out / (mode[2:] + ".log"), cwd=clean, timeout=90)
            if marker not in output:
                raise RuntimeError(f"Missing success marker: {marker}")
            extracted = [line.split("=", 1)[1] for line in output.splitlines()
                         if line.startswith("EXTRACTION_DIRECTORY=")]
            if len(extracted) != 1 or Path(extracted[0]).exists():
                raise RuntimeError("The extracted native directory was not cleaned up.")
            loaded = sorted(set(line.split("=", 1)[1] for line in output.splitlines()
                                if line.startswith("NATIVE_LIBRARY=")))
            if mode == "--smoke-test" and not any("SkiaSharp" in name for name in loaded):
                raise RuntimeError("The GUI did not load the extracted renderer.")
            results[mode[2:]] = {"passed": True, "extraction_cleaned": True, "loaded_libraries": loaded}

    archive_files(out / "delivery.zip", [binary])
    archive_files(out / "baseline.zip", [baseline / executable_name] + native_sources)
    files = [{"name": p.name, "bytes": p.stat().st_size,
              "sha256": hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(native.iterdir())]
    report = {
        "rid": args.rid,
        "commit": run(["git", "rev-parse", "HEAD"]).strip(),
        "sdk": run(["dotnet", "--version"]).strip(),
        "os": platform.platform(),
        "baseline_executable_bytes": (baseline / executable_name).stat().st_size,
        "baseline_native_bytes": sum(p.stat().st_size for p in native_sources),
        "baseline_total_bytes": (baseline / executable_name).stat().st_size + sum(p.stat().st_size for p in native_sources),
        "baseline_zip_bytes": (out / "baseline.zip").stat().st_size,
        "extracted_native_bytes": sum(p["bytes"] for p in files),
        "embedded_native_zip_bytes": payload.stat().st_size,
        "single_file_bytes": binary.stat().st_size,
        "single_file_zip_bytes": (out / "delivery.zip").stat().st_size,
        "running_disk_bytes": binary.stat().st_size + sum(p["bytes"] for p in files),
        "sha256": hashlib.sha256(binary.read_bytes()).hexdigest(),
        "native_files": files,
        "excluded_native_files": [{"name": p.name, "bytes": p.stat().st_size,
                                   "reason": "Software renderer; verified by isolated GUI smoke test"} for p in excluded],
        "tests": results,
        "settings": {"aot": True, "trim": "full", "optimization": "Size", "invariant_globalization": True,
                     "rendering": "Software", "theme": None, "bundled_fonts": False},
    }
    (out / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    labels = ["baseline_executable_bytes", "baseline_total_bytes", "baseline_zip_bytes", "extracted_native_bytes",
              "single_file_bytes", "single_file_zip_bytes", "running_disk_bytes"]
    markdown = f"## Updater size probe: {args.rid}\n\n| Measurement | Bytes | MiB |\n|---|---:|---:|\n"
    markdown += "".join(f"| {label} | {report[label]} | {report[label] / 1048576:.2f} |\n" for label in labels)
    markdown += "\nNativeAOT, full trimming, software rendering; no theme or bundled fonts.\n"
    markdown += "File/SHA256/ZIP and real desktop GUI smoke tests passed from a single-file directory.\n"
    markdown += "Symbols excluded. macOS bundled libraries are thinned to arm64; baseline preserves NuGet originals.\n"
    (out / "report.md").write_text(markdown, encoding="utf-8")
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
            summary.write(markdown)
    print(markdown)


if __name__ == "__main__":
    main()
