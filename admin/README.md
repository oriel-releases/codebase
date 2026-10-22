# Oriel Admin

The owner's dashboard at **oriel-admin.web.app**: every account, its devices and app versions, current wallpaper,
shared settings, favorites and followers, plus shares and follow codes. Read-only.

Next.js 16 (static export), Tailwind 4, [shadcn/ui](https://ui.shadcn.com) (Radix, Nova preset) with the website's
palette, Firebase Auth + Firestore in the browser.

```sh
npm install
npm run dev     # http://localhost:3001
npm run build   # static files in out/
npx shadcn@latest add <component>   # more components into src/components/ui
```

## Who can get in

Only the owner's Google sign-in. The page checks it (`adminEmail` in `src/lib/firebase.ts`), but that is only the
sign on the door: the code is public to anyone who loads the site. The lock is `isAdmin()` in
`website/firestore.rules`, checked by Google: the same email, verified, signed in with Google. Any other account gets
"permission denied" on every read. Keep the two emails the same, and deploy the rules from `website/`:
`firebase deploy --only firestore:rules`.

Who can change those rules can change who gets in: keep `oriel-gcp`'s Firebase members to the owner.

## Deploying (Firebase Hosting)

Its own Hosting site in `oriel-gcp`, **oriel-admin** (https://oriel-admin.web.app), next to the website's:

```sh
npm run build
npx firebase deploy --only hosting   # from admin/: firebase.json names the site
```

- `oriel-admin.web.app` must be in **Firebase console > Authentication > Settings > Authorized domains**, or Google
  sign-in refuses it.
- `firebase.json` keeps it out of search engines and frames.

## Reads

Spark allows 50,000 Firestore reads a day. The overview reads every account and every session once and keeps them in
memory (switching pages is free; Refresh reads again). An account's page reads about 30 documents.
