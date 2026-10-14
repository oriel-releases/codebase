#!/usr/bin/env bash
# Builds a Release Oriel.app and wraps it in a drag-to-Applications DMG -> releases/Oriel-<version>.dmg
# Signed ad hoc for now: users approve the first launch in System Settings > Privacy & Security > Open Anyway.
set -euo pipefail
cd "$(dirname "$0")/.."

scripts/build.sh Release
app="build/Build/Products/Release/Oriel.app"
version="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$app/Contents/Info.plist")"

codesign --force --deep --sign - "$app"

stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
cp -R "$app" "$stage/"
ln -s /Applications "$stage/Applications"

mkdir -p releases
dmg="releases/Oriel-$version.dmg"
rm -f "$dmg"
hdiutil create -volname "Oriel" -srcfolder "$stage" -ov -format UDZO -quiet "$dmg"
echo "Packaged $dmg ($(du -h "$dmg" | cut -f1))"
