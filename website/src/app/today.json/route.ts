import photos from "@/data/today.json";

export const dynamic = "force-static";

/**
 * The Wallpaper of the day list for the desktop apps, as a static file (/today.json). A static site can't answer
 * "which photo is today's", so the apps get the list and the rule, and pick exactly like /today does: the number of
 * days since 1 January 1970 in local time, as a position in the list (going round). Same photo everywhere, no server.
 */
export function GET() {
  return Response.json({
    rule: "photos[localDaysSince1970 mod photos.length]",
    photos: (photos as { id: string; path: string; by: string; handle: string }[]).map(({ id, path, by, handle }) => ({ id, path, by, handle })),
  });
}
