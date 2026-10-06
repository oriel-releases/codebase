#!/usr/bin/env bash
# Removes Oriel's integration for this user: timers, autostart, menu entry, install folder.
# Your photos (Pictures/Oriel) and settings (~/.local/share/oriel) stay unless you pass --purge.
set -u
data="${XDG_DATA_HOME:-$HOME/.local/share}"
config="${XDG_CONFIG_HOME:-$HOME/.config}"

pkill -x Oriel 2>/dev/null || true
if command -v systemctl >/dev/null; then
  systemctl --user disable --now oriel-apply.timer 2>/dev/null
  systemctl --user stop oriel-retry.timer oriel-retry.service 2>/dev/null
  systemctl --user daemon-reload 2>/dev/null
fi
rm -f "$config/systemd/user/oriel-apply.timer" "$config/systemd/user/oriel-apply.service"
rm -f "$config/autostart/oriel.desktop" "$data/applications/oriel.desktop"
rm -f "$data/icons/hicolor/256x256/apps/oriel.png" "$HOME/.local/bin/oriel"
rm -rf "$data/OrielApp"
command -v update-desktop-database >/dev/null && update-desktop-database "$data/applications" 2>/dev/null
command -v secret-tool >/dev/null && secret-tool clear application oriel 2>/dev/null

if [ "${1:-}" = "--purge" ]; then rm -rf "$data/oriel"; echo "Removed settings and history too."; fi
echo "Oriel removed."
