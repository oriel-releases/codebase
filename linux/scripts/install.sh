#!/usr/bin/env bash
# Installs Oriel for this user from a build folder (no root): ./scripts/publish.sh && ./scripts/install.sh
# For normal use, download Oriel.AppImage from the website instead; this is for running from source.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
src="${1:-$root/artifacts/Oriel-linux-x64}"
dest="${XDG_DATA_HOME:-$HOME/.local/share}/OrielApp"
[ -x "$src/Oriel" ] || { echo "No build at $src. Run ./scripts/publish.sh first." >&2; exit 1; }

rm -rf "$dest"; mkdir -p "$dest" "$HOME/.local/bin"
cp -r "$src/." "$dest/"
ln -sf "$dest/Oriel" "$HOME/.local/bin/oriel"
echo "Installed to $dest (command: oriel). Starting it once adds the menu entry and the oriel:// link handler."
nohup "$dest/Oriel" >/dev/null 2>&1 &
