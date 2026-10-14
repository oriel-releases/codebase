#!/usr/bin/env bash
# Builds the app and prints only errors and warnings (one line each), for a quick check.
set -uo pipefail
cd "$(dirname "$0")/.."
scripts/configure.sh >/dev/null
xcodebuild -project Oriel.xcodeproj -scheme Oriel -configuration Debug -derivedDataPath build \
  -destination 'generic/platform=macOS' build 2>&1 | grep -E "^/.*(error|warning): " | sed -E 's|^/Users/[^ ]*/oriel-macos/||' | cut -c1-260 | sort -u
echo "exit ${PIPESTATUS[0]}"
