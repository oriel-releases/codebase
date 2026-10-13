"use client";

import { doc, getDoc } from "firebase/firestore";
import { useEffect, useState, useSyncExternalStore } from "react";
import { site } from "@/config/site";
import { firestore } from "@/lib/firebase";
import { rememberShare } from "@/lib/pending-share";
import { useUser } from "@/lib/use-user";
import { Avatar } from "@/components/share/Avatar";
import { LinkProblem, SignInFirst, askAgain } from "@/components/share/LinkStates";
import { WallpaperSkeleton, WallpaperStage, type StagePhoto } from "@/components/wallpaper/WallpaperStage";

type Shared = StagePhoto & { from: string; picture: string };

const subscribe = () => () => {};
/** The browser tab's title (outside the component: it changes the page, not React state). */
const setTitle = (title: string) => {
  document.title = title;
};

/** A share's ID in the link: /share/AB3K9QXZ (letters and numbers, never O, 0, I or 1). */
function idFromPath(path: string): string | null {
  const id = (path.split("/")[2] ?? "").toUpperCase();
  return /^[A-HJ-NP-Z2-9]{8}$/.test(id) ? id : null;
}

/**
 * Links made by Oriel 1.0.7 and older carry the photo in the query (/share?id=&i=&by=&u=&from=): they keep working as
 * they did, signed in or not.
 */
function parseOldLink(search: string): Shared | null {
  const q = new URLSearchParams(search);
  const id = q.get("id") ?? "";
  const path = q.get("i") ?? "";
  if (!/^[A-Za-z0-9_-]{6,24}$/.test(id) || !/^[A-Za-z0-9_-]{6,120}$/.test(path)) return null;
  return {
    id,
    path,
    by: (q.get("by") ?? "").slice(0, 80),
    handle: /^[A-Za-z0-9_.-]{1,40}$/.test(q.get("u") ?? "") ? q.get("u")! : "",
    from: (q.get("from") ?? "").replace(/[\u0000-\u001f<>]/g, "").trim().slice(0, 30),
    picture: "",
  };
}

/** The page a friend lands on: who sent it, the photo, and one button that opens Oriel to set it as the wallpaper. */
export function SharedWallpaper() {
  const location = useSyncExternalStore(subscribe, () => window.location.pathname + window.location.search, () => null);
  const phone = useSyncExternalStore(subscribe, () => /Android|iPhone|iPad|iPod/i.test(navigator.userAgent), () => false);
  const user = useUser();
  const pathname = location?.split("?")[0] ?? "";
  const shareId = location === null ? undefined : idFromPath(pathname);
  const oldLink = location === null || shareId ? null : parseOldLink(location.slice(pathname.length));

  // A new link's photo, read once signed in. undefined while loading, null when there's no such share.
  const [loaded, setLoaded] = useState<{ id: string; shared: Shared | null } | undefined>(undefined);
  const uid = user?.uid;
  useEffect(() => {
    if (!shareId || !uid) return;
    let cancelled = false;
    getDoc(doc(firestore(), "shares", shareId))
      .then((snap) => {
        if (cancelled) return;
        const d = snap.data();
        setLoaded({
          id: shareId,
          shared:
            d && typeof d.photoId === "string" && typeof d.photoPath === "string"
              ? {
                  id: d.photoId,
                  path: d.photoPath,
                  by: String(d.photographer ?? ""),
                  handle: String(d.photographerHandle ?? ""),
                  from: String(d.from ?? ""),
                  picture: String(d.picture ?? ""),
                }
              : null,
        });
      })
      .catch(() => !cancelled && setLoaded({ id: shareId, shared: null }));
    return () => {
      cancelled = true;
    };
  }, [shareId, uid]);

  const shared = shareId ? (loaded?.id === shareId ? loaded.shared : undefined) : oldLink;
  const sentence = shared?.from ? `${shared.from} shared a wallpaper with you` : "A wallpaper was shared with you";

  // The tab title carries the name. The photo is also remembered: if this person installs Oriel first, it comes
  // along when they sign in.
  const photoId = shared?.id;
  const from = shared?.from ?? "";
  useEffect(() => {
    if (!photoId) return;
    setTitle(`${sentence} · ${site.name}`);
    rememberShare(photoId, from);
  }, [photoId, from, sentence]);

  if (location === null) return <WallpaperSkeleton />;
  if (shareId) {
    if (user === undefined) return <WallpaperSkeleton />;
    if (user === null) return <SignInFirst what="the wallpaper shared with you" />;
    if (shared === undefined) return <WallpaperSkeleton />;
  }
  if (!shared) return <LinkProblem title="This link looks broken" note={askAgain} />;

  return (
    <WallpaperStage
      photo={shared}
      label={sentence}
      labelAs="h1"
      badge={<Avatar picture={shared.picture} name={shared.from} className="flex size-6 items-center justify-center rounded-full" />}
      appLink={`oriel://wallpaper?id=${shared.id}${shared.from ? `&from=${encodeURIComponent(shared.from)}` : ""}`}
      again="open this link again"
      phone={phone}
    />
  );
}
