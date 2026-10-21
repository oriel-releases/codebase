import { remember } from "./settings";
import { faviconUrl, siteIconUrl } from "./shortcuts";

/**
 * Shortcuts whose icon comes from the site itself: the browser's icon cache only knows sites visited in this browser
 * (a fresh Edge has never opened Gmail) and hands back its generic page icon for the rest.
 */
let siteIcons: string[] = remember<string[]>("site-icons") ?? [];

/** The icon to show first for a link: the browser's cache, or the site's own when the cache didn't know it (or there's none, Firefox). */
export function iconFor(url: string): string | null {
  return siteIcons.includes(url) ? siteIconUrl(url) : (faviconUrl(url) ?? siteIconUrl(url));
}

/** Whether a loaded icon came from the browser's cache (only those can be checked for the generic page icon). */
export const fromIconCache = (img: HTMLImageElement) => img.src.includes("/_favicon/");

/** Remembers that the browser's cache doesn't know this site; returns the site's own icon to show instead. */
export function useSiteIcon(url: string): string | null {
  siteIcons = [...siteIcons.filter((u) => u !== url), url];
  remember("site-icons", siteIcons);
  return siteIconUrl(url);
}

/** A small fingerprint of an icon's pixels (icons from the browser's cache can be read, they're the extension's own). */
function iconPrint(img: HTMLImageElement): string {
  const canvas = document.createElement("canvas");
  canvas.width = canvas.height = 16;
  const ctx = canvas.getContext("2d", { willReadFrequently: true })!;
  ctx.drawImage(img, 0, 0, 16, 16);
  return canvas.toDataURL();
}

/** What the browser's cache returns for a site it doesn't know: its generic page icon. */
let genericIcon: Promise<string> | null = null;
function genericPrint(): Promise<string> {
  return (genericIcon ??= new Promise((done) => {
    const img = new Image();
    img.onload = () => done(iconPrint(img));
    img.onerror = () => done("");
    img.src = faviconUrl("https://oriel-unknown-site.invalid/") ?? "";
  }));
}

/** Whether an icon from the browser's cache is only its generic page icon. */
export async function isGenericIcon(img: HTMLImageElement): Promise<boolean> {
  const generic = await genericPrint();
  return !!generic && iconPrint(img) === generic;
}

/**
 * A square picture (filled to its corners, like a profile photo) shows as a circle, as Chrome already hands some back;
 * a logo with clear corners (Gmail's M) keeps its shape. Only icons from the browser's cache can be read; a site's own
 * icon (another origin) stays as it is.
 */
export function filledCorners(img: HTMLImageElement): boolean {
  try {
    const canvas = document.createElement("canvas");
    canvas.width = canvas.height = 16;
    const ctx = canvas.getContext("2d", { willReadFrequently: true })!;
    ctx.drawImage(img, 0, 0, 16, 16);
    return [[0, 0], [15, 0], [0, 15], [15, 15]].every(([x, y]) => ctx.getImageData(x, y, 1, 1).data[3] > 200);
  } catch {
    return false;
  }
}
