#!/usr/bin/env python3
"""
Compare two BenchmarkDotNet CSV reports and print mean + allocation deltas.
Usage: python3 tools/compare_benchmarks.py base.csv new.csv
"""
from __future__ import annotations

import csv
import math
import pathlib
import re
import sys
from dataclasses import dataclass
from typing import Dict, Optional

Duration = Optional[float]
Bytes = Optional[float]


@dataclass
class BenchmarkResult:
    mean_seconds: Duration
    stddev_seconds: Duration
    allocated_bytes: Bytes


DURATION_UNITS = {
    "ns": 1e-9,
    "us": 1e-6,
    "ms": 1e-3,
    "s": 1.0,
}


def parse_duration(value: str) -> Duration:
    value = value.strip()
    if not value:
        return None
    cleaned = (
        value.replace(",", "")
        .replace("\u00b5", "u")  # micro sign
        .replace("\u03bc", "u")  # Greek mu
    )
    match = re.match(r"([-+]?\d*\.?\d+)\s*(ns|us|ms|s)", cleaned, re.IGNORECASE)
    if not match:
        return None
    amount, unit = match.groups()
    return float(amount) * DURATION_UNITS[unit.lower()]


def parse_bytes(value: str) -> Bytes:
    value = value.strip()
    if not value:
        return None
    cleaned = value.replace(",", "")
    match = re.match(r"([-+]?\d*\.?\d+)\s*(B|KB|MB)", cleaned, re.IGNORECASE)
    if not match:
        return None
    amount, unit = match.groups()
    factor = {"b": 1, "kb": 1024, "mb": 1024 * 1024}[unit.lower()]
    return float(amount) * factor


def read_results(path: pathlib.Path) -> Dict[str, BenchmarkResult]:
    with path.open(newline="", encoding="utf-8") as handle:
        reader = csv.DictReader(handle)
        results: Dict[str, BenchmarkResult] = {}
        for row in reader:
            method = row.get("Method", "").strip().strip("'\"")
            mean = parse_duration(row.get("Mean", ""))
            stddev = parse_duration(row.get("StdDev", ""))
            alloc = parse_bytes(row.get("Allocated", ""))
            results[method] = BenchmarkResult(mean_seconds=mean, stddev_seconds=stddev, allocated_bytes=alloc)
        return results


def format_duration(seconds: Duration) -> str:
    if seconds is None or math.isnan(seconds):
        return "n/a"
    abs_val = abs(seconds)
    if abs_val < 1e-6:
        value, unit = seconds * 1e9, "ns"
    elif abs_val < 1e-3:
        value, unit = seconds * 1e6, "us"
    elif abs_val < 1:
        value, unit = seconds * 1e3, "ms"
    else:
        value, unit = seconds, "s"
    return f"{value:.3f} {unit}"


def format_bytes(value: Bytes) -> str:
    if value is None or math.isnan(value):
        return "n/a"
    abs_val = abs(value)
    if abs_val < 1024:
        return f"{value:.0f} B"
    elif abs_val < 1024 * 1024:
        return f"{value / 1024:.1f} KB"
    return f"{value / 1024 / 1024:.2f} MB"


def pct_delta(old: Duration, new: Duration) -> str:
    if old is None or new is None or old == 0:
        return "n/a"
    delta = (new - old) / old * 100
    return f"{delta:+.1f}%"


def build_table(base: Dict[str, BenchmarkResult], new: Dict[str, BenchmarkResult]) -> str:
    headers = [
        "Benchmark",
        "Base Mean",
        "New Mean",
        "Mean Delta",
        "Base Alloc",
        "New Alloc",
        "Alloc Delta",
    ]
    rows = [headers]
    for name in sorted(set(base) | set(new)):
        base_result = base.get(name)
        new_result = new.get(name)
        base_mean = base_result.mean_seconds if base_result else None
        new_mean = new_result.mean_seconds if new_result else None
        base_alloc = base_result.allocated_bytes if base_result else None
        new_alloc = new_result.allocated_bytes if new_result else None
        rows.append(
            [
                name or "(unknown)",
                format_duration(base_mean),
                format_duration(new_mean),
                pct_delta(base_mean, new_mean),
                format_bytes(base_alloc),
                format_bytes(new_alloc),
                pct_delta(base_alloc, new_alloc),
            ]
        )
    widths = [max(len(row[i]) for row in rows) for i in range(len(headers))]
    lines = []
    for idx, row in enumerate(rows):
        padded = [cell.ljust(widths[i]) for i, cell in enumerate(row)]
        line = "  ".join(padded)
        lines.append(line)
        if idx == 0:
            lines.append("-" * len(line))
    return "\n".join(lines)


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__.strip())
        return 1
    base_path = pathlib.Path(sys.argv[1]).expanduser()
    new_path = pathlib.Path(sys.argv[2]).expanduser()
    if not base_path.is_file():
        print(f"Base file not found: {base_path}")
        return 1
    if not new_path.is_file():
        print(f"New file not found: {new_path}")
        return 1

    base_results = read_results(base_path)
    new_results = read_results(new_path)
    print(build_table(base_results, new_results))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
