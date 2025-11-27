#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project="$root_dir/Sparky.Benchmarks/Sparky.Benchmarks.Runner.csproj"
results_dir="$root_dir/BenchmarkDotNet.Artifacts/results"
default_csv="$(ls -t "$results_dir"/*-report.csv 2>/dev/null | head -n1 || true)"

usage() {
  cat <<'USAGE'
Usage:
  ./benchmark.sh run                     # run benchmarks
  ./benchmark.sh compare <base> [new]    # compare two BenchmarkDotNet CSV reports
  ./benchmark.sh trailer                 # benchmark current + parent commits and add a commit trailer

If [new] is omitted, the latest CSV in BenchmarkDotNet.Artifacts/results is used.
USAGE
}

command="${1:-trailer}"

case "$command" in
  run)
    dotnet run -c Release --project "$project" --filter '*'
    latest_csv="$(ls -t "$results_dir"/*-report.csv 2>/dev/null | head -n1 || true)"
    if [[ -n "$latest_csv" ]]; then
      echo "Latest BenchmarkDotNet CSV: $latest_csv"
    else
      echo "No BenchmarkDotNet CSV found in $results_dir"
    fi
    ;;
  compare)
    base_csv="${2:-}"
    new_csv="${3:-$default_csv}"
    if [[ -z "$base_csv" ]]; then
      usage
      exit 1
    fi
    python3 "$root_dir/tools/compare_benchmarks.py" "$base_csv" "$new_csv"
    ;;
  trailer)
    python3 "$root_dir/tools/jj_benchmark_trailer.py"
    ;;
  *)
    usage
    exit 1
    ;;
esac
