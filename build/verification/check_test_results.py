"""Reject empty, skipped or failed TRX runs, including an empty NonUI filter."""

import argparse
from pathlib import Path
import defusedxml.ElementTree as ET


def passing_count(report):
    root = ET.parse(report, forbid_dtd=True, forbid_entities=True, forbid_external=True).getroot()
    summary = root.find("{*}ResultSummary")
    counters = root.find("{*}ResultSummary/{*}Counters")
    if summary is None or summary.get("outcome") not in ("Completed", "Passed") or counters is None:
        raise SystemExit(f"Missing or unsuccessful test summary: {report}")
    counts = {name: int(value) for name, value in counters.attrib.items()}
    required = {"total", "executed", "passed"}
    if not required.issubset(counts):
        raise SystemExit(f"Missing test counters: {report}")
    if any(count != 0 for name, count in counts.items() if name not in required):
        raise SystemExit(f"Non-passing test counters: {report}: {counts}")
    if counts["total"] <= 0 or counts["total"] != counts["executed"] or counts["total"] != counts["passed"]:
        raise SystemExit(f"Incomplete test counts: {report}: {counts}")
    results = root.findall("{*}Results/{*}UnitTestResult")
    if len(results) != counts["total"] or any(result.get("outcome") != "Passed" for result in results):
        raise SystemExit(f"Missing or non-passing test results: {report}")
    print(f"{report}: {counts['passed']} passed, no skipped or failed tests")
    return counts["passed"]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    parser.add_argument("--minimum", type=int, required=True)
    args = parser.parse_args()
    if args.minimum <= 0:
        parser.error("--minimum must be positive")
    reports = list(args.directory.rglob("*.trx"))
    if not reports:
        raise SystemExit(f"No TRX reports in {args.directory}")
    passed = sum(passing_count(report) for report in reports)
    if passed < args.minimum:
        raise SystemExit(f"Expected at least {args.minimum} passing tests, got {passed}")


if __name__ == "__main__":
    main()
