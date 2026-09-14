"""Check that non-UI tests actually collected product code coverage."""

import argparse
from pathlib import Path
import defusedxml.ElementTree as ET


def verify_report(report, required_packages):
    root = ET.parse(report, forbid_dtd=True, forbid_entities=True, forbid_external=True).getroot()
    if root.tag != "coverage":
        raise SystemExit(f"Not a Cobertura coverage report: {report}")
    covered = int(root.get("lines-covered", "0"))
    valid = int(root.get("lines-valid", "0"))
    if not 0 < covered <= valid or not 0 < float(root.get("line-rate", "0")) <= 1:
        raise SystemExit(f"Empty or invalid code coverage: {report}")

    packages = {package.get("name"): package for package in root.findall("packages/package")}
    for name in required_packages:
        package = packages.get(name)
        if package is None:
            raise SystemExit(f"Missing product coverage for {name}: {report}")
        lines = package.findall("classes/class/lines/line")
        if not any(int(line.get("hits", "0")) > 0 for line in lines):
            raise SystemExit(f"No executed product lines for {name}: {report}")
    print(f"{report}: {covered}/{valid} product lines hit; required packages: {', '.join(required_packages)}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--require-package", nargs="+", required=True)
    args = parser.parse_args()
    reports = list(args.directory.rglob("coverage.cobertura.xml"))
    if not reports:
        raise SystemExit(f"No code coverage report in {args.directory}")
    # VSTest can retain the original attachment and a deployment copy. Check each,
    # without adding their line counts together or treating them as separate runs.
    for report in reports:
        verify_report(report, args.require_package)


if __name__ == "__main__":
    main()
