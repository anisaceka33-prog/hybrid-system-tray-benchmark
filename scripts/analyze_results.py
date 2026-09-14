"""Session-aware analysis for final benchmark data; raw CSVs are never modified."""
from __future__ import annotations

import argparse
import csv
import glob
import json
import math
import statistics
from collections import defaultdict
from pathlib import Path

import matplotlib.pyplot as plt
from scipy import stats

REQUIRED = {
    "run_id", "session_id", "timestamp", "variant", "lifecycle",
    "payload_bytes", "direction", "metric", "value", "unit",
    "cycle_index", "status", "failure_reason", "build_commit",
    "runtime_version",
}


def experiment_from_path(path: Path) -> str:
    name = path.name.lower()
    for experiment in ("e1", "e2", "e3", "e4", "e5", "e6"):
        if name.startswith(f"final_{experiment}_"):
            return experiment.upper()
    return "unknown"


def load_rows(patterns: list[str]) -> tuple[list[dict], list[dict]]:
    rows, errors = [], []
    paths = sorted({Path(item) for pattern in patterns for item in glob.glob(pattern)})
    for path in paths:
        with path.open(newline="", encoding="utf-8-sig") as handle:
            reader = csv.DictReader(handle)
            missing = REQUIRED - set(reader.fieldnames or [])
            if missing:
                errors.append({"file": str(path), "error": "missing columns", "columns": sorted(missing)})
                continue
            for line_number, row in enumerate(reader, 2):
                try:
                    row["value"] = float(row["value"])
                    row["payload_bytes"] = int(row["payload_bytes"])
                    row["cycle_index"] = int(row["cycle_index"])
                    if not math.isfinite(row["value"]):
                        raise ValueError("non-finite")
                except ValueError:
                    errors.append({"file": str(path), "line": line_number, "error": "invalid numeric value"})
                    continue
                row["experiment"] = experiment_from_path(path)
                row["source_file"] = str(path)
                rows.append(row)
    if not paths:
        errors.append({"error": "no input files matched"})
    return rows, errors


def percentile(values: list[float], probability: float) -> float:
    if len(values) == 1:
        return values[0]
    position = (len(values) - 1) * probability
    lower, upper = math.floor(position), math.ceil(position)
    return values[lower] if lower == upper else values[lower] + (values[upper] - values[lower]) * (position - lower)


def describe(values: list[float]) -> dict:
    ordered = sorted(values)
    count = len(ordered)
    mean = statistics.fmean(ordered)
    standard_deviation = statistics.stdev(ordered) if count > 1 else 0.0
    margin = stats.t.ppf(0.975, count - 1) * standard_deviation / math.sqrt(count) if count > 1 else 0.0
    return {
        "n": count, "mean": mean, "median": statistics.median(ordered),
        "stddev": standard_deviation,
        "iqr": percentile(ordered, 0.75) - percentile(ordered, 0.25),
        "min": ordered[0], "max": ordered[-1],
        "ci95_low": mean - margin, "ci95_high": mean + margin,
    }


def make_session_means(rows: list[dict]) -> list[dict]:
    grouped = defaultdict(list)
    for row in rows:
        if row["status"] == "ok":
            key = (row["experiment"], row["variant"], row["lifecycle"], row["metric"], row["payload_bytes"], row["session_id"])
            grouped[key].append(row["value"])
    return [{
        "experiment": key[0], "variant": key[1], "lifecycle": key[2],
        "metric": key[3], "payload_bytes": key[4], "session_id": key[5],
        "value": statistics.fmean(values), "observations": len(values),
    } for key, values in grouped.items()]


def make_summary(rows: list[dict], sessions: list[dict]) -> list[dict]:
    observations, session_values = defaultdict(list), defaultdict(list)
    for row in rows:
        if row["status"] == "ok":
            key = (row["experiment"], row["variant"], row["lifecycle"], row["metric"], row["payload_bytes"])
            observations[key].append(row["value"])
    for row in sessions:
        key = (row["experiment"], row["variant"], row["lifecycle"], row["metric"], row["payload_bytes"])
        session_values[key].append(row["value"])
    output = []
    for key, values in sorted(observations.items()):
        item = {
            "experiment": key[0], "variant": key[1], "lifecycle": key[2],
            "metric": key[3], "payload_bytes": key[4],
            "n_observations": len(values), "n_sessions": len(session_values[key]),
        }
        item.update(describe(values))
        output.append(item)
    return output


def make_comparisons(sessions: list[dict]) -> list[dict]:
    groups, comparisons = defaultdict(list), []
    for row in sessions:
        key = (row["experiment"], row["metric"], row["payload_bytes"], row["variant"], row["lifecycle"])
        groups[key].append(row["value"])
    by_metric = defaultdict(list)
    for key, values in groups.items():
        by_metric[key[:3]].append((key, values))
    for base, conditions in sorted(by_metric.items()):
        if len(conditions) != 2:
            continue
        (left_key, left), (right_key, right) = sorted(conditions)
        pooled = math.sqrt(((len(left) - 1) * statistics.variance(left) + (len(right) - 1) * statistics.variance(right)) / (len(left) + len(right) - 2)) if len(left) > 1 and len(right) > 1 else 0.0
        comparisons.append({
            "experiment": base[0], "metric": base[1], "payload_bytes": base[2],
            "condition_a": f"{left_key[3]}:{left_key[4]}",
            "condition_b": f"{right_key[3]}:{right_key[4]}",
            "n_a": len(left), "n_b": len(right),
            "cohens_d_b_minus_a": (statistics.fmean(right) - statistics.fmean(left)) / pooled if pooled else 0.0,
            "welch_t_p": stats.ttest_ind(left, right, equal_var=False).pvalue if len(left) > 1 and len(right) > 1 else None,
            "mann_whitney_p": stats.mannwhitneyu(left, right, alternative="two-sided").pvalue,
            "shapiro_a_p": stats.shapiro(left).pvalue if 3 <= len(left) <= 5000 else None,
            "shapiro_b_p": stats.shapiro(right).pvalue if 3 <= len(right) <= 5000 else None,
        })
    return comparisons


def write_csv(path: Path, rows: list[dict]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if not rows:
        path.write_text("", encoding="utf-8")
        return
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def subset(summary: list[dict], experiment: str, metrics: set[str]) -> list[dict]:
    return [row for row in summary if row["experiment"] == experiment and row["metric"] in metrics]


def boxplot(axis, sessions: list[dict], experiment: str, metric: str, title: str, label_field: str) -> None:
    grouped = defaultdict(list)
    for row in sessions:
        if row["experiment"] == experiment and row["metric"] == metric:
            grouped[str(row[label_field])].append(row["value"])
    if grouped:
        labels = sorted(grouped)
        axis.boxplot([grouped[label] for label in labels], tick_labels=labels, showmeans=True)
    axis.set_title(title)
    axis.set_ylabel("ms" if metric.endswith("_ms") else "bytes")
    axis.grid(axis="y", alpha=0.25)


def make_figures(sessions: list[dict], output: Path) -> None:
    output.mkdir(parents=True, exist_ok=True)
    plt.style.use("seaborn-v0_8-whitegrid")
    figure, axis = plt.subplots(figsize=(7, 4.5))
    boxplot(axis, sessions, "E1", "fresh_process_tti_ms", "Figure 7. Fresh-process TTI", "variant")
    figure.tight_layout(); figure.savefig(output / "figure_7_startup.png", dpi=180); plt.close(figure)

    figure, axes = plt.subplots(1, 2, figsize=(11, 4.5))
    boxplot(axes[0], sessions, "E2", "working_set_bytes", "Working set", "variant")
    boxplot(axes[1], sessions, "E2", "private_bytes", "Private memory", "variant")
    figure.suptitle("Figure 8. Memory footprint"); figure.tight_layout(); figure.savefig(output / "figure_8_memory.png", dpi=180); plt.close(figure)

    figure, axes = plt.subplots(1, 2, figsize=(11, 4.5))
    boxplot(axes[0], sessions, "E4", "roundtrip_rtt_ms", "Round-trip RTT", "variant")
    payload_groups = defaultdict(list)
    for row in sessions:
        if row["experiment"] == "E5" and row["metric"] == "payload_rtt_ms":
            payload_groups[row["payload_bytes"]].append(row["value"])
    if payload_groups:
        sizes = sorted(payload_groups)
        axes[1].plot(sizes, [statistics.fmean(payload_groups[size]) for size in sizes], marker="o")
        axes[1].set_xscale("log", base=10)
    axes[1].set_title("Payload scaling"); axes[1].set_xlabel("Actual payload bytes"); axes[1].set_ylabel("ms")
    figure.suptitle("Figure 9. Bridge latency"); figure.tight_layout(); figure.savefig(output / "figure_9_bridge.png", dpi=180); plt.close(figure)

    figure, axis = plt.subplots(figsize=(7, 4.5))
    boxplot(axis, sessions, "E6", "lifecycle_reopen_ms", "Figure 10. Lifecycle reopen latency", "lifecycle")
    figure.tight_layout(); figure.savefig(output / "figure_10_lifecycle.png", dpi=180); plt.close(figure)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("path", nargs="+", default=["results/raw/final_e*.csv"])
    parser.add_argument("--out", default="results/processed")
    parser.add_argument("--figures", default="results/figures")
    args = parser.parse_args()
    rows, errors = load_rows(args.path)
    output = Path(args.out)
    output.mkdir(parents=True, exist_ok=True)
    sessions = make_session_means(rows)
    summary = make_summary(rows, sessions)
    comparisons = make_comparisons(sessions)
    failures = [row for row in rows if row["status"] != "ok"]
    validation = {"rows": len(rows), "failures": len(failures), "errors": errors, "files": sorted({row["source_file"] for row in rows})}
    (output / "validation.json").write_text(json.dumps(validation, indent=2), encoding="utf-8")
    (output / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    write_csv(output / "summary.csv", summary)
    write_csv(output / "session_means.csv", sessions)
    write_csv(output / "comparisons.csv", comparisons)
    write_csv(output / "table_4_startup.csv", subset(summary, "E1", {"fresh_process_tti_ms"}))
    write_csv(output / "table_5_memory.csv", subset(summary, "E2", {"working_set_bytes", "private_bytes"}))
    write_csv(output / "table_6_cpu.csv", subset(summary, "E2", {"cpu_percent"}))
    write_csv(output / "table_7_topology.csv", subset(summary, "E2", {"process_count", "webview2_process_count"}))
    write_csv(output / "table_8_bridge.csv", subset(summary, "E3", {"js_to_dotnet_js_rtt_ms", "js_to_dotnet_host_rtt_ms"}) + subset(summary, "E4", {"roundtrip_rtt_ms"}))
    write_csv(output / "table_9_lifecycle.csv", subset(summary, "E6", {"lifecycle_reopen_ms", "working_set_bytes", "private_bytes", "process_count", "webview2_process_count"}))
    make_figures(sessions, Path(args.figures))
    print(json.dumps({"rows": len(rows), "failures": len(failures), "validation_errors": len(errors), "groups": len(summary), "session_groups": len(sessions)}, indent=2))
    return 0 if not errors else 1


if __name__ == "__main__":
    raise SystemExit(main())
