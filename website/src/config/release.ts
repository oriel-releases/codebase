/** Public releases repo (the source repo is private): installer downloads and app updates come from here. */
export const repoUrl = "https://github.com/akbarjorayev/oriel-public";

/**
 * The current Windows release. The default link always serves the newest installer from
 * GitHub Releases (windows/scripts/release.ps1 uploads it), so only version and date need
 * updating per release. Set ORIEL_DOWNLOAD_URL to host it elsewhere.
 */
export const release = {
  version: "1.0.0",
  releasedOn: "2026-10-04",
  fileName: "Oriel-Setup.exe",
  url: process.env.ORIEL_DOWNLOAD_URL ?? `${repoUrl}/releases/latest/download/Oriel-Setup.exe`,
  platform: "Windows",
  requirements: "Windows 10 (version 2004) or Windows 11, 64-bit",
} as const;

export type Release = typeof release;
