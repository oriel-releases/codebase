/** Public URL in production builds; override with the SITE_URL environment variable. */
const productionUrl = "https://oriel.akbarjon.uz";

/** Site-wide facts. */
export const site = {
  name: "Oriel",
  tagline: "A new wallpaper, every day.",
  /** Release stage shown next to the name; set to null once Oriel leaves beta. */
  stage: "Beta" as string | null,
  description:
    "Oriel is a free app for Windows and Linux that sets a fresh, high-quality Unsplash photo as your desktop wallpaper every day. Lightweight, quiet, and it lives in your tray.",
  url: process.env.SITE_URL ?? (process.env.NODE_ENV === "production" ? productionUrl : "http://localhost:3000"),
  locale: "en_US",
  keywords: [
    "daily wallpaper",
    "Windows wallpaper app",
    "Linux wallpaper app",
    "Unsplash wallpaper",
    "wallpaper changer",
    "Windows 11 wallpaper",
    "GNOME KDE wallpaper changer",
    "desktop background",
  ],
  unsplashUrl: "https://unsplash.com/?utm_source=oriel&utm_medium=referral",
} as const;

export const routes = {
  home: "/",
  download: "/download",
  login: "/login",
} as const;

export function absoluteUrl(path: string): string {
  return new URL(path, site.url).toString();
}
