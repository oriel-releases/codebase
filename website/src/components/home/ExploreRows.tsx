"use client";

import { useState, type CSSProperties } from "react";
import Link from "next/link";
import { routes } from "@/config/site";
import { exploreRows } from "@/content/home";
import { imageUrl, photoTitle, type ExplorePhoto } from "@/lib/explore";
import { useExploreFeed } from "@/components/explore/useExploreFeed";
import { Container } from "@/components/ui/Container";
import { Section } from "@/components/ui/Section";
import { SectionHeading } from "@/components/ui/SectionHeading";

/**
 * The way into Explore: this hour's wallpapers in three rows of ten, drifting by forever, slowly, the middle row the
 * other way. Each row is its ten twice over, moved left by one set's width and started again, so it never ends; it
 * stops while the pointer is on it. With reduced motion the rows stand still. Each wallpaper opens on /explore.
 */

const perRow = 10;
/** Seconds for one set to pass, a little different per row so they never line up; and where each starts. */
const rows = [
  { reverse: false, seconds: 120, start: 0 },
  { reverse: true, seconds: 140, start: -45 },
  { reverse: false, seconds: 130, start: -90 },
];

export function ExploreRows() {
  const { feed } = useExploreFeed();
  const photos = feed?.landscape ?? [];

  return (
    <Section labelledBy="explore-rows" className="overflow-x-clip">
      <div className="w-full">
        <Container className="flex flex-col items-center text-center">
          <SectionHeading id="explore-rows">{exploreRows.title}</SectionHeading>
        </Container>

        {/* Faded at both ends, so the rows come in from nowhere and leave the same way. */}
        <div className="relative mt-12">
          <div className="flex flex-col gap-3 [mask-image:linear-gradient(to_right,transparent,black_12%,black_88%,transparent)] sm:gap-4">
            {rows.map((row, r) => (
              <Row key={r} photos={photos.slice(r * perRow, (r + 1) * perRow)} {...row} />
            ))}
          </div>
        </div>
      </div>
    </Section>
  );
}

function Row({ photos, reverse, seconds, start }: { photos: ExplorePhoto[]; reverse: boolean; seconds: number; start: number }) {
  // Until the feed is here: the same tiles, empty, so nothing moves when it arrives.
  const set: (ExplorePhoto | null)[] = photos.length ? photos : Array.from({ length: perRow }, () => null);
  const style: CSSProperties = {
    animationDuration: `${seconds}s`,
    animationDirection: reverse ? "reverse" : "normal",
    animationDelay: `${start}s`,
  };
  return (
    <div
      style={style}
      className="flex w-max motion-safe:animate-[drift_120s_linear_infinite] hover:[animation-play-state:paused]"
    >
      {[0, 1].map((copy) =>
        set.map((photo, i) => <Tile key={`${copy}-${photo?.id ?? i}`} photo={photo} copy={copy === 1} />),
      )}
    </div>
  );
}

const tileClass = "mr-3 h-32 shrink-0 overflow-hidden rounded-[1.5rem] sm:mr-4 sm:h-44 lg:h-52";

/** One wallpaper, a link to it on /explore. The second set is only there for the loop: hidden from screen readers and Tab. */
function Tile({ photo, copy }: { photo: ExplorePhoto | null; copy: boolean }) {
  const [loaded, setLoaded] = useState(false);
  if (!photo) return <span aria-hidden className={`${tileClass} aspect-[3/2] bg-surface`} />;
  return (
    <Link
      href={`${routes.explore}?wallpaper=${photo.id}`}
      aria-hidden={copy || undefined}
      tabIndex={copy ? -1 : undefined}
      draggable={false}
      className={`${tileClass} group relative isolate aspect-[3/2] ring-1 ring-ink/5 focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-accent`}
      style={{ backgroundColor: photo.color }}
    >
      {/* eslint-disable-next-line @next/next/no-img-element -- hotlinked Unsplash photos: there is no image server on static hosting */}
      <img
        src={imageUrl(photo, 600, 400)}
        alt={copy ? "" : photoTitle(photo)}
        loading="lazy"
        decoding="async"
        draggable={false}
        ref={(el) => {
          if (el?.complete && el.naturalWidth > 0) setLoaded(true);
        }}
        onLoad={() => setLoaded(true)}
        className={`size-full object-cover transition-[opacity,scale] duration-700 ease-out group-hover:scale-[1.05] motion-reduce:transition-none ${
          loaded ? "opacity-100" : "opacity-0"
        }`}
      />
    </Link>
  );
}
