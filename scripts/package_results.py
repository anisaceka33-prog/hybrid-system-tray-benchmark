"""Create a reproducibility ZIP after all final quality checks pass."""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
import zipfile
from datetime import datetime, timezone
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--results", default="results")
    parser.add_argument("--output")
    args = parser.parse_args()
    root = Path(args.results)
    required = [root / "environment-manifest.json"]
    required += [root / "processed" / f"quality_e{number}.json" for number in range(1, 7)]
    errors = []
    for path in required:
        if not path.exists():
            errors.append(f"missing {path}")
        elif path.name.startswith("quality_") and not json.loads(path.read_text(encoding="utf-8-sig")).get("passed"):
            errors.append(f"quality check failed: {path}")
    for experiment in range(1, 7):
        if not list((root / "raw").glob(f"final_e{experiment}_*.csv")):
            errors.append(f"missing final E{experiment} raw CSV")
    for figure in range(7, 11):
        if not list((root / "figures").glob(f"figure_{figure}_*.png")):
            errors.append(f"missing Figure {figure}")
    if errors:
        print(json.dumps({"packaged": False, "errors": errors}, indent=2))
        return 1

    processed = root / "processed"
    processed.mkdir(parents=True, exist_ok=True)
    patch_path = processed / "source_changes.patch"
    patch = subprocess.run(["git", "diff", "--binary", "HEAD"], check=True, capture_output=True).stdout
    patch_path.write_bytes(patch)
    status_path = processed / "git-status.txt"
    status_path.write_bytes(subprocess.run(["git", "status", "--short"], check=True, capture_output=True).stdout)

    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    output = Path(args.output or root / f"final-benchmark-package_{stamp}.zip")
    include = [
        root / "environment-manifest.json", root / "raw", processed,
        root / "figures", Path("scripts"), Path("docs"), Path("benchmarks"),
        Path("src"), Path("tests"), Path("WebView2SystemTrayBenchmark.sln"),
    ]
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for item in include:
            if item.is_file():
                archive.write(item, item.as_posix())
            elif item.exists():
                for path in sorted(item.rglob("*")):
                    if path.is_file() and not {"bin", "obj", "__pycache__"}.intersection(path.parts):
                        archive.write(path, path.as_posix())
    print(output)
    return 0


if __name__ == "__main__":
    sys.exit(main())
