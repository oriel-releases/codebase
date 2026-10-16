import list from "../data/today.json";
import { fromPool, poolPhotos, type Photo } from "./photo";
import { unsplashRef } from "./config";

/** The Wallpaper of the day list, the website's own (written with it by website/scripts/build-today.mjs). */
interface TodayEntry {
  id: string;
  path: string;
  by: string;
  handle: string;
}

const dayMs = 86_400_000;

/**
 * Today's photo, the same one the website's /today and the apps show: the days since 1 January 1970 in local time, as
 * a position in the list (going round). Nothing is stored or fetched.
 */
export function todayPhoto(now = new Date()): Photo {
  const photos = list as TodayEntry[];
  const day = Math.floor((now.getTime() - now.getTimezoneOffset() * 60_000) / dayMs);
  const p = photos[((day % photos.length) + photos.length) % photos.length];
  const pooled = poolPhotos.find((x) => x.id === p.id);
  if (pooled) return fromPool(pooled);
  return {
    id: p.id,
    url: `https://images.unsplash.com/${p.path}`,
    photographer: p.by,
    photographerUrl: `https://unsplash.com/@${p.handle}?${unsplashRef}`,
    pageUrl: `https://unsplash.com/photos/${p.id}?${unsplashRef}`,
    description: "",
  };
}
