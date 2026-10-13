// Store build: runs both builds with ORIEL_STORE=1, then copies each output (and the source) into release/. Edge takes
// the Chrome build as it is. Only the newest version sits in release/: an earlier one (its folders and the zips made
// from them) moves to release/old/<version>/.
// Zip the *contents* of each folder yourself (select everything inside, right-click, Compress to ZIP file).
import { execFileSync } from "node:child_process";
import { cpSync, existsSync, mkdirSync, readdirSync, readFileSync, renameSync, rmSync } from "node:fs";
import { resolve } from "node:path";

const root = resolve(import.meta.dirname, "..");
const { version } = JSON.parse(readFileSync(resolve(root, "package.json"), "utf8"));

execFileSync("npm", ["run", "build"], { cwd: root, stdio: "inherit", shell: process.platform === "win32", env: { ...process.env, ORIEL_STORE: "1" } });
const release = resolve(root, "release");
if (!existsSync(release)) mkdirSync(release);
for (const entry of readdirSync(release)) {
  const old = /^oriel-[a-z]+-(\d+\.\d+\.\d+)(\.zip)?$/.exec(entry)?.[1];
  if (!old || old === version) continue;
  mkdirSync(resolve(release, "old", old), { recursive: true });
  renameSync(resolve(release, entry), resolve(release, "old", old, entry));
}
const folder = (kind) => {
  const dir = resolve(root, `release/oriel-${kind}-${version}`);
  rmSync(dir, { recursive: true, force: true });
  return dir;
};

cpSync(resolve(root, "dist/chrome"), folder("chrome"), { recursive: true });
cpSync(resolve(root, "dist/chrome"), folder("edge"), { recursive: true });
cpSync(resolve(root, "dist/firefox"), folder("firefox"), { recursive: true });
const sourceDir = folder("source");
mkdirSync(sourceDir);
for (const entry of readdirSync(root)) {
  if (["node_modules", "dist", "release"].includes(entry)) continue;
  cpSync(resolve(root, entry), resolve(sourceDir, entry), { recursive: true });
}
console.log(`\nFolders for version ${version} are in extension/release/`);
