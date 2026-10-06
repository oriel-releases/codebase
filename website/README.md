# Oriel website

Next.js (App Router) + Tailwind CSS. Two pages: `/` (what Oriel is) and `/download`.

```bash
npm install
npm run dev      # http://localhost:3000
npm run build
```

Production builds use `https://oriel.akbarjon.uz` as the site URL. To use another domain, set
`SITE_URL` (copy `.env.example` to `.env.local`, or set it in your host's environment variables).

## Deploy (Firebase Hosting)

Every page is static: `npm run build` exports the site to `out/` (`output: "export"` in
`next.config.ts`). Deploy with `npm run build && firebase deploy --only hosting` (project `oriel-gcp`).
The custom domain `oriel.akbarjon.uz` is connected in the Firebase console under Hosting.

## Layout

```
src/config     site facts and the current release (version, download URL)
src/content    page copy; components only render it
src/lib        logic: SEO metadata, schema.org JSON-LD, llms.txt, platform detection
src/components ui/ (Button, Logo, ...), layout/, home/, download/, seo/
src/app        routes + SEO files: sitemap, robots, manifest, opengraph-image, llms.txt
```

## SEO / GEO

- Per-page canonical, Open Graph and Twitter metadata (`lib/seo/metadata.ts`)
- JSON-LD: WebSite, SoftwareApplication, FAQPage, BreadcrumbList (`lib/seo/structured-data.ts`)
- `sitemap.xml`, `robots.txt` (all crawlers allowed), web manifest, generated OG image
- `/llms.txt`: plain-text summary for AI answer engines, built from the same content as the pages

## Releasing

Run `windows/scripts/release.ps1` (Windows) or `linux/scripts/release.sh` (Linux), then set `version` in the
Firestore document for that app (`application/windows` or `application/linux`), update its `fallbackVersions`
and `releasedOn` in `src/config/release.ts`, and redeploy. The download buttons point at
`https://github.com/oriel-releases/windows/releases/latest/download/Oriel-Setup.exe` and
`https://github.com/oriel-releases/linux/releases/latest/download/Oriel.AppImage`, which always
serve the newest file, so the links themselves never change. `firestore.rules` lets the site read those two
documents (and nothing else under `application/`).
