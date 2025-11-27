#!/usr/bin/env python3
"""
Run benchmarks on the current change and its parent, compare results, and
append a Benchmark trailer to the current commit message.

Workflow:
1) Benchmark current change.
2) `jj new @-` to edit the parent in the working copy, benchmark that state.
3) `jj undo` to return to the original change.
4) Compare results and append a trailer to @.
"""
from __future__ import annotations

import pathlib
import shutil
import subprocess
import sys
import tempfile
import re
from typing import Dict, Iterable, Optional, Tuple

ROOT = pathlib.Path(__file__).resolve().parent.parent
BENCH_SCRIPT = ROOT / "benchmark.sh"
DEFAULT_CSV = ROOT / "BenchmarkDotNet.Artifacts/results/Sparky.Benchmarks.CircuitBenchmarks-report.csv"
TRAILER_KEY = "Benchmark"

# Allow importing helpers without making tools a package.
sys.path.insert(0, str(ROOT / "tools"))
import compare_benchmarks  # type: ignore  # noqa: E402


def run(cmd: Iterable[str], *, capture: bool = True) -> subprocess.CompletedProcess:
    """Run a command in the repo root."""
    result = subprocess.run(
        list(cmd),
        cwd=ROOT,
        text=True,
        capture_output=capture,
        check=False,
    )
    if result.returncode != 0:
        stderr = result.stderr.strip()
        stdout = result.stdout.strip()
        details = f"\nstdout:\n{stdout}\n\nstderr:\n{stderr}" if capture else ""
        raise RuntimeError(f"Command failed ({result.returncode}): {' '.join(cmd)}{details}")
    return result


def run_benchmark(label: str, tmpdir: pathlib.Path) -> pathlib.Path:
    """Run the benchmark suite and copy the CSV to a temp file."""
    print(f"Running benchmarks for {label}...")
    run([str(BENCH_SCRIPT), "run"], capture=False)
    if not DEFAULT_CSV.exists():
        raise RuntimeError(f"Benchmark output not found at {DEFAULT_CSV}")
    dest = tmpdir / f"{label}.csv"
    shutil.copy(DEFAULT_CSV, dest)
    print(f"Saved {label} results to {dest}")
    return dest


def checkout_parent_and_benchmark(tmpdir: pathlib.Path) -> pathlib.Path:
    """Temporarily check out the parent change, run benchmarks, then undo."""
    run(["jj", "new", "@-"], capture=False)
    try:
        return run_benchmark("parent", tmpdir)
    finally:
        # Always return to the original working copy state.
        print("Restoring original working copy (jj undo)...")
        run(["jj", "undo"], capture=False)


def format_pct(delta: Optional[float]) -> str:
    if delta is None:
        return "n/a"
    return f"{delta:+.1f}%"


def compute_mean_delta(old: Optional[compare_benchmarks.BenchmarkResult], new: Optional[compare_benchmarks.BenchmarkResult]) -> Optional[float]:
    if not old or not new or old.mean_seconds is None or new.mean_seconds is None or old.mean_seconds == 0:
        return None
    return (new.mean_seconds - old.mean_seconds) / old.mean_seconds * 100


def summarize(base: Dict[str, compare_benchmarks.BenchmarkResult], new: Dict[str, compare_benchmarks.BenchmarkResult]) -> str:
    parts = []
    for name in sorted(set(base) | set(new)):
        base_res = base.get(name)
        new_res = new.get(name)
        delta = compute_mean_delta(base_res, new_res)
        base_dur = compare_benchmarks.format_duration(base_res.mean_seconds if base_res else None)
        new_dur = compare_benchmarks.format_duration(new_res.mean_seconds if new_res else None)
        parts.append(f"{name}: {format_pct(delta)} ({base_dur} -> {new_dur})")
    return "\n  ".join(parts)


def update_trailer(existing: str, key: str, value: str) -> str:
    """Append or replace a trailer line with the given key."""
    lines = existing.rstrip().splitlines()
    trailer_pattern = re.compile(r"^[A-Za-z0-9][A-Za-z0-9-]*:")
    trailer_start = len(lines)
    while trailer_start > 0 and trailer_pattern.match(lines[trailer_start - 1].strip()):
        trailer_start -= 1

    body = lines[:trailer_start]
    trailers = lines[trailer_start:]

    trailers = [line for line in trailers if not line.lower().startswith(f"{key.lower()}:")]
    trailers.append(f"{key}: {value}")

    body_text = "\n".join(body).rstrip()
    trailer_text = "\n".join(trailers).rstrip()

    if body_text and trailer_text:
        return f"{body_text}\n\n{trailer_text}\n"
    if trailer_text:
        return f"{trailer_text}\n"
    return f"{body_text}\n"


def append_trailer(table: str, base_results: Dict[str, compare_benchmarks.BenchmarkResult], new_results: Dict[str, compare_benchmarks.BenchmarkResult]) -> None:
    summary_line = summarize(base_results, new_results)
    current_desc = run(["jj", "log", "-r", "@", "-G", "-T", "description"]).stdout.rstrip("\n")
    new_desc = update_trailer(current_desc, TRAILER_KEY, summary_line)
    print("\nBenchmark comparison:\n")
    print(table)
    print("\nUpdating current change description with trailer...")
    run(["jj", "describe", "-m", new_desc], capture=False)
    print(f"Added trailer: {TRAILER_KEY}: {summary_line}")


def main() -> int:
    with tempfile.TemporaryDirectory(prefix="jj-bench-") as tmp:
        tmpdir = pathlib.Path(tmp)
        current_csv = run_benchmark("current", tmpdir)
        parent_csv = checkout_parent_and_benchmark(tmpdir)
        # Put the current results back in place so the repo reflects the current commit.
        shutil.copy(current_csv, DEFAULT_CSV)

        base_results = compare_benchmarks.read_results(parent_csv)
        new_results = compare_benchmarks.read_results(current_csv)
        table = compare_benchmarks.build_table(base_results, new_results)
        append_trailer(table, base_results, new_results)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
