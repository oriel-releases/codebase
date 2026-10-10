# Unsplash production access: application draft

Apply at https://unsplash.com/oauth/applications → Oriel → "Apply for production". Unsplash reviews by hand
(usually a few days). Production raises the limit from 50 to 5,000 requests per hour. Attach the screenshots listed
at the end; they are what reviewers check most.

## Application name

Oriel

## Description (paste)

Oriel is a free desktop app for Windows and Linux that sets a new Unsplash photo as the wallpaper every day (or on a
schedule the user picks), plus a browser extension that shows the same photo on the new tab page. Users pick topics they
like (nature, mountains, cities, ...), can search Unsplash and set any result as their wallpaper, keep favorites, and share
a photo with a friend through a link that opens Oriel to set it.

Website: https://oriel.akbarjon.uz

## How Oriel uses the API (paste)

- Search (`/search/photos`) for the user's topics and searches, and `/photos/:id` to open a photo a friend shared. One
  search finds the day's photo, so a typical user makes 1 to 3 requests per wallpaper change.
- Images are loaded from the `urls` the API returns (images.unsplash.com, with Unsplash's sizing parameters), never
  re-hosted on our servers. The desktop app saves the one photo it sets as the wallpaper on the user's own computer,
  which is what a wallpaper needs.
- The `download_location` endpoint is triggered every time a photo is set as a wallpaper, by any route (schedule,
  "Change now", search, a shared link).
- Attribution: wherever a photo is shown, Oriel credits "Photo by <name> on Unsplash", linking to the photographer's
  profile and the photo page with `utm_source=oriel&utm_medium=referral`. That's on the app's Home screen, the history,
  the search results, the browser extension's photo info and the website's share and wallpaper-of-the-day pages.
- Oriel does not sell photos or use them to make a competing image service. It is free, with no ads.

## Why production access

Oriel is past beta testing and the demo key's 50 requests per hour is shared by every user, so wallpaper changes start
failing once more than a handful of people use it at the same hour.

## Screenshots to attach

1. The Home screen with "Photo by … on Unsplash" under the current wallpaper.
2. Search results, each with the photographer's name on the photo.
3. The browser extension's photo info panel (the "i" button).
4. https://oriel.akbarjon.uz/today with its credit line.
