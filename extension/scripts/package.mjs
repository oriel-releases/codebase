// Store build: runs both builds with ORIEL_STORE=1, then copies each output (and the source) into release/.
// Zip the *contents* of each folder yourself (select everything inside, right-click, Compress to ZIP file).
import { execFileSync } from "node:child_process";
import { cpSync, mkdirSync, readdirSync, readFileSync, rmSync } from "node:fs";
import { resolve } from "node:path";

const root = resolve(import.meta.dirname, "..");
const { version } = JSON.parse(readFileSync(resolve(root, "package.json"), "utf8"));

execFileSync("npm", ["run", "build"], { cwd: root, stdio: "inherit", shell: process.platform === "win32", env: { ...process.env, ORIEL_STORE: "1" } });
rmSync(resolve(root, "release"), { recursive: true, force: true });
mkdirSync(resolve(root, "release"));

cpSync(resolve(root, "dist/chrome"), resolve(root, `release/oriel-chrome-${version}`), { recursive: true });
cpSync(resolve(root, "dist/firefox"), resolve(root, `release/oriel-firefox-${version}`), { recursive: true });
const sourceDir = resolve(root, `release/oriel-source-${version}`);
mkdirSync(sourceDir);
for (const entry of readdirSync(root)) {
  if (["node_modules", "dist", "release"].includes(entry)) continue;
  cpSync(resolve(root, entry), resolve(sourceDir, entry), { recursive: true });
}
console.log(`\nFolders for version ${version} are in extension/release/`);
