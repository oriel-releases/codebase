/**
 * Explore: this hour's wallpapers, the same for every Oriel user. The public oriel-releases/explore repo picks them once
 * an hour and publishes them as `explore.json` (metadata only, in Unsplash's own shape; the images come from Unsplash's
 * CDN), so looking through them costs no Unsplash requests. Like the apps (desktop ExploreService, Android Explore.kt),
 * the browser keeps the feed and asks again once a newer hour is due, at most every 5 minutes until it's out. The last
 * hour's photos are kept too, so a wallpaper opened from it still shows after the hour turns.
 */

const feedUrl = "https://raw.githubusercontent.com/oriel-releases/explore/feed/explore.json";
const key = "oriel.explore";
const hourMs = 3_600_000;
/** The hour's feed is published a few minutes past the hour (sometimes later): until then, ask again this often. */
const retryMs = 5 * 60_000;

export type ExplorePhoto = {
  id: string;
  width: number;
  height: number;
  /** The photo's main colour (#rrggbb), shown while it loads. */
  color: string;
  /** Its own description, or Unsplash's generated one; may be empty. */
  description: string;
  /** The interest it was found under ("ocean"). */
  topic: string;
  /** Unsplash's raw image address; sizes and crops are added to it. */
  raw: string;
  by: string;
  handle: string;
};

export type ExploreFeed = {
  /** The hour (UTC) it was picked for, in milliseconds. */
  hour: number;
  /** For computers. */
  landscape: ExplorePhoto[];
  /** For phones. */
  portrait: ExplorePhoto[];
};

type Kept = { feed: ExploreFeed; previous: ExplorePhoto[] };

/* eslint-disable @typescript-eslint/no-explicit-any -- Unsplash's JSON, checked field by field */
function parsePhoto(o: any): ExplorePhoto | null {
  const raw = o?.urls?.raw;
  if (typeof o?.id !== "string" || typeof raw !== "string" || !raw.startsWith("https://images.unsplash.com/")) return null;
  return {
    id: o.id,
    width: Number(o.width) || 0,
    height: Number(o.height) || 0,
    color: typeof o.color === "string" ? o.color : "#808080",
    description: String(o.description || o.alt_description || "").trim(),
    topic: String(o.topic || ""),
    raw,
    by: String(o.user?.name || ""),
    handle: String(o.user?.username || ""),
  };
}

function parseFeed(o: any): ExploreFeed | null {
  const hour = Date.parse(o?.hour);
  if (!Number.isFinite(hour)) return null;
  const list = (items: unknown) => {
    const seen = new Set<string>();
    return (Array.isArray(items) ? items : []).map(parsePhoto).filter((p): p is ExplorePhoto => {
      if (!p || seen.has(p.id)) return false;
      seen.add(p.id);
      return true;
    });
  };
  return { hour, landscape: list(o.landscape), portrait: list(o.portrait) };
}
/* eslint-enable @typescript-eslint/no-explicit-any */

let kept: Kept | null | undefined;
let triedAt = 0;

function read(): Kept | null {
  try {
    return JSON.parse(localStorage.getItem(key) ?? "null") as Kept | null;
  } catch {
    return null;
  }
}

function write(value: Kept) {
  try {
    localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // Private mode or storage full: the feed is fetched again next visit.
  }
}

/** What this browser already has (maybe an hour old), to show at once while a newer feed is asked for. */
export function keptFeed(): ExploreFeed | null {
  if (kept === undefined) kept = read();
  return kept?.feed ?? null;
}

/** The feed is for this hour (UTC) or later: nothing newer to fetch yet. */
const isCurrent = (feed: ExploreFeed, now: number) => feed.hour >= Math.floor(now / hourMs) * hourMs;

/**
 * This hour's wallpapers; the last ones kept when the new ones can't be had; null when there never were any.
 * `force` (Refresh) asks again even if the last try failed moments ago.
 */
export async function loadFeed(force = false): Promise<ExploreFeed | null> {
  if (kept === undefined) kept = read();
  const now = Date.now();
  if (kept && isCurrent(kept.feed, now)) return kept.feed;
  if (!force && now - triedAt < retryMs) return kept?.feed ?? null;
  triedAt = now;
  try {
    const response = await fetch(feedUrl, { cache: "no-store" });
    const fresh = response.ok ? parseFeed(await response.json()) : null;
    if (fresh && fresh.landscape.length + fresh.portrait.length > 0 && (!kept || fresh.hour >= kept.feed.hour)) {
      const previous = kept && fresh.hour > kept.feed.hour ? [...kept.feed.landscape, ...kept.feed.portrait] : (kept?.previous ?? []);
      kept = { feed: fresh, previous };
      write(kept);
    }
  } catch {
    // Offline, or GitHub didn't answer: the kept feed stays.
  }
  return kept?.feed ?? null;
}

const shuffled = <T,>(items: T[]) => {
  const out = [...items];
  for (let i = out.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [out[i], out[j]] = [out[j]!, out[i]!];
  }
  return out;
};

/**
 * Refresh, as on the phone: a new hour's photos when they're out, else these same ones in a new order (so it still
 * looks fresh). It takes at least `minimumMs`, so a quick answer still reads as done.
 */
export async function refreshFeed(current: ExploreFeed | null, minimumMs = 1400): Promise<ExploreFeed | null> {
  const started = Date.now();
  const feed = await loadFeed(true);
  await new Promise((done) => setTimeout(done, Math.max(0, minimumMs - (Date.now() - started))));
  if (feed && current && feed.hour === current.hour) {
    return { ...current, landscape: shuffled(current.landscape), portrait: shuffled(current.portrait) };
  }
  return feed;
}

/**
 * A wallpaper by its id, and the list it is in (computers' or phones'), to step through. Photos from the hour before
 * are found too, on their own.
 */
export function findPhoto(feed: ExploreFeed | null, id: string): { photo: ExplorePhoto; list: ExplorePhoto[] } | null {
  for (const list of feed ? [feed.landscape, feed.portrait] : []) {
    const photo = list.find((p) => p.id === id);
    if (photo) return { photo, list };
  }
  const old = kept?.previous.find((p) => p.id === id);
  return old ? { photo: old, list: [old] } : null;
}

/** The photo at `width` (and cropped to `height`, keeping faces and detail in), as the apps fetch it. */
export function imageUrl(photo: ExplorePhoto, width: number, height?: number): string {
  const url = new URL(photo.raw);
  url.searchParams.set("w", String(width));
  if (height) {
    url.searchParams.set("h", String(height));
    url.searchParams.set("fit", "crop");
    url.searchParams.set("crop", "faces,entropy");
  }
  url.searchParams.set("q", "75");
  url.searchParams.set("fm", "jpg");
  url.searchParams.set("auto", "format");
  return url.toString();
}

/** A sentence for the photo: its description, starting with a capital, or its topic. */
export function photoTitle(photo: ExplorePhoto): string {
  const text = photo.description || (photo.topic ? `${photo.topic} wallpaper` : "Wallpaper");
  return text.charAt(0).toUpperCase() + text.slice(1);
}
