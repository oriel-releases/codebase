#!/usr/bin/env bash
# Runs the OrielKit unit tests (no UI, no app bundle needed).
set -euo pipefail
cd "$(dirname "$0")/../OrielKit"
swift test "$@"
