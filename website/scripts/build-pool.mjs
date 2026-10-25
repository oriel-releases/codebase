// Builds src/data/pool.json: a curated set of Unsplash photos per topic, which the Wallpaper of the day list
// (scripts/build-today.mjs) is taken from (the photos are hotlinked from images.unsplash.com, as Unsplash asks).
// Run: npm run pool   (reads UNSPLASH_ACCESS_KEY from ../windows/.env).
// It adds to the photos already there (the Wallpaper of the day list points at them), up to `perTopic` per topic, at most
// `rounds` API requests per topic; on hitting the rate limit it keeps what it has. Each photo also gets `a`, its vibrant
// accent color, from imgix's palette (images.unsplash.com, not the API: costs no requests).
import { readFileSync, writeFileSync, mkdirSync, existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const perTopic = 60;
const rounds = 3;

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const env = readFileSync(join(root, "..", "windows", ".env"), "utf8");
const key = /^UNSPLASH_ACCESS_KEY=(.+)$/m.exec(env)?.[1]?.trim().replace(/^"|"$/g, "");
if (!key) throw new Error("UNSPLASH_ACCESS_KEY not found in windows/.env");

// Keep in step with BuiltInTopics in windows/src/Oriel.Core/Settings/OrielSettings.cs.
const topics = ["nature", "mountains", "ocean", "space", "cities", "architecture", "minimal", "dark", "abstract", "forest", "desert", "aerial", "night sky", "snow"];

const file = join(root, "src", "data", "pool.json");
const pool = existsSync(file) ? JSON.parse(readFileSync(file, "utf8")).photos : [];
const seen = new Set(pool.map((p) => p.id));

fetching: for (const topic of topics) {
  for (let round = 0; round < rounds && pool.filter((p) => p.t === topic).length < perTopic; round++) {
    const url = `https://api.unsplash.com/photos/random?query=${encodeURIComponent(topic)}&orientation=landscape&content_filter=high&count=30`;
    const res = await fetch(url, { headers: { Authorization: `Client-ID ${key}`, "Accept-Version": "v1" } });
    if (!res.ok) {
      console.log(`${topic}: HTTP ${res.status}, stopping here (rate limit left: ${res.headers.get("x-ratelimit-remaining")})`);
      break fetching;
    }
    for (const p of await res.json()) {
      if (seen.has(p.id) || p.width < 3000 || pool.filter((x) => x.t === topic).length >= perTopic) continue;
      seen.add(p.id);
      const raw = new URL(p.urls.raw);
      pool.push({
        id: p.id,
        // Path plus the ixid Unsplash uses to count views; everything else is added per screen.
        u: raw.pathname.slice(1) + "?ixid=" + raw.searchParams.get("ixid"),
        w: p.width,
        h: p.height,
        c: p.color,
        n: p.user.name,
        l: p.user.username,
        d: (p.alt_description ?? p.description ?? "").slice(0, 120),
        t: topic,
      });
    }
    console.log(topic.padEnd(14), pool.filter((p) => p.t === topic).length, "photos, requests left:", res.headers.get("x-ratelimit-remaining"));
  }
}

/** The photo's vibrant color (imgix palette), else its muted one, else Unsplash's average color. */
async function accentOf(p) {
  try {
    const res = await fetch(`https://images.unsplash.com/${p.u.split("?")[0]}?palette=json&colors=6`);
    const d = (await res.json()).dominant_colors ?? {};
    return d.vibrant?.hex ?? d.muted?.hex ?? p.c;
  } catch {
    return p.c;
  }
}

const missing = pool.filter((p) => !p.a);
for (let i = 0; i < missing.length; i += 8) {
  await Promise.all(missing.slice(i, i + 8).map(async (p) => (p.a = await accentOf(p))));
}
console.log("Accent colors added:", missing.length);

mkdirSync(join(root, "src", "data"), { recursive: true });
writeFileSync(file, JSON.stringify({ builtAt: new Date().toISOString(), topics, photos: pool }));
console.log("Wrote", pool.length, "photos");
