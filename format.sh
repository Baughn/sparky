#!/usr/bin/env bash
# Format all C# files in the project
set -euo pipefail

cd "$(dirname "$0")"

echo "Formatting C# files..."
dotnet format Sparky.sln
