export interface Photo {
  id: string;
  /** images.unsplash.com URL; sizing parameters are added per screen by {@link imageUrl}. */
  url: string;
  photographer: string;
  photographerUrl: string;
  pageUrl: string;
  description: string;
  topic?: string;
  /** Average colour, shown while the photo loads. */
  color?: string;
  /** Where it's cropped, when it was placed in a position editor (an app): the same point on every device. */
  position?: Position;
}

/**
 * Unsplash's smart crop, the middle, or around a point picked in an app's position editor (x, y: 0..1 across and down
 * the photo). A point fits any screen's shape, so this tab crops its own shape around it.
 */
export type Position = { crop: "smart" } | { crop: "center" } | { crop: "focus"; x: number; y: number };

/** The same position (or none) on two photos: nothing to paint again. */
export function samePosition(a?: Position, b?: Position): boolean {
  return JSON.stringify(a ?? null) === JSON.stringify(b ?? null);
}

/** Unsplash's crop parameters for a position (its smart crop when there's none). */
function cropParams(position?: Position): Record<string, string> {
  if (position?.crop === "center") return {};
  if (position?.crop === "focus") return { crop: "focalpoint", "fp-x": position.x.toFixed(4), "fp-y": position.y.toFixed(4) };
  return { crop: "entropy" };
}

/** The photo sized and cropped for this screen (Unsplash crops on its side, so only the pixels we show are sent). */
export function imageUrl(photo: Photo, width: number, height: number, quality = 80): string {
  const url = new URL(photo.url);
  // An AI wallpaper made on a desktop: Pollinations keeps one size only, so its link is used as it is.
  if (url.host !== "images.unsplash.com") return photo.url;
  url.search = new URLSearchParams({
    ixid: url.searchParams.get("ixid") ?? "",
    w: String(Math.round(width)),
    h: String(Math.round(height)),
    fit: "crop",
    ...cropParams(photo.position),
    q: String(quality),
    fm: "jpg",
    auto: "format",
  }).toString();
  return url.toString();
}

/** A tiny version that arrives in a blink, used as the blurred first paint. */
export function placeholderUrl(photo: Photo): string {
  return imageUrl(photo, 64, 40, 40);
}

/** Identifies the current time slot ("daily" and "hourly" keep one photo per slot), or null when every tab gets a new photo. */
export function slotKey(change: "tab" | "hourly" | "daily", bump: number, now = new Date()): string | null {
  if (change === "tab") return null;
  const day = `${now.getFullYear()}-${now.getMonth()}-${now.getDate()}`;
  return `${change === "hourly" ? `${day}T${now.getHours()}` : day}#${bump}`;
}
