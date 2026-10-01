"""Reject empty or skipped suites: platform CI must execute every selected test."""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

if len(sys.argv) < 2:
    sys.exit('Pass the expected TRX report paths.')

for argument in sys.argv[1:]:
    report = Path(argument)
    if not report.is_file():
        sys.exit(f'{report}: expected TRX report was not produced.')
    root = ET.parse(report).getroot()
    counters = root.find('.//{*}Counters')
    if counters is None or int(counters.get('total', '0')) == 0:
        sys.exit(f'{report}: no tests selected.')
    if counters.get('passed') != counters.get('total'):
        sys.exit(f'{report}: some selected tests failed or were skipped: {counters.attrib}')
    print(f'{report}: {counters.get("passed")} tests passed, none skipped.')
