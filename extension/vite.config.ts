import { defineConfig, type Plugin } from "vite";
import tailwindcss from "@tailwindcss/vite";
import { resolve } from "node:path";
import pkg from "./package.json" with { type: "json" };

/** The two browsers differ only in how the manifest declares things; the code is the same. */
function manifest(target: "chrome" | "firefox"): Plugin {
  return {
    name: "oriel-manifest",
    generateBundle() {
      // Store builds (ORIEL_STORE=1, see scripts/package.mjs) only touch the production site; dev builds also allow localhost.
      const sites = ["https://oriel.akbarjon.uz/login*", ...(process.env.ORIEL_STORE ? [] : ["http://localhost:3000/login*"])];
      const json = {
        manifest_version: 3,
        name: "Oriel",
        short_name: "Oriel",
        version: pkg.version,
        description: "A beautiful Unsplash photo on every new tab. Match your Oriel desktop wallpaper, or pick your own.",
        icons: { "16": "icons/icon-16.png", "32": "icons/icon-32.png", "48": "icons/icon-48.png", "128": "icons/icon-128.png" },
        chrome_url_overrides: { newtab: "newtab.html" },
        // The toolbar icon drops the settings dialog down under it (popup.html); on an Oriel new tab it opens the page's own
        // dialog instead (src/background.ts). Firefox runs background scripts, not service workers.
        action: { default_title: "Oriel settings", default_popup: "popup.html" },
        background: target === "firefox" ? { scripts: ["background.js"] } : { service_worker: "background.js" },
        // "search" lets the search box use the browser's own default search engine.
        // "favicon" (Chrome only) lets the shortcuts show each site's icon from the browser's own cache.
        // "activeTab": the toolbar popup can offer the page you're on as a shortcut (only after you click the icon).
        permissions: ["storage", "search", "activeTab", ...(target === "chrome" ? ["favicon"] : [])],
        content_scripts: [{ matches: sites, js: ["content.js"], run_at: "document_start" }],
        ...(target === "firefox" && {
          // data_collection_permissions needs Firefox 140 (desktop) / 142 (Android).
          browser_specific_settings: {
            gecko: { id: "extension@oriel.akbarjon.uz", strict_min_version: "140.0", data_collection_permissions: { required: ["none"] } },
            gecko_android: { strict_min_version: "142.0" },
          },
        }),
      };
      this.emitFile({ type: "asset", fileName: "manifest.json", source: JSON.stringify(json, null, 2) });
    },
  };
}

export default defineConfig(({ mode }) => {
  const target = mode === "firefox" ? "firefox" : "chrome";
  return {
    base: "./",
    plugins: [tailwindcss(), manifest(target)],
    build: {
      outDir: `dist/${target}`,
      emptyOutDir: true,
      target: "es2022",
      chunkSizeWarningLimit: 900,
      rollupOptions: {
        input: { newtab: resolve(__dirname, "newtab.html"), content: resolve(__dirname, "src/content/bridge.ts"), popup: resolve(__dirname, "popup.html"), background: resolve(__dirname, "src/background.ts") },
        output: {
          // The content script and the background script must stay one self-contained file with a fixed name.
          entryFileNames: (chunk) => (chunk.name === "content" || chunk.name === "background" ? `${chunk.name}.js` : "assets/[name]-[hash].js"),
        },
      },
    },
  };
});
