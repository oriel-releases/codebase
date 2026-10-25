import type { Photo } from "./photo.ts";
import { siteUrl, unsplashRef } from "./config.ts";

/**
 * The Wallpaper of the day list, the website's own, published at /today.json for every app (written by
 * website/scripts/build-today.mjs). Fetched and kept here; nothing is bundled.
 */
export interface TodayEntry {
  id: string;
  path: string;
  by: string;
  handle: string;
}

const key = "oriel.today";
/** The list hardly ever changes: read again once a day, or every 5 minutes while it can't be. */
const dayMs = 86_400_000;
const retryMs = 5 * 60_000;

interface Cached {
  fetchedAt: number;
  triedAt: number;
  photos: TodayEntry[];
}

function read(): Cached | null {
  try {
    const c = JSON.parse(localStorage.getItem(key) ?? "null") as Cached | null;
    return c && Array.isArray(c.photos) ? c : null;
  } catch {
    return null;
  }
}

function write(c: Cached): void {
  try {
    localStorage.setItem(key, JSON.stringify(c));
  } catch {
    // Storage full or blocked: fetched again next time.
  }
}

/**
 * Today's photo from [list], the same one the website's /today and the apps show: the days since 1 January 1970 in
 * local time, as a position in the list (going round). Null for an empty list.
 */
export function todayFrom(list: TodayEntry[], now = new Date()): Photo | null {
  if (!list.length) return null;
  const day = Math.floor((now.getTime() - now.getTimezoneOffset() * 60_000) / dayMs);
  const p = list[((day % list.length) + list.length) % list.length];
  return {
    id: p.id,
    url: `https://images.unsplash.com/${p.path}`,
    photographer: p.by,
    photographerUrl: `https://unsplash.com/@${p.handle}?${unsplashRef}`,
    pageUrl: `https://unsplash.com/photos/${p.id}?${unsplashRef}`,
    description: "",
  };
}

/** Today's photo from the kept list; null before the list was first fetched. */
export function todayPhoto(now = new Date()): Photo | null {
  return todayFrom(read()?.photos ?? [], now);
}

/** Fetches the list when the kept one is a day old (or missing). True when a list arrived. Never throws. */
export async function refreshToday(now = Date.now()): Promise<boolean> {
  const kept = read();
  if (kept && (now - kept.fetchedAt < dayMs || now - kept.triedAt < retryMs)) return false;
  write({ fetchedAt: kept?.fetchedAt ?? 0, triedAt: now, photos: kept?.photos ?? [] });
  try {
    const res = await fetch(`${siteUrl()}/today.json`);
    if (!res.ok) return false;
    const photos = ((await res.json()) as { photos?: TodayEntry[] }).photos?.filter((p) => p?.id && p.path && p.handle) ?? [];
    if (!photos.length) return false;
    write({ fetchedAt: now, triedAt: now, photos });
    return true;
  } catch {
    return false;
  }
}
