"""Execute production job bodies with portable data and strict desktop substitutes."""

import argparse
import json
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--probe", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    probe = args.probe.resolve(strict=True)
    with tempfile.TemporaryDirectory(prefix="syncclipboard-quartz-jobs-") as temporary:
        root = Path(temporary).resolve()
        executable = root / "probe"
        shutil.copytree(probe.parent, executable)
        config = {"Env": {"PortableAppDataFolder": True, "PortableUserConfig": True}}
        (executable / "StaticConfig.json").write_text(json.dumps(config), encoding="utf-8")
        report = root / "result.json"
        subprocess.run(["dotnet", str(executable / probe.name), str(root), str(report)],
                       cwd=root, check=True, timeout=120)
        data = json.loads(report.read_text(encoding="utf-8"))
        if data.get("passed") is not True or len(data.get("checks", [])) != 10:
            raise RuntimeError("Incomplete Quartz production-job verification")
        if (data.get("canceledJobs") != 6 or data.get("uiCallbacksExecuted") != 0
                or data.get("historyCancellationWhileWaiting") is not True):
            raise RuntimeError("Cancellation or UI isolation verification failed")
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(data, indent=2), encoding="utf-8")
    print("Quartz production jobs: 10 groups passed, six cancellation entries, no UI execution.")


if __name__ == "__main__":
    main()
