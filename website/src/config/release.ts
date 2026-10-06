import { fetchLatestVersion } from "@/lib/latest-version";

/** Public releases repo (the source repo is private): installer downloads and app updates come from here. */
export const repoUrl = "https://github.com/oriel-releases/windows";

/** Used only when Firestore can't be read at build time. */
const fallbackVersion = "1.0.5";

/**
 * The current Windows release. The version comes from Firestore (`application/version`), at build
 * time here and again in the browser on the download page, so a new release only needs that
 * document updated. The default link always serves the newest installer from GitHub Releases
 * (windows/scripts/release.ps1 uploads it). Set ORIEL_DOWNLOAD_URL to host it elsewhere.
 */
export const release = {
  version: (await fetchLatestVersion()) ?? fallbackVersion,
  releasedOn: "2026-10-06",
  fileName: "Oriel-Setup.exe",
  url: process.env.ORIEL_DOWNLOAD_URL ?? `${repoUrl}/releases/latest/download/Oriel-Setup.exe`,
  platform: "Windows",
  requirements: "Windows 10 (version 2004) or Windows 11, 64-bit",
} as const;

export type Release = typeof release;
