import { unsplashRef } from "./config.ts";
import type { Photo } from "./photo.ts";

/**
 * "Its own" picks from the apps' Explore feed: 300 wide Unsplash photos, the same for everyone, new every hour
 * (oriel-releases/explore). Only metadata comes from GitHub; the images come from Unsplash's CDN, as it asks.
 */
export const feedUrl = "https://raw.githubusercontent.com/oriel-releases/explore/feed/explore.json";

const key = "oriel.explore";
/** Tried at most this often while the new hour's feed isn't up yet (or the browser is offline). */
const retryMs = 5 * 60_000;

/** The parts of Unsplash's own photo object the feed keeps (see build.mjs in oriel-releases/explore). */
interface FeedPhoto {
  id: string;
  color?: string | null;
  description?: string | null;
  alt_description?: string | null;
  topic?: string;
  urls: { raw: string };
  links: { html: string };
  user: { name: string; username: string };
}

interface Cached {
  /** The feed's hour (UTC, ISO), e.g. "2026-10-23T09:00:00Z". */
  hour: string;
  triedAt: number;
  photos: Photo[];
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

/** One feed photo as the page's Photo; null for anything that isn't a free images.unsplash.com photo. */
export function fromFeed(p: FeedPhoto): Photo | null {
  if (!p?.id || !p.urls?.raw?.startsWith("https://images.unsplash.com/") || !p.user?.username) return null;
  return {
    id: p.id,
    url: p.urls.raw,
    photographer: p.user.name,
    photographerUrl: `https://unsplash.com/@${p.user.username}?${unsplashRef}`,
    pageUrl: `${p.links?.html ?? `https://unsplash.com/photos/${p.id}`}?${unsplashRef}`,
    description: p.description || p.alt_description || "",
    topic: p.topic,
    color: p.color ?? undefined,
  };
}

/** The start of this hour in UTC, as the feed writes it. */
export function hourOf(now = new Date()): string {
  const d = new Date(now);
  d.setUTCMinutes(0, 0, 0);
  return d.toISOString().replace(".000Z", "Z");
}

/** The photos kept from the last fetch (possibly an earlier hour's); empty before the first one arrives. */
export function feedPhotos(): Photo[] {
  return read()?.photos ?? [];
}

/**
 * Fetches the feed when the kept one is from an earlier hour (at most every 5 minutes). True when new photos arrived.
 * Never throws: offline, the kept photos stay.
 */
export async function refreshFeed(now = Date.now()): Promise<boolean> {
  const kept = read();
  if (kept && (kept.hour === hourOf(new Date(now)) || now - kept.triedAt < retryMs)) return false;
  write({ hour: kept?.hour ?? "", triedAt: now, photos: kept?.photos ?? [] });
  try {
    const res = await fetch(feedUrl, { cache: "no-cache" });
    if (!res.ok) return false;
    const json = (await res.json()) as { hour?: string; landscape?: FeedPhoto[] };
    const photos = (json.landscape ?? []).map(fromFeed).filter((p): p is Photo => p !== null);
    if (!photos.length || json.hour === kept?.hour) return false;
    write({ hour: json.hour ?? hourOf(new Date(now)), triedAt: now, photos });
    return true;
  } catch {
    return false;
  }
}

/** Picks a photo for the time slot from [photos]; the same inputs always give the same photo. Null when there are none. */
export function pickFrom(photos: Photo[], change: "tab" | "hourly" | "daily", bump: number, now = new Date()): Photo | null {
  if (!photos.length) return null;
  const day = `${now.getFullYear()}-${now.getMonth()}-${now.getDate()}`;
  const slot = change === "tab" ? String(Math.random()) : change === "hourly" ? `${day}T${now.getHours()}` : day;
  return photos[hash(`${slot}#${bump}`) % photos.length];
}

/** This slot's photo from the kept feed; null before the first feed arrives (the caller shows Wallpaper of the day). */
export function pick(change: "tab" | "hourly" | "daily", bump: number, now = new Date()): Photo | null {
  return pickFrom(feedPhotos(), change, bump, now);
}

/** FNV-1a: small, fast and well spread. */
function hash(text: string): number {
  let h = 0x811c9dc5;
  for (let i = 0; i < text.length; i++) {
    h ^= text.charCodeAt(i);
    h = Math.imul(h, 0x01000193);
  }
  return h >>> 0;
}
