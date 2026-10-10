"use client";

import { useSyncExternalStore } from "react";
import { site } from "@/config/site";
import photos from "@/data/today.json";
import { rememberShare } from "@/lib/pending-share";
import { WallpaperSkeleton, WallpaperStage, type StagePhoto } from "@/components/wallpaper/WallpaperStage";

const list = photos as (StagePhoto & { description: string; topic: string; ratio: number })[];
const dayMs = 86_400_000;

/**
 * Which photo is today's, with nothing stored anywhere: count the days since 1 January 1970 in the visitor's own time
 * zone (so it changes at their midnight), and take that position in the list, going round when it runs out. Everyone
 * gets the same photo on the same date, and tomorrow is always the next one in the list.
 */
export function photoForDate(date: Date): { photo: (typeof list)[number]; day: number } {
  const day = Math.floor((date.getTime() - date.getTimezoneOffset() * 60_000) / dayMs);
  return { photo: list[((day % list.length) + list.length) % list.length], day };
}

const subscribe = () => () => {};
/** The date as a stable string, so the store only changes when the day does. */
const todayKey = () => new Date().toDateString();

/** Wallpaper of the day, laid out like a shared photo: the photo, one button, the footer, on one screen. */
export function WallpaperOfTheDay() {
  // The day is the visitor's (their midnight), so it is only known in the browser. The server draws the skeleton.
  const key = useSyncExternalStore(subscribe, todayKey, () => null);
  const phone = useSyncExternalStore(subscribe, () => /Android|iPhone|iPad|iPod/i.test(navigator.userAgent), () => false);
  if (key === null) return <WallpaperSkeleton />;

  const today = new Date(key);
  const { photo } = photoForDate(today);
  const date = today.toLocaleDateString(undefined, { weekday: "long", day: "numeric", month: "long" });

  return (
    <WallpaperStage
      photo={photo}
      label={`Wallpaper of the day · ${date}`}
      badge={
        <svg viewBox="0 0 24 24" width={13} height={13} fill="none" stroke="currentColor" strokeWidth={2.5} strokeLinecap="round" aria-hidden>
          <circle cx="12" cy="12" r="4" />
          <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
        </svg>
      }
      appLink={`oriel://wallpaper?id=${photo.id}&from=${site.name}`}
      // Installing first? The photo comes along at sign-in ("Oriel shared a wallpaper with you").
      onSet={() => rememberShare(photo.id, site.name)}
      again="click again"
      phone={phone}
    />
  );
}
