// Builds src/data/pool.json: a curated set of Unsplash photos per topic, so the extension needs no API key
// and makes no API calls (the photos are hotlinked from images.unsplash.com, as Unsplash asks).
// Run: npm run pool   (reads UNSPLASH_ACCESS_KEY from ../windows/.env). Costs one API request per topic.
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const env = readFileSync(join(root, "..", "windows", ".env"), "utf8");
const key = /^UNSPLASH_ACCESS_KEY=(.+)$/m.exec(env)?.[1]?.trim().replace(/^"|"$/g, "");
if (!key) throw new Error("UNSPLASH_ACCESS_KEY not found in windows/.env");

// Keep in step with BuiltInTopics in windows/src/Oriel.Core/Settings/OrielSettings.cs.
const topics = ["nature", "mountains", "ocean", "space", "cities", "architecture", "minimal", "dark", "abstract", "forest", "desert", "aerial", "night sky", "snow"];

const pool = [];
const seen = new Set();
for (const topic of topics) {
  const url = `https://api.unsplash.com/photos/random?query=${encodeURIComponent(topic)}&orientation=landscape&content_filter=high&count=30`;
  const res = await fetch(url, { headers: { Authorization: `Client-ID ${key}`, "Accept-Version": "v1" } });
  if (!res.ok) throw new Error(`${topic}: HTTP ${res.status} (rate limit left: ${res.headers.get("x-ratelimit-remaining")})`);
  for (const p of await res.json()) {
    if (seen.has(p.id) || p.width < 3000) continue;
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
  console.log(topic.padEnd(14), pool.length, "photos so far, requests left:", res.headers.get("x-ratelimit-remaining"));
}

mkdirSync(join(root, "src", "data"), { recursive: true });
writeFileSync(join(root, "src", "data", "pool.json"), JSON.stringify({ builtAt: new Date().toISOString(), topics, photos: pool }));
console.log("Wrote", pool.length, "photos");
