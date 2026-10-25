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
    crop: "entropy",
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
