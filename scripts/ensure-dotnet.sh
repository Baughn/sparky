#!/usr/bin/env bash
set -euo pipefail

# Find nearest global.json (current dir -> parents)
find_global_json() {
  local d="$PWD"
  while :; do
    if [ -f "$d/global.json" ]; then echo "$d/global.json"; return 0; fi
    [ "$d" = "/" ] && return 1
    d="$(dirname "$d")"
  done
}

GLOBAL_JSON_PATH="$(find_global_json || true)"
if [ -z "${GLOBAL_JSON_PATH:-}" ]; then
  echo "ensure-dotnet: no global.json found; nothing to enforce."
  exit 0
fi
echo "ensure-dotnet: using $(realpath "$GLOBAL_JSON_PATH")"

# Extract .sdk.version (prefer jq, fallback to python)
extract_version() {
  if command -v jq >/dev/null 2>&1; then
    jq -r '.sdk.version // empty' < "$GLOBAL_JSON_PATH"
  else
    python3 - "$GLOBAL_JSON_PATH" <<'PY'
import json,sys
p=sys.argv[1]
with open(p,'r',encoding='utf-8') as f:
    j=json.load(f)
print(j.get('sdk',{}).get('version',''))
PY
  fi
}
SDK_VERSION="$(extract_version)"
if [ -z "$SDK_VERSION" ] || [ "$SDK_VERSION" = "null" ]; then
  echo "ensure-dotnet: could not read .sdk.version from global.json" >&2
  exit 1
fi
echo "ensure-dotnet: target SDK = $SDK_VERSION"

# If exact version already installed and resolvable, done.
have_exact() {
  command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | awk '{print $1}' | grep -qx "$SDK_VERSION"
}
if have_exact; then
  echo "ensure-dotnet: SDK $SDK_VERSION already present."
  exit 0
fi

# Minimal deps (non-interactive); best-effort
# Check if sudo is functional (may be broken in sandboxed environments like Claude Code web)
can_sudo() {
  command -v sudo >/dev/null 2>&1 && sudo -n true 2>/dev/null
}

if command -v apt-get >/dev/null 2>&1 && can_sudo; then
  sudo DEBIAN_FRONTEND=noninteractive apt-get update -y 2>/dev/null || true
  sudo DEBIAN_FRONTEND=noninteractive apt-get install -y curl ca-certificates tar gzip jq 2>/dev/null || true
elif ! command -v curl >/dev/null 2>&1; then
  echo "ensure-dotnet: curl not found and cannot install (no sudo). Cannot proceed." >&2
  exit 1
fi

# Fetch installer
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$tmp/dotnet-install.sh"
chmod +x "$tmp/dotnet-install.sh"

# Choose install dir: per-user
INSTALL_DIR="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"
mkdir -p "$INSTALL_DIR"

# Install EXACT SDK version
echo "ensure-dotnet: installing $SDK_VERSION to $INSTALL_DIR"
DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
  "$tmp/dotnet-install.sh" \
    --version "$SDK_VERSION" \
    --install-dir "$INSTALL_DIR" \
    --quality ga

# Export for current process and make available to shells
export DOTNET_ROOT="$INSTALL_DIR"
export PATH="$INSTALL_DIR:$INSTALL_DIR/tools:$PATH"

PROFILE_SNIPPET='
# dotnet (installed via dotnet-install.sh)
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
'
grep -q 'DOTNET_ROOT=.*\.dotnet' "${HOME}/.bashrc" 2>/dev/null || echo "$PROFILE_SNIPPET" >> "${HOME}/.bashrc"

# Symlink for non-interactive shells (best-effort, only if sudo works)
if [ -x "$INSTALL_DIR/dotnet" ] && can_sudo; then
  sudo ln -sf "$INSTALL_DIR/dotnet" /usr/local/bin/dotnet 2>/dev/null || true
fi

# Verify exact version now present
if ! dotnet --list-sdks 2>/dev/null | awk '{print $1}' | grep -qx "$SDK_VERSION"; then
  echo "ensure-dotnet: expected SDK $SDK_VERSION not found after install" >&2
  exit 2
fi

echo "ensure-dotnet: SDK $SDK_VERSION ready."
