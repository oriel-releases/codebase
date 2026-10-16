// Builds src/data/today.json: the photos "Wallpaper of the day" (/today) rotates through, one per day, the same for
// everyone. Taken from the browser extension's curated Unsplash pool (extension/src/data/pool.json), so it needs no API
// key and no API calls. Only wide, large photos. Run: npm run today   (after `npm run pool` in extension/).
// Also writes the extension's copy, extension/src/data/today.json.
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const pool = JSON.parse(readFileSync(join(root, "..", "extension", "src", "data", "pool.json"), "utf8"));

const photos = pool.photos
  .filter((p) => p.w >= 4000 && p.w / p.h >= 1.4 && p.w / p.h <= 2.1)
  .map((p) => ({ id: p.id, path: p.u.split("?")[0], by: p.n, handle: p.l, description: p.d, topic: p.t, ratio: Math.round((p.w / p.h) * 1000) / 1000 }));

// Spread the topics out, so two days in a row rarely share one: take them round-robin.
const byTopic = new Map();
for (const p of photos) byTopic.set(p.topic, [...(byTopic.get(p.topic) ?? []), p]);
const mixed = [];
while ([...byTopic.values()].some((list) => list.length)) for (const list of byTopic.values()) if (list.length) mixed.push(list.shift());

writeFileSync(join(root, "src", "data", "today.json"), JSON.stringify(mixed));
// The extension shows the same Wallpaper of the day and keeps its own copy (only what it needs), so its source builds
// on its own, as the Firefox store's reviewers build it.
const slim = mixed.map(({ id, path, by, handle }) => ({ id, path, by, handle }));
writeFileSync(join(root, "..", "extension", "src", "data", "today.json"), JSON.stringify(slim));
console.log(`today.json: ${mixed.length} photos (website and extension)`);
