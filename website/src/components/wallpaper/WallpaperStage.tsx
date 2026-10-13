"use client";

import { useState, type ReactNode } from "react";
import Link from "next/link";
import { routes, site } from "@/config/site";
import { SiteFooter } from "@/components/layout/SiteFooter";
import { AnchorButton } from "@/components/ui/Button";
import { Container } from "@/components/ui/Container";
import { RevealImage } from "@/components/ui/RevealImage";

/**
 * The one-photo pages (/share and /today): the photo as wide as the header, one button that opens Oriel to set it,
 * and the footer, all on one screen without scrolling.
 */

export const linkClass = "underline underline-offset-4 transition-colors hover:text-ink";
/** The most the photo may take: whatever height is left, so the page and its footer fit the screen without scrolling. */
export const photoMaxHeight = "max-h-[clamp(10rem,calc(100dvh-22rem),50rem)]";
const bone = "animate-pulse rounded-full bg-surface";
const utm = "utm_source=oriel&utm_medium=referral";

/**
 * One frame for every state, so nothing jumps while the page loads. The middle is only what matters: the photo (with
 * its label on it) and one action. The quiet details (a hint after clicking, the credit) sit at the edges.
 */
export function Frame({ backdrop, photo, action, note }: { backdrop?: string; photo: ReactNode; action: ReactNode; note: ReactNode }) {
  return (
    <div className="relative flex min-h-dvh flex-col overflow-hidden">
      {/* The photo itself, very soft, so the page takes on its colours. A CSS blur on one small image: no extra work. */}
      {backdrop && <Backdrop key={backdrop} src={backdrop} />}
      <div className="flex flex-1 flex-col items-center justify-center pt-20 pb-4">
        {/* The same width as the header: from the logo to the end of its Download button (and as the wallpaper card on /me). */}
        <Container className="flex flex-col items-center text-center">
          <div className="w-full">{photo}</div>
          <div className="mt-5 flex min-h-12 w-full items-center justify-center text-sm text-muted text-pretty">{action}</div>
        </Container>
      </div>
      <p className="flex min-h-12 items-center justify-center px-5 pb-3 text-center text-sm text-muted text-pretty">{note}</p>
      <SiteFooter />
    </div>
  );
}

/** The soft background: fades in once its small image has arrived, instead of popping in. */
function Backdrop({ src }: { src: string }) {
  const [shown, setShown] = useState(false);
  return (
    // eslint-disable-next-line @next/next/no-img-element -- decorative, hotlinked from Unsplash
    <img
      src={src}
      alt=""
      aria-hidden
      ref={(el) => {
        if (el?.complete && el.naturalWidth > 0) setShown(true);
      }}
      onLoad={() => setShown(true)}
      className={`pointer-events-none absolute inset-0 -z-10 size-full scale-125 object-cover blur-3xl saturate-150 transition-opacity duration-[1400ms] ease-out motion-reduce:transition-none ${
        shown ? "opacity-30 dark:opacity-25" : "opacity-0"
      }`}
    />
  );
}

/** Until the photo's own shape is known: the most common shape of an Unsplash photo (3:2), same as the skeleton. */
const defaultRatio = 3 / 2;

/** While the page doesn't know its photo yet (it is picked in the browser): the same shapes, pulsing. */
export function WallpaperSkeleton() {
  return (
    <Frame
      photo={<div className={`w-full animate-pulse rounded-[2rem] bg-surface ${photoMaxHeight}`} style={{ aspectRatio: defaultRatio }} aria-hidden />}
      action={<div className={`${bone} h-12 w-full`} aria-hidden />}
      note={null}
    />
  );
}

/** `ratio` (width / height), when known up front, sizes the card exactly before the photo arrives. */
export type StagePhoto = { id: string; path: string; by: string; handle: string; ratio?: number };

/**
 * The photo with its label on the top left and the Unsplash credit at its foot (like the card on /me), and the
 * "Set as my wallpaper" button. On a phone the button becomes a line saying to open the page on a computer.
 */
export function WallpaperStage({
  photo,
  label,
  labelAs = "p",
  badge,
  appLink,
  onSet,
  again,
  phone,
  button = "Set as my wallpaper",
}: {
  photo: StagePhoto;
  label: string;
  /** "h1" when the label is the page's heading. */
  labelAs?: "h1" | "p";
  /** The round mark before the label: an initial, or an icon. */
  badge: ReactNode;
  appLink: string;
  onSet?: () => void;
  /** The end of the help line after clicking: "open this link again". */
  again: string;
  phone: boolean;
  /** The button's words. */
  button?: string;
}) {
  const [tried, setTried] = useState(false);
  // The card keeps its size the whole time: the photo's shape when known (from the data, or once it has loaded),
  // otherwise the skeleton's. It never collapses while the photo downloads, so nothing below it moves.
  const [ratio, setRatio] = useState(photo.ratio ?? defaultRatio);
  const [loaded, setLoaded] = useState(false);
  const Label = labelAs;
  const overlay = `transition-opacity duration-700 ease-out ${loaded ? "opacity-100" : "opacity-0"}`;
  const photoPage = `https://unsplash.com/photos/${photo.id}?${utm}`;
  // Without a handle (older share links) the name leads to the photo's page instead.
  const authorPage = photo.handle ? `https://unsplash.com/@${photo.handle}?${utm}` : photoPage;

  return (
    <Frame
      backdrop={`https://images.unsplash.com/${photo.path}?w=200&q=50&fm=jpg&auto=format&fit=crop`}
      photo={
        // The photo keeps its own proportions (it only crops a little on a very short screen, so the page never scrolls).
        <div
          // The shadow grows in with the photo: the card lifts off the page as it arrives.
          className={`relative isolate w-full overflow-hidden rounded-[2rem] bg-surface text-white ring-1 transition-[box-shadow,--tw-ring-color] duration-1000 ease-out motion-reduce:transition-none ${
            loaded ? "shadow-2xl shadow-ink/25 ring-ink/10" : "animate-pulse shadow-none ring-transparent"
          } ${photoMaxHeight}`}
          style={{ aspectRatio: ratio }}
        >
          <RevealImage
            key={photo.id}
            src={`https://images.unsplash.com/${photo.path}?w=1400&q=80&fm=jpg&auto=format&fit=crop`}
            alt={photo.by ? `Wallpaper photo by ${photo.by}` : "Wallpaper photo"}
            className="absolute inset-0 size-full object-cover"
            onLoaded={(img) => {
              if (img.naturalHeight > 0) setRatio(img.naturalWidth / img.naturalHeight);
              setLoaded(true);
            }}
          />
          {/* The label, the shade and the credit arrive with the photo, never on an empty card. */}
          <div aria-hidden className={`pointer-events-none absolute inset-0 bg-gradient-to-t from-black/75 via-black/0 to-black/20 ${overlay}`} />
          <Label className={`${overlay} absolute top-5 left-5 inline-flex items-center gap-2 rounded-full bg-black/35 py-1.5 pr-3.5 pl-1.5 text-[13px] font-medium text-white ring-1 ring-white/15 backdrop-blur-md sm:top-7 sm:left-7`}>
            <span aria-hidden className="flex size-6 items-center justify-center rounded-full bg-white text-xs font-semibold text-black">
              {badge}
            </span>
            {label}
          </Label>
          <p className={`${overlay} absolute inset-x-0 bottom-0 p-5 text-left text-sm text-white/80 sm:px-7 sm:pb-6`}>
            Photo{photo.by ? " by " : ""}
            {photo.by && (
              <a href={authorPage} target="_blank" rel="noopener noreferrer" className="font-medium text-white underline underline-offset-4">
                {photo.by}
              </a>
            )}{" "}
            on{" "}
            <a href={photoPage} target="_blank" rel="noopener noreferrer" className="font-medium text-white underline underline-offset-4">
              Unsplash
            </a>
          </p>
        </div>
      }
      action={
        phone ? (
          <span className="max-w-sm">Open this page on your Windows or Linux computer to set it as your wallpaper.</span>
        ) : (
          <AnchorButton
            href={appLink}
            onClick={() => {
              setTried(true);
              onSet?.();
            }}
            className="w-full py-3.5 text-base"
          >
            {button}
          </AnchorButton>
        )
      }
      note={
        phone ? null : (
          // Always in the layout (the line has a fixed height), so it eases in after the click without moving anything.
          <span
            aria-hidden={!tried}
            className={`max-w-md transition-[opacity,translate] duration-500 ease-out motion-reduce:transition-none ${
              tried ? "translate-y-0 opacity-100" : "pointer-events-none translate-y-1 opacity-0"
            }`}
          >
            Allow your browser to open {site.name}. Nothing happened?{" "}
            <Link href={routes.download} tabIndex={tried ? 0 : -1} className={linkClass}>
              Get {site.name}
            </Link>{" "}
            (it&apos;s free), then {again}.
          </span>
        )
      }
    />
  );
}
