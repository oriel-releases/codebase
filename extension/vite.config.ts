import { defineConfig, type Plugin } from "vite";
import tailwindcss from "@tailwindcss/vite";
import { resolve } from "node:path";
import pkg from "./package.json" with { type: "json" };

/** The two browsers differ only in how the manifest declares things; the code is the same. */
function manifest(target: "chrome" | "firefox"): Plugin {
  return {
    name: "oriel-manifest",
    generateBundle() {
      const sites = ["https://oriel.akbarjon.uz/login*", "http://localhost:3000/login*"];
      const json = {
        manifest_version: 3,
        name: "Oriel: New Tab Wallpaper",
        short_name: "Oriel",
        version: pkg.version,
        description: "A beautiful Unsplash photo on every new tab. Match your Oriel desktop wallpaper, or pick your own.",
        icons: { "16": "icons/icon-16.png", "32": "icons/icon-32.png", "48": "icons/icon-48.png", "128": "icons/icon-128.png" },
        chrome_url_overrides: { newtab: "newtab.html" },
        // "search" lets the search box use the browser's own default search engine.
        permissions: ["storage", "search"],
        content_scripts: [{ matches: sites, js: ["content.js"], run_at: "document_start" }],
        ...(target === "firefox" && {
          browser_specific_settings: { gecko: { id: "extension@oriel.akbarjon.uz", strict_min_version: "128.0", data_collection_permissions: { required: ["none"] } } },
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
        input: { newtab: resolve(__dirname, "newtab.html"), content: resolve(__dirname, "src/content/bridge.ts") },
        output: {
          // The content script must stay one self-contained file with a fixed name.
          entryFileNames: (chunk) => (chunk.name === "content" ? "content.js" : "assets/[name]-[hash].js"),
        },
      },
    },
  };
});
