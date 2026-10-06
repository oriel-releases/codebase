#!/usr/bin/env bash
# Self-contained, pre-compiled Release build -> artifacts/Oriel-linux-<arch>
# Works on Linux, macOS and Windows (Git Bash / WSL): the .NET SDK cross-compiles.
#   ./scripts/publish.sh [x64|arm64]
set -euo pipefail
arch="${1:-x64}"
case "$arch" in x64|arm64) ;; *) echo "arch must be x64 or arm64" >&2; exit 2;; esac
root="$(cd "$(dirname "$0")/.." && pwd)"
out="$root/artifacts/Oriel-linux-$arch"

rm -rf "$out"
dotnet publish "$root/src/Oriel.App" -c Release -r "linux-$arch" --self-contained -o "$out" -nologo
rm -f "$out"/*.pdb
# The Windows and macOS backends of Avalonia are dead weight in a Linux build.
rm -f "$out"/Avalonia.Win32*.dll "$out"/Avalonia.Native.dll "$out"/Avalonia.Metal.dll
chmod +x "$out/Oriel"
echo "Built $out ($(du -sh "$out" | cut -f1))"
