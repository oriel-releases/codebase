#!/usr/bin/env bash
# Builds a release and (unless --no-upload) publishes it to github.com/oriel-releases/macos as v<version>:
#   Oriel.dmg     drag-to-Applications disk image (what the website links)
#   Oriel.zip     the app for Sparkle updates
#   appcast.xml   the update feed, signed with Sparkle's EdDSA key (kept in this Mac's Keychain by generate_keys)
#
# Usage: GITHUB_TOKEN=... scripts/release.sh [--no-upload]
# Before: bump MARKETING_VERSION and CURRENT_PROJECT_VERSION in project.yml.
set -euo pipefail
cd "$(dirname "$0")/.."

upload=1
[[ "${1:-}" == "--no-upload" ]] && upload=0

version="$(grep -E '^\s*MARKETING_VERSION:' project.yml | head -1 | sed -E 's/.*"(.*)".*/\1/')"
build_number="$(grep -E '^\s*CURRENT_PROJECT_VERSION:' project.yml | head -1 | sed -E 's/.*"(.*)".*/\1/')"
repo="oriel-releases/macos"
sparkle_bin="build/SourcePackages/artifacts/sparkle/Sparkle/bin"

scripts/configure.sh
grep -q 'UNSPLASH_ACCESS_KEY = .\+' Config/Secrets.xcconfig || { echo "error: no Unsplash key (UNSPLASH_ACCESS_KEY or .env)" >&2; exit 1; }
grep -q 'SPARKLE_PUBLIC_KEY = .\+' Config/Secrets.xcconfig || { echo "error: no SPARKLE_PUBLIC_KEY in .env (run $sparkle_bin/generate_keys)" >&2; exit 1; }

echo "Building Oriel $version ($build_number)…"
xcodebuild -project Oriel.xcodeproj -scheme Oriel -configuration Release -derivedDataPath build \
  -destination 'generic/platform=macOS' ONLY_ACTIVE_ARCH=NO -quiet build
app="build/Build/Products/Release/Oriel.app"
# Ad-hoc signature: required on Apple silicon. Not notarized (no paid Apple Developer account).
codesign --force --deep --sign - "$app"

out="releases/$version"
rm -rf "$out"
mkdir -p "$out"
ditto -c -k --keepParent "$app" "$out/Oriel.zip"

stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
cp -R "$app" "$stage/"
ln -s /Applications "$stage/Applications"
hdiutil create -volname "Oriel" -srcfolder "$stage" -ov -format UDZO -quiet "$out/Oriel.dmg"

# The feed: generate_appcast signs the zip with the private key from the Keychain.
"$sparkle_bin/generate_appcast" --download-url-prefix "https://github.com/$repo/releases/download/v$version/" \
  -o "$out/appcast.xml" "$out" >/dev/null
echo "Built $out: $(ls "$out" | tr '\n' ' ')"

if [[ $upload == 1 ]]; then
  : "${GITHUB_TOKEN:?set GITHUB_TOKEN (Contents: read and write on $repo)}"
  command -v gh >/dev/null || { echo "error: install the GitHub CLI: brew install gh" >&2; exit 1; }
  GH_TOKEN="$GITHUB_TOKEN" gh release create "v$version" --repo "$repo" --title "Oriel $version" \
    --notes "Oriel for macOS $version" "$out/Oriel.dmg" "$out/Oriel.zip" "$out/appcast.xml"
  echo "Published https://github.com/$repo/releases/tag/v$version"
fi
