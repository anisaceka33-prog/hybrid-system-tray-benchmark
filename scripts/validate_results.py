"""Validate final benchmark CSV files without modifying raw observations."""
from __future__ import annotations

import argparse
import csv
import glob
import json
import math
import sys
from collections import Counter
from pathlib import Path

REQUIRED = {
    "run_id", "session_id", "timestamp", "variant", "lifecycle",
    "payload_bytes", "direction", "metric", "value", "unit",
    "cycle_index", "status", "failure_reason", "build_commit",
    "runtime_version",
}
METRICS = {
    "E1": {"fresh_process_tti_ms"},
    "E2": {"working_set_bytes", "private_bytes", "cpu_percent", "process_count", "webview2_process_count"},
    "E3": {"js_to_dotnet_js_rtt_ms", "js_to_dotnet_host_rtt_ms"},
    "E4": {"roundtrip_rtt_ms"},
    "E5": {"payload_rtt_ms", "payload_actual_bytes"},
    "E6": {"lifecycle_reopen_ms", "working_set_bytes", "private_bytes", "process_count", "webview2_process_count"},
}
ROWS_PER_SESSION = {"E1": 1, "E2": 6, "E3": 2, "E4": 1, "E5": 8, "E6": 5}


def expand(patterns: list[str]) -> list[Path]:
    return sorted({Path(item) for pattern in patterns for item in glob.glob(pattern)})


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--experiment", required=True, choices=METRICS)
    parser.add_argument("--files", nargs="+", required=True)
    parser.add_argument("--sessions", type=int, required=True)
    parser.add_argument("--iterations", type=int, default=1)
    parser.add_argument("--variants", nargs="*", default=[])
    parser.add_argument("--lifecycles", nargs="*", default=[])
    parser.add_argument("--manifest", default="results/environment-manifest.json")
    parser.add_argument("--report")
    args = parser.parse_args()

    files = expand(args.files)
    errors: list[str] = []
    rows: list[dict[str, str]] = []
    if not files:
        errors.append("no input files matched")

    for path in files:
        with path.open(newline="", encoding="utf-8-sig") as handle:
            reader = csv.DictReader(handle)
            missing = REQUIRED - set(reader.fieldnames or [])
            if missing:
                errors.append(f"{path}: missing columns {sorted(missing)}")
                continue
            width = len(reader.fieldnames or [])
            for line_number, row in enumerate(reader, 2):
                if None in row or len(row) != width:
                    errors.append(f"{path}:{line_number}: malformed CSV row")
                    continue
                try:
                    value = float(row["value"])
                    int(row["payload_bytes"])
                    int(row["cycle_index"])
                    if not math.isfinite(value):
                        raise ValueError("non-finite")
                except ValueError:
                    errors.append(f"{path}:{line_number}: invalid numeric value")
                rows.append(row)

    manifest_path = Path(args.manifest)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig")) if manifest_path.exists() else {}
    if not manifest:
        errors.append(f"missing environment manifest: {manifest_path}")
    else:
        for key in ("windows", "hardware", "dotnet", "webview2_runtime_version", "build_configuration", "architecture", "git", "captured_at_utc", "power_mode"):
            if key not in manifest:
                errors.append(f"manifest missing {key}")
        if manifest.get("build_configuration") != "Release":
            errors.append("manifest build configuration is not Release")
        if manifest.get("architecture") != "x64":
            errors.append("manifest architecture is not x64")
        if manifest.get("webview2_runtime_version") in (None, "", "unknown"):
            errors.append("manifest WebView2 runtime is unknown")

    commits = {row["build_commit"] for row in rows}
    if "unknown" in commits or "" in commits:
        errors.append("unknown build_commit found")
    manifest_commit = manifest.get("git", {}).get("commit_sha")
    if manifest_commit and commits and commits != {manifest_commit}:
        errors.append(f"CSV commits {sorted(commits)} do not match manifest {manifest_commit}")
    runtimes = {row["runtime_version"] for row in rows}
    if "unknown" in runtimes or "" in runtimes:
        errors.append("unknown runtime_version found")

    metrics = {row["metric"] for row in rows if row["status"] == "ok"}
    missing_metrics = METRICS[args.experiment] - metrics
    if missing_metrics:
        errors.append(f"missing expected metrics {sorted(missing_metrics)}")
    statuses = Counter(row["status"] for row in rows)
    invalid_statuses = set(statuses) - {"ok", "failed"}
    if invalid_statuses:
        errors.append(f"invalid status values {sorted(invalid_statuses)}")

    variants = {row["variant"] for row in rows}
    lifecycles = {row["lifecycle"] for row in rows}
    if args.variants and not set(args.variants).issubset(variants):
        errors.append(f"missing variants {sorted(set(args.variants) - variants)}")
    if args.lifecycles and not set(args.lifecycles).issubset(lifecycles):
        errors.append(f"missing lifecycles {sorted(set(args.lifecycles) - lifecycles)}")

    multiplier = args.iterations if args.experiment in {"E3", "E4", "E5", "E6"} else 1
    condition_count = max(1, len(args.variants) or len(args.lifecycles))
    expected_rows = args.sessions * ROWS_PER_SESSION[args.experiment] * multiplier * condition_count
    if len(rows) != expected_rows:
        errors.append(f"row count mismatch: expected {expected_rows}, found {len(rows)}")

    report = {
        "experiment": args.experiment,
        "files": [str(path) for path in files],
        "rows": len(rows),
        "expected_rows": expected_rows,
        "successful": statuses["ok"],
        "failed": statuses["failed"],
        "metrics": sorted(metrics),
        "commits": sorted(commits),
        "runtime_versions": sorted(runtimes),
        "errors": errors,
        "passed": not errors,
    }
    report_path = Path(args.report or f"results/processed/quality_{args.experiment.lower()}.json")
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0 if not errors else 1


if __name__ == "__main__":
    sys.exit(main())
