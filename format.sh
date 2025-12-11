#!/usr/bin/env bash
# Format all C# files in the project using CSharpier
set -euo pipefail

cd "$(dirname "$0")"

if [[ "${1:-}" == "--check" ]]; then
    echo "Checking C# formatting..."
    csharpier check .
else
    echo "Formatting C# files..."
    csharpier format .
fi
