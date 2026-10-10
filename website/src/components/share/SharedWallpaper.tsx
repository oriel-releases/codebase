"use client";

import { useEffect, useSyncExternalStore } from "react";
import Link from "next/link";
import { routes, site } from "@/config/site";
import { rememberShare } from "@/lib/pending-share";
import { Logo } from "@/components/ui/Logo";
import { Frame, WallpaperSkeleton, WallpaperStage, linkClass, photoMaxHeight, type StagePhoto } from "@/components/wallpaper/WallpaperStage";

type Shared = StagePhoto & { from: string };

/** Reads the share link: the Unsplash photo ID, its image path, the photographer and who sent it. Anything unexpected makes the link "broken". */
function parse(search: string): Shared | null {
  const q = new URLSearchParams(search);
  const id = q.get("id") ?? "";
  const path = q.get("i") ?? "";
  if (!/^[A-Za-z0-9_-]{6,24}$/.test(id) || !/^[A-Za-z0-9_-]{6,120}$/.test(path)) return null;
  return {
    id,
    path,
    by: (q.get("by") ?? "").slice(0, 80),
    // The photographer's Unsplash handle (for the link to their profile); anything but a plain handle is dropped.
    handle: /^[A-Za-z0-9_.-]{1,40}$/.test(q.get("u") ?? "") ? q.get("u")! : "",
    // Only the sender's first name travels in the link. Shown as plain text only.
    from: (q.get("from") ?? "").replace(/[\u0000-\u001f<>]/g, "").trim().slice(0, 30),
  };
}

const subscribe = () => () => {};
/** The browser tab's title (outside the component: it changes the page, not React state). */
const setTitle = (title: string) => {
  document.title = title;
};

/** The page a friend lands on: who sent it, the photo, and one button that opens Oriel to set it as the wallpaper. */
export function SharedWallpaper() {
  // Read on the client only (the page is static); null on the server so the first paint matches.
  const search = useSyncExternalStore(subscribe, () => window.location.search, () => null);
  const phone = useSyncExternalStore(subscribe, () => /Android|iPhone|iPad|iPod/i.test(navigator.userAgent), () => false);
  const shared = search === null ? undefined : parse(search);
  const sentence = shared?.from ? `${shared.from} shared a wallpaper with you` : "A wallpaper was shared with you";

  // The tab title and history entry carry the name too (the static HTML can only hold the general title).
  // The photo is also remembered: if this person installs Oriel first, it comes along when they sign in.
  const sharedId = shared?.id;
  const sharedFrom = shared?.from ?? "";
  useEffect(() => {
    if (!sharedId) return;
    setTitle(`${sentence} · ${site.name}`);
    rememberShare(sharedId, sharedFrom);
  }, [sharedId, sharedFrom, sentence]);

  if (shared === undefined) return <WallpaperSkeleton />;

  if (shared === null) {
    return (
      <Frame
        photo={
          // No photo to show: the Oriel mark in its place, and no button.
          <div className={`grid aspect-[3/2] w-full place-items-center rounded-[2rem] bg-surface ${photoMaxHeight}`}>
            <Logo size={96} className="size-16 opacity-80 sm:size-24" />
          </div>
        }
        action={<span className="text-lg font-medium text-ink">This link looks broken</span>}
        note={
          <>
            Ask your friend to share it again, or&nbsp;
            <Link href={routes.home} className={linkClass}>
              see what {site.name} is
            </Link>
            .
          </>
        }
      />
    );
  }

  return (
    <WallpaperStage
      photo={shared}
      label={sentence}
      labelAs="h1"
      badge={shared.from ? shared.from.charAt(0).toUpperCase() : "★"}
      appLink={`oriel://wallpaper?id=${shared.id}${shared.from ? `&from=${encodeURIComponent(shared.from)}` : ""}`}
      again="open this link again"
      phone={phone}
    />
  );
}
