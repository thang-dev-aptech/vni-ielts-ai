#!/usr/bin/env bash
#
# Thin wrapper so `bash scripts/bootstrap.sh` still works on macOS/Linux.
# pnpm bootstrap calls scripts/bootstrap.mjs directly (Windows `bash` is
# often the WSL stub with no /bin/bash).
#
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
exec node "$ROOT/scripts/bootstrap.mjs"
