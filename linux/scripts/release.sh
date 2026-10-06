#!/usr/bin/env bash
# Builds, packages (Velopack AppImage + update feed) and publishes a release to GitHub Releases.
# Installed copies pick it up within a day (Settings > Updates > Check for updates to get it right away).
#
#   1. Bump <Version> in src/Oriel.App/Oriel.App.csproj
#   2. export GITHUB_TOKEN=<token with Contents: read and write on oriel-releases/linux>
#   3. ./scripts/release.sh [x64|arm64]      (or --no-upload to only build ./releases locally)
#
# Run it on Linux (or WSL): Velopack builds AppImages with Linux tools.
set -euo pipefail

arch="x64"; upload=1
for a in "$@"; do
  case "$a" in
    --no-upload) upload=0 ;;
    x64|arm64) arch="$a" ;;
    *) echo "unknown argument: $a" >&2; exit 2 ;;
  esac
done

root="$(cd "$(dirname "$0")/.." && pwd)"
repo_url="https://github.com/oriel-releases/linux"
pack_id="OrielApp"        # never change it, or installed copies stop updating
vpk_version="1.2.161"     # keep in step with the Velopack package in Oriel.App.csproj
channel="linux"; [ "$arch" = "x64" ] || channel="linux-$arch"
out="$root/releases"
csproj="$root/src/Oriel.App/Oriel.App.csproj"

version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$csproj" | head -1)"
[ -n "$version" ] || { echo "No <Version> in $csproj" >&2; exit 1; }
if [ "$upload" = 1 ] && [ -z "${GITHUB_TOKEN:-}" ]; then echo 'Set GITHUB_TOKEN, or use --no-upload' >&2; exit 1; fi
echo "Releasing Oriel $version ($channel)"

if ! dotnet tool list -g | grep -qE "^vpk\s+$vpk_version"; then
  dotnet tool update -g vpk --version "$vpk_version"
fi
vpk="$HOME/.dotnet/tools/vpk"; [ -x "$vpk" ] || vpk="vpk"

"$root/scripts/publish.sh" "$arch"

# The previous release lets vpk build a small delta update. The first release has none.
if [ "$upload" = 1 ]; then
  "$vpk" download github --repoUrl "$repo_url" --token "$GITHUB_TOKEN" --channel "$channel" --outputDir "$out" \
    || echo "No previous release found; making a full release only."
fi

"$vpk" pack --packId "$pack_id" --packTitle Oriel --packAuthors "Akbarjon Jorayev" --packVersion "$version" \
  --packDir "$root/artifacts/Oriel-linux-$arch" --mainExe Oriel \
  --icon "$root/src/Oriel.App/Assets/AppIcon.png" --channel "$channel" --outputDir "$out"

if [ "$upload" = 0 ]; then echo "Built $out (not uploaded)"; exit 0; fi

tag="v$version"
"$vpk" upload github --repoUrl "$repo_url" --token "$GITHUB_TOKEN" --channel "$channel" --outputDir "$out" \
  --publish --releaseName "Oriel $version" --tag "$tag"

# Also attach the AppImage as Oriel.AppImage: the website links to
# $repo_url/releases/latest/download/Oriel.AppImage, which always serves the newest one.
if [ "$arch" = "x64" ]; then
  appimage="$(ls "$out"/*.AppImage | head -1)"
  api="${repo_url/https:\/\/github.com\//https://api.github.com/repos/}"
  uploads="${repo_url/https:\/\/github.com\//https://uploads.github.com/repos/}"
  release_id="$(curl -fsS -H "Authorization: Bearer $GITHUB_TOKEN" -H "Accept: application/vnd.github+json" "$api/releases/tags/$tag" \
    | sed -n 's/^  "id": \([0-9]*\),.*/\1/p' | head -1)"
  curl -fsS -X POST -H "Authorization: Bearer $GITHUB_TOKEN" -H "Accept: application/vnd.github+json" \
    -H "Content-Type: application/octet-stream" --data-binary @"$appimage" \
    "$uploads/releases/$release_id/assets?name=Oriel.AppImage" > /dev/null
fi

echo "Published $repo_url/releases/tag/$tag"
echo "Now update 'version' in Firestore (application/linux), plus fallbackVersion and releasedOn in website/src/config/release.ts, and redeploy the site."
