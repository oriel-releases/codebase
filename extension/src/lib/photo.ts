import { unsplashRef } from "./config";
import pool from "../data/pool.json";

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

export interface PoolPhoto {
  id: string;
  u: string;
  w: number;
  h: number;
  c: string;
  n: string;
  l: string;
  d: string;
  t: string;
}

export const poolPhotos = pool.photos as PoolPhoto[];
export const poolTopics = pool.topics as string[];

export function fromPool(p: PoolPhoto): Photo {
  return {
    id: p.id,
    url: `https://images.unsplash.com/${p.u}`,
    photographer: p.n,
    photographerUrl: `https://unsplash.com/@${p.l}?${unsplashRef}`,
    pageUrl: `https://unsplash.com/photos/${p.id}?${unsplashRef}`,
    description: p.d,
    topic: p.t,
    color: p.c,
  };
}

/** The photo sized and cropped for this screen (Unsplash crops on its side, so only the pixels we show are sent). */
export function imageUrl(photo: Photo, width: number, height: number, quality = 80): string {
  const url = new URL(photo.url);
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

/** Picks a photo for the time slot; the same inputs always give the same photo. */
export function pick(topics: string[], change: "tab" | "hourly" | "daily", bump: number, now = new Date()): Photo {
  const wanted = new Set(topics);
  const candidates = poolPhotos.filter((p) => wanted.has(p.t));
  const from = candidates.length ? candidates : poolPhotos;
  const day = `${now.getFullYear()}-${now.getMonth()}-${now.getDate()}`;
  const slot = change === "tab" ? String(Math.random()) : change === "hourly" ? `${day}T${now.getHours()}` : day;
  return fromPool(from[hash(`${slot}#${bump}`) % from.length]);
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
