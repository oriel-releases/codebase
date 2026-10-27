"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { RotateCw } from "lucide-react";
import { routes } from "@/config/site";
import { explorePage } from "@/content/explore";
import { imageUrl, photoTitle, type ExplorePhoto } from "@/lib/explore";
import { SegmentedSwitch } from "@/components/download/SegmentedSwitch";
import { exploreMemory } from "@/components/explore/memory";
import type { FeedState } from "@/components/explore/useExploreFeed";
import { Button } from "@/components/ui/Button";
import { MonitorIcon, PhoneIcon } from "@/components/ui/icons";

/**
 * /explore without a wallpaper: this hour's wallpapers in a grid, for computers (landscape) or phones (portrait), a few
 * rows at a time as the visitor scrolls. Refresh works as on the phone: a new hour, or the same photos in a new order.
 */

type Kind = "landscape" | "portrait";
const pageSize = 24;

const shape: Record<Kind, { grid: string; ratio: string; size: [number, number] }> = {
  landscape: { grid: "grid-cols-1 sm:grid-cols-2 lg:grid-cols-3", ratio: "3 / 2", size: [800, 533] },
  portrait: { grid: "grid-cols-2 sm:grid-cols-3 lg:grid-cols-5", ratio: "9 / 16", size: [420, 747] },
};

/** The title, the same in the static page (for search engines) and once loaded. */
function Heading() {
  return <h1 className="text-4xl font-semibold tracking-tight text-balance sm:text-6xl">{explorePage.title}</h1>;
}

const tileClass = "rounded-[1.5rem] bg-surface";
/** The title on the left, the list switch and Refresh on the right, along the grid's edges; stacked on a phone. */
const bar = "mx-auto flex w-full max-w-7xl flex-col gap-6 px-4 sm:flex-row sm:items-end sm:justify-between sm:px-8";

/** Before the feed is known: the heading, and the grid's shapes pulsing. */
export function ExploreGridSkeleton() {
  return (
    <div className="flex flex-col pt-28 pb-24 sm:pt-32">
      <div className={bar}>
        <Heading />
        <div className="h-11 w-72 animate-pulse rounded-full bg-surface" aria-hidden />
      </div>
      <ul aria-hidden className={`mx-auto mt-8 grid w-full max-w-7xl gap-4 px-4 sm:px-8 ${shape.landscape.grid}`}>
        {Array.from({ length: 9 }, (_, i) => (
          <li key={i} className={`animate-pulse ${tileClass}`} style={{ aspectRatio: shape.landscape.ratio }} />
        ))}
      </ul>
    </div>
  );
}

export function ExploreGrid({ feed, loading, refreshing, refresh }: FeedState) {
  // Phones start on phones' wallpapers; coming back from a wallpaper, the list it was opened from.
  const [kind, setKind] = useState<Kind>(
    () => exploreMemory.kind ?? (/Android|iPhone|iPad|iPod/i.test(navigator.userAgent) ? "portrait" : "landscape"),
  );
  const [shown, setShown] = useState(pageSize);
  const end = useRef<HTMLDivElement>(null);
  const list = feed?.[kind] ?? [];

  // Near the end of what's shown: the next rows.
  useEffect(() => {
    const el = end.current;
    if (!el) return;
    const watch = new IntersectionObserver(
      ([entry]) => {
        if (entry?.isIntersecting) setShown((n) => Math.min(n + pageSize, list.length));
      },
      { rootMargin: "800px 0px" },
    );
    watch.observe(el);
    return () => watch.disconnect();
  }, [list.length]);

  return (
    <div className="flex flex-col pt-28 pb-24 sm:pt-32">
      <div className={bar}>
        <Heading />
        <div className="flex flex-wrap items-center gap-2.5">
          <SegmentedSwitch
            label="Wallpapers for"
            value={kind}
            onChange={(next) => {
              setKind(next);
              setShown(pageSize);
              exploreMemory.kind = next;
            }}
            options={[
              { key: "landscape", label: explorePage.computers, Icon: MonitorIcon },
              { key: "portrait", label: explorePage.phones, Icon: PhoneIcon },
            ]}
          />
          <Button variant="secondary" onClick={refresh} disabled={refreshing} aria-busy={refreshing}>
            <RotateCw size={15} aria-hidden className={refreshing ? "animate-spin" : ""} />
            {explorePage.refresh}
          </Button>
        </div>
      </div>
      {!feed && !loading && (
        <p role="alert" className="mx-auto mt-4 w-full max-w-7xl px-4 text-sm text-muted sm:px-8">
          {explorePage.failed}
        </p>
      )}

      {/* A new order (Refresh) or another list draws the grid afresh, the tiles rising in one after another. */}
      <ul
        key={`${kind}-${list[0]?.id ?? ""}`}
        className={`mx-auto mt-8 grid w-full max-w-7xl gap-4 px-4 transition-opacity duration-300 sm:px-8 ${shape[kind].grid} ${
          refreshing ? "opacity-50" : ""
        }`}
      >
        {(loading ? [] : list.slice(0, shown)).map((photo, i) => (
          <Tile key={photo.id} photo={photo} kind={kind} delay={(i % pageSize) * 25} />
        ))}
        {loading &&
          Array.from({ length: 9 }, (_, i) => (
            <li key={i} aria-hidden className={`animate-pulse ${tileClass}`} style={{ aspectRatio: shape[kind].ratio }} />
          ))}
      </ul>
      <div ref={end} aria-hidden />
    </div>
  );
}

/** One wallpaper: its colour until it arrives, a gentle zoom on hover, and its photographer at its foot. */
function Tile({ photo, kind, delay }: { photo: ExplorePhoto; kind: Kind; delay: number }) {
  const [loaded, setLoaded] = useState(false);
  const [w, h] = shape[kind].size;
  return (
    <li
      className="transition-[opacity,translate] duration-700 ease-out starting:translate-y-3 starting:opacity-0 motion-reduce:transition-none"
      style={{ transitionDelay: `${delay}ms` }}
    >
      <Link
        href={`${routes.explore}?wallpaper=${photo.id}`}
        onClick={() => {
          exploreMemory.kind = kind;
        }}
        className={`group relative isolate block overflow-hidden ${tileClass} ring-1 ring-ink/5 focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-accent`}
        style={{ aspectRatio: shape[kind].ratio, backgroundColor: photo.color }}
      >
        {/* eslint-disable-next-line @next/next/no-img-element -- hotlinked Unsplash photos: there is no image server on static hosting */}
        <img
          src={imageUrl(photo, w, h)}
          alt={photoTitle(photo)}
          loading="lazy"
          decoding="async"
          draggable={false}
          ref={(el) => {
            if (el?.complete && el.naturalWidth > 0) setLoaded(true);
          }}
          onLoad={() => setLoaded(true)}
          className={`absolute inset-0 size-full object-cover transition-[opacity,scale] duration-700 ease-out group-hover:scale-[1.04] motion-reduce:transition-none ${
            loaded ? "opacity-100" : "opacity-0"
          }`}
        />
        <span
          aria-hidden
          className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/60 to-black/0 px-4 pt-10 pb-3 text-left text-[13px] font-medium text-white opacity-0 transition-opacity duration-300 group-hover:opacity-100 group-focus-visible:opacity-100 [@media(hover:none)]:opacity-100"
        >
          {photo.by}
        </span>
      </Link>
    </li>
  );
}
