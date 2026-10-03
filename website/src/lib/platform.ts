export type Platform = "windows" | "mac" | "linux" | "mobile" | "unknown";

/** Best-effort OS detection from a user agent string. */
export function detectPlatform(userAgent: string): Platform {
  const ua = userAgent.toLowerCase();
  if (/android|iphone|ipad|ipod|mobile/.test(ua)) return "mobile";
  if (ua.includes("windows")) return "windows";
  if (ua.includes("mac os")) return "mac";
  if (ua.includes("linux") || ua.includes("cros")) return "linux";
  return "unknown";
}

/** Oriel only runs on Windows; "unknown" gets the benefit of the doubt. */
export function canRunOriel(platform: Platform): boolean {
  return platform === "windows" || platform === "unknown";
}
