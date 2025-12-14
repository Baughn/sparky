#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

declare -A repos=(
    ["vsapi"]="https://github.com/anegostudios/vsapi.git"
    ["vsessentialsmod"]="https://github.com/anegostudios/vsessentialsmod.git"
    ["vscreativemod"]="https://github.com/anegostudios/vscreativemod.git"
    ["vssurvivalmod"]="https://github.com/anegostudios/vssurvivalmod.git"
)

for dir in "${!repos[@]}"; do
    url="${repos[$dir]}"
    if [ -d "$dir/.git" ]; then
        echo "Updating $dir..."
        git -C "$dir" pull
    else
        echo "Cloning $dir..."
        git clone "$url" "$dir"
    fi
done

echo "Done."
