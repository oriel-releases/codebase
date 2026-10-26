import { fetchLatestVersion, type AppPlatform } from "@/lib/latest-version";

/** Public releases repos (the source repo is private): installer downloads and app updates come from here. */
export const repoUrls = {
  windows: "https://github.com/oriel-releases/windows",
  macos: "https://github.com/oriel-releases/macos",
  linux: "https://github.com/oriel-releases/linux",
  android: "https://github.com/oriel-releases/android",
} as const satisfies Record<AppPlatform, string>;

/** Used only when Firestore can't be read at build time. */
const fallbackVersions = {
  windows: "1.1.3",
  macos: "1.0.0",
  linux: "1.1.3",
  android: "1.1.3",
} as const satisfies Record<AppPlatform, string>;

const [windowsVersion, macosVersion, linuxVersion, androidVersion] = await Promise.all([
  fetchLatestVersion("windows"),
  fetchLatestVersion("macos"),
  fetchLatestVersion("linux"),
  fetchLatestVersion("android"),
]);

/**
 * The current release of each app. The version comes from Firestore (`application/windows`, `application/macos`,
 * `application/linux` and `application/android`), at build time here and again in the browser on the download page, so a new
 * release only needs that document updated. The default links always serve the newest file from GitHub
 * Releases (windows/scripts/release.ps1, macos/scripts/release.sh, linux/scripts/release.sh and
 * android/scripts/release.ps1 upload them). Set ORIEL_DOWNLOAD_URL / ORIEL_MACOS_DOWNLOAD_URL / ORIEL_LINUX_DOWNLOAD_URL / ORIEL_ANDROID_DOWNLOAD_URL to host them elsewhere.
 */
export const releases = {
  windows: {
    platform: "windows",
    name: "Windows",
    version: windowsVersion ?? fallbackVersions.windows,
    releasedOn: "2026-10-26",
    fileName: "Oriel-Setup.exe",
    url: process.env.ORIEL_DOWNLOAD_URL || `${repoUrls.windows}/releases/latest/download/Oriel-Setup.exe`,
    requirements: "Windows 10 (version 2004) or Windows 11, 64-bit",
  },
  macos: {
    platform: "macos",
    name: "Mac",
    version: macosVersion ?? fallbackVersions.macos,
    releasedOn: "2026-10-15",
    fileName: "Oriel.dmg",
    url: process.env.ORIEL_MACOS_DOWNLOAD_URL || `${repoUrls.macos}/releases/latest/download/Oriel.dmg`,
    requirements: "macOS 26 Tahoe or newer",
  },
  linux: {
    platform: "linux",
    name: "Linux",
    version: linuxVersion ?? fallbackVersions.linux,
    releasedOn: "2026-10-26",
    fileName: "Oriel.AppImage",
    url: process.env.ORIEL_LINUX_DOWNLOAD_URL || `${repoUrls.linux}/releases/latest/download/Oriel.AppImage`,
    requirements: "64-bit Linux desktop (GNOME, KDE Plasma, Xfce, Cinnamon and more)",
  },
  android: {
    platform: "android",
    name: "Android",
    version: androidVersion ?? fallbackVersions.android,
    releasedOn: "2026-10-26",
    fileName: "Oriel.apk",
    url: process.env.ORIEL_ANDROID_DOWNLOAD_URL || `${repoUrls.android}/releases/latest/download/Oriel.apk`,
    requirements: "Android 8.0 or newer",
  },
} as const;

export type Release = (typeof releases)[AppPlatform];
export type Releases = typeof releases;

/** The date of the most recent release of any app. */
export const lastReleasedOn = Object.values(releases)
  .map((r) => r.releasedOn)
  .sort()
  .at(-1)!;
