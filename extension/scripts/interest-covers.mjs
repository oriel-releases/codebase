// Picks a cover photo for each built-in interest tile in the desktop apps (InterestCovers.cs): the best-liked big
// landscape photo among the top search results. Prints C# lines to paste.
// Run: node scripts/interest-covers.mjs nature "night sky" ...   (reads UNSPLASH_ACCESS_KEY from ../windows/.env)
// Costs one API request per interest.
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const env = readFileSync(join(root, "..", "windows", ".env"), "utf8");
const key = /^UNSPLASH_ACCESS_KEY=(.+)$/m.exec(env)?.[1]?.trim().replace(/^"|"$/g, "");
if (!key) throw new Error("UNSPLASH_ACCESS_KEY not found in windows/.env");

const interests = process.argv.slice(2);
if (interests.length === 0) throw new Error("Name the interests to find covers for.");

for (const interest of interests) {
  const url = `https://api.unsplash.com/search/photos?query=${encodeURIComponent(interest)}&orientation=landscape&content_filter=high&per_page=15`;
  const res = await fetch(url, { headers: { Authorization: `Client-ID ${key}`, "Accept-Version": "v1" } });
  if (!res.ok) throw new Error(`${interest}: HTTP ${res.status} (rate limit left: ${res.headers.get("x-ratelimit-remaining")})`);
  const { results } = await res.json();
  const best = results.filter((p) => p.width >= 3000 && p.width > p.height * 1.2).sort((a, b) => b.likes - a.likes)[0] ?? results[0];
  if (!best) {
    console.error(`// ${interest}: nothing found`);
    continue;
  }
  const raw = new URL(best.urls.raw);
  const path = raw.pathname.slice(1) + "?ixid=" + raw.searchParams.get("ixid");
  console.log(`        [${JSON.stringify(interest)}] = (${JSON.stringify(path)}, ${JSON.stringify(best.user.name)}),`);
}
