"""Reject empty, skipped or failed TRX runs, including an empty NonUI filter."""

import argparse
from pathlib import Path
import xml.etree.ElementTree as ET


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    parser.add_argument("--minimum", type=int, required=True)
    args = parser.parse_args()
    reports = list(args.directory.rglob("*.trx"))
    if not reports:
        raise SystemExit(f"No TRX reports in {args.directory}")
    passed = 0
    for report in reports:
        root = ET.parse(report).getroot()
        counters = root.find("{*}ResultSummary/{*}Counters")
        if counters is None:
            raise SystemExit(f"Missing test counters: {report}")
        counts = {name: int(counters.get(name, "0")) for name in ("total", "executed", "passed")}
        results = root.findall("{*}Results/{*}UnitTestResult")
        if (counts["total"] == 0 or counts["total"] != counts["executed"]
                or counts["executed"] != counts["passed"]
                or len(results) != counts["passed"]
                or any(result.get("outcome") != "Passed" for result in results)):
            raise SystemExit(f"Incomplete or failing tests: {report}: {counters.attrib}")
        print(f"{report}: {counts['passed']} passed, no skipped or failed tests")
        passed += counts["passed"]
    if passed < args.minimum:
        raise SystemExit(f"Expected at least {args.minimum} passing tests, got {passed}")


if __name__ == "__main__":
    main()
