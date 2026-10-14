#!/usr/bin/env bash
# Builds Oriel.app. Usage: scripts/build.sh [Debug|Release]  -> build/Build/Products/<config>/Oriel.app
set -euo pipefail
cd "$(dirname "$0")/.."

config="${1:-Debug}"
scripts/configure.sh
xcodebuild -project Oriel.xcodeproj -scheme Oriel -configuration "$config" \
  -derivedDataPath build -destination 'generic/platform=macOS' -quiet build
echo "Built build/Build/Products/$config/Oriel.app"
