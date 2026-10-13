export type Platform = "windows" | "mac" | "linux" | "android" | "mobile" | "unknown";

/** Best-effort OS detection from a user agent string. */
export function detectPlatform(userAgent: string): Platform {
  const ua = userAgent.toLowerCase();
  if (ua.includes("android")) return "android";
  if (/iphone|ipad|ipod|mobile/.test(ua)) return "mobile";
  if (ua.includes("windows")) return "windows";
  if (ua.includes("mac os")) return "mac";
  if (ua.includes("linux") || ua.includes("cros")) return "linux";
  return "unknown";
}

/** Oriel runs on Windows, Linux and Android; "unknown" gets the benefit of the doubt. */
export function canRunOriel(platform: Platform): boolean {
  return platform === "windows" || platform === "linux" || platform === "android" || platform === "unknown";
}

export type Browser = "chrome" | "edge" | "firefox" | "unknown";

/**
 * Best-effort browser detection from a user agent string. Chromium browsers that use the Chrome Web Store (Brave, Opera,
 * Vivaldi) count as "chrome": Brave even reports itself as Chrome. Safari and anything else is "unknown".
 */
export function detectBrowser(userAgent: string): Browser {
  const ua = userAgent.toLowerCase();
  // Edge first: its user agent contains "Chrome/" too ("... Chrome/141 Safari/537.36 Edg/141").
  if (/ edg(e|a|ios)?\//.test(ua)) return "edge";
  if (/firefox\/|fxios\//.test(ua)) return "firefox";
  if (/chrome\/|chromium\/|crios\//.test(ua)) return "chrome";
  return "unknown";
}
