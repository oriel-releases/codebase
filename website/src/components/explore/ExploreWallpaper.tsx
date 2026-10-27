"use client";

import { useCallback, useEffect, useMemo, useState, useSyncExternalStore } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft, Wallpaper } from "lucide-react";
import { routes, site } from "@/config/site";
import { explorePage } from "@/content/explore";
import { findPhoto, imageUrl, photoTitle, type ExplorePhoto } from "@/lib/explore";
import { rememberShare } from "@/lib/pending-share";
import { detectPlatform } from "@/lib/platform";
import { DeviceStage, type StageImage } from "@/components/devices/DeviceStage";
import type { FeedState } from "@/components/explore/useExploreFeed";
import { AnchorButton, ButtonLink } from "@/components/ui/Button";

/**
 * /explore?wallpaper=ID: one of this hour's wallpapers on Oriel's devices (the iMac, the MacBook and the iPhone, each
 * cropping it to its own screen, its credit on the iMac's new tab) and "Set as wallpaper", which opens Oriel with it
 * like a share link. The arrow keys step through the list it came from, the devices crossfading as they go. "Explore"
 * beside the button (a plain link) and Escape go to the grid.
 */

const utm = "utm_source=oriel&utm_medium=referral";
const linkClass = "underline underline-offset-4 transition-colors hover:text-ink";
const subscribe = () => () => {};

/**
 * The stage's room: as big as the screen allows, with the buttons still in view under it (the footer is a scroll
 * away). The page is a full screen of its own.
 */
const stageWidth = "w-[min(100%,calc((100svh-14rem)*16/9))] min-w-[min(100%,40rem)]";

export function ExploreWallpaper({ id, feed, loading }: FeedState & { id: string }) {
  const router = useRouter();
  const found = useMemo(() => findPhoto(feed, id), [feed, id]);
  const list = found?.list;
  const index = found && list ? list.indexOf(found.photo) : 0;
  const images = useMemo<StageImage[]>(
    () => (list ?? []).map((p) => ({ key: p.id, src: (w, h) => imageUrl(p, w, h) })),
    [list],
  );

  const back = useCallback(() => router.push(routes.explore), [router]);

  const go = useCallback(
    (step: number) => {
      if (!list || list.length < 2) return;
      const next = list[(index + step + list.length) % list.length]!;
      router.replace(`${routes.explore}?wallpaper=${next.id}`, { scroll: false });
    },
    [list, index, router],
  );

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      // Not while the eye has hidden the page (its Escape brings the page back) or another key combination.
      if (e.defaultPrevented || e.altKey || e.ctrlKey || e.metaKey) return;
      if (e.key === "Escape" && document.documentElement.hasAttribute("data-view-only")) return;
      if (e.key === "ArrowLeft") go(-1);
      else if (e.key === "ArrowRight") go(1);
      else if (e.key === "Escape") back();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [go, back]);

  // The tab says which wallpaper this is (the static page only knows it's Explore).
  useEffect(() => {
    if (!found) return;
    const before = document.title;
    document.title = `${photoTitle(found.photo)} · ${site.name}`;
    return () => {
      document.title = before;
    };
  }, [found]);

  if (loading) return <WallpaperSkeleton />;
  if (!found || !list) return <Gone id={id} />;
  const { photo } = found;

  return (
    <div className="flex min-h-svh flex-col justify-center pt-20 pb-10">
      <div className="mx-auto w-full max-w-[88rem] px-4 sm:px-8">
        <Backdrop list={list} index={index} />
        <h1 className="sr-only">{photoTitle(photo)}</h1>
        <div data-view-grow className={`relative mx-auto ${stageWidth}`}>
          <DeviceStage
            images={images}
            index={index}
            credit={
              <>
                Photo by{" "}
                <a href={`https://unsplash.com/@${photo.handle}?${utm}`} target="_blank" rel="noopener noreferrer" tabIndex={-1} className="underline">
                  {photo.by}
                </a>{" "}
                on{" "}
                <a href={`https://unsplash.com/photos/${photo.id}?${utm}`} target="_blank" rel="noopener noreferrer" tabIndex={-1} className="underline">
                  Unsplash
                </a>
              </>
            }
          />
        </div>

        {/* The credit for screen readers (the iMac's is hidden from them), then the one action. */}
        <p className="sr-only">
          Photo by{" "}
          <a href={`https://unsplash.com/@${photo.handle}?${utm}`} target="_blank" rel="noopener noreferrer">
            {photo.by}
          </a>{" "}
          on{" "}
          <a href={`https://unsplash.com/photos/${photo.id}?${utm}`} target="_blank" rel="noopener noreferrer">
            Unsplash
          </a>
        </p>
        <div className="flex flex-col items-center text-center">
          <SetWallpaper key={photo.id} photo={photo} withBack />
        </div>
      </div>
    </div>
  );
}

/**
 * The wallpaper over the whole window, behind everything, under a frosted layer: the page takes on its colours. The
 * layer is tinted with the page's own colour, so the button and the header stay readable in light and dark. It
 * fades in once loaded and crossfades with the devices; a small copy is plenty under that much blur.
 */
function Backdrop({ list, index }: { list: ExplorePhoto[]; index: number }) {
  const n = list.length;
  return (
    <div aria-hidden className="pointer-events-none fixed inset-0 -z-10 overflow-hidden">
      {list.map((p, i) => {
        const near = i === index || (i + 1) % n === index || i === (index + 1) % n;
        return near && <BackdropImage key={p.id} photo={p} shown={i === index} />;
      })}
      <div className="absolute inset-0 bg-paper/45 backdrop-blur-3xl backdrop-saturate-150 dark:bg-paper/55" />
    </div>
  );
}

/** One wallpaper behind the page: it fades in once it has loaded (never popping in half drawn), and out when it goes. */
function BackdropImage({ photo, shown }: { photo: ExplorePhoto; shown: boolean }) {
  const [loaded, setLoaded] = useState(false);
  return (
    // eslint-disable-next-line @next/next/no-img-element -- hotlinked Unsplash photos: there is no image server on static hosting
    <img
      src={imageUrl(photo, 640)}
      alt=""
      draggable={false}
      ref={(el) => {
        if (el?.complete && el.naturalWidth > 0) setLoaded(true);
      }}
      onLoad={() => setLoaded(true)}
      className={`absolute inset-0 size-full scale-110 object-cover transition-opacity duration-[1800ms] ease-out motion-reduce:transition-none ${
        shown && loaded ? "opacity-100" : "opacity-0"
      }`}
    />
  );
}

/**
 * Opens Oriel with the wallpaper (`oriel://wallpaper?id=…`, as /today and share links do), and remembers it for after
 * installing. Then a line eases in: allow the browser to open Oriel, or get it. iPhones and iPads get a line instead.
 */
function SetWallpaper({ photo, withBack = false }: { photo: Pick<ExplorePhoto, "id">; withBack?: boolean }) {
  const [tried, setTried] = useState(false);
  const iphone = useSyncExternalStore(subscribe, () => detectPlatform(navigator.userAgent) === "mobile", () => false);
  // A plain link to the grid, no animation: a page of its own, not a step back in history.
  const backButton = withBack && (
    <ButtonLink
      href={routes.explore}
      variant="secondary"
      aria-label={`Back to ${explorePage.back}`}
      className="py-4 text-base max-sm:px-4"
    >
      <ArrowLeft size={17} aria-hidden />
      <span className="max-sm:sr-only">{explorePage.back}</span>
    </ButtonLink>
  );
  if (iphone)
    return (
      <>
        <p className="mt-8 max-w-sm text-sm text-pretty text-muted">{explorePage.phoneNote}</p>
        {backButton && <div className="mt-6">{backButton}</div>}
      </>
    );
  return (
    <>
      <div data-chrome="down" className="mt-8 flex w-full max-w-md gap-2.5">
        {backButton}
        <AnchorButton
          href={`oriel://wallpaper?id=${photo.id}&from=${site.name}`}
          onClick={() => {
            setTried(true);
            rememberShare(photo.id, site.name);
          }}
          className="flex-1 py-4 text-base"
        >
          <Wallpaper size={17} aria-hidden />
          {explorePage.set}
        </AnchorButton>
      </div>
      <p
        data-chrome="down"
        aria-hidden={!tried}
        className={`mt-4 min-h-5 max-w-md text-sm text-pretty text-muted transition-[opacity,translate] duration-500 ease-out motion-reduce:transition-none ${
          tried ? "translate-y-0 opacity-100" : "pointer-events-none translate-y-1 opacity-0"
        }`}
      >
        Allow your browser to open {site.name}. Nothing happened?{" "}
        <Link href={routes.download} tabIndex={tried ? 0 : -1} className={linkClass}>
          Get {site.name}
        </Link>{" "}
        (it&apos;s free), then click again.
      </p>
    </>
  );
}

/** While the feed loads: the bar, the stage and the title's shapes, pulsing. */
function WallpaperSkeleton() {
  const bone = "animate-pulse rounded-full bg-surface";
  return (
    <div aria-hidden className="flex min-h-svh flex-col justify-center pt-20 pb-10">
      <div className="mx-auto w-full max-w-[88rem] px-4 sm:px-8">
        <div className={`mx-auto aspect-[16/9] animate-pulse rounded-[2rem] bg-surface max-sm:aspect-[8/7] ${stageWidth}`} />
        <div className="flex justify-center">
          <div className={`mt-8 h-13 w-56 ${bone}`} />
        </div>
      </div>
    </div>
  );
}

/**
 * A link from an earlier hour: the photo isn't in the feed any more, so there's nothing to show it with. Oriel only
 * needs its id, so it can still be set; or on to this hour's.
 */
function Gone({ id }: { id: string }) {
  return (
    <div className="flex min-h-[70svh] flex-col items-center justify-center px-6 pt-28 pb-16 text-center">
      <h1 className="text-3xl font-semibold tracking-tight text-balance sm:text-4xl">{explorePage.gone}</h1>
      <p className="mt-4 max-w-md text-pretty text-muted">{explorePage.goneBody}</p>
      <SetWallpaper photo={{ id }} />
      <Link
        href={routes.explore}
        className="mt-2 text-sm font-medium underline-offset-4 transition-colors hover:underline"
      >
        {explorePage.seeNew}
      </Link>
    </div>
  );
}
