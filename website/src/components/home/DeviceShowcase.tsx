"use client";

import { useCallback, useEffect, useState, type DragEvent } from "react";
import { RotateCcw } from "lucide-react";
import photos from "@/data/today.json";
import { showcase, sync } from "@/content/home";
import { Button } from "@/components/ui/Button";
import { DeviceStage, type StageImage } from "@/components/devices/DeviceStage";
import { TryYoursButton, useOwnPhoto } from "@/components/home/OwnPhoto";

/**
 * What Oriel does, shown rather than told: the same photo on an iMac, a MacBook and an iPhone, changing on all of them at
 * once, with hotlinked Unsplash photos credited on the iMac's new tab. A visitor's own photo ("Try yours", see OwnPhoto)
 * goes on every screen instead, and can also be dropped onto them.
 */

const utm = "utm_source=oriel&utm_medium=referral";
const linkClass =
  "underline underline-offset-4 transition-colors hover:text-ink";
const slides = showcase.photoIds
  .map((id) => photos.find((p) => p.id === id))
  .filter((p) => p !== undefined);
const every = 6000;

const images: StageImage[] = slides.map((p) => ({
  key: p.id,
  src: (w, h) =>
    `https://images.unsplash.com/${p.path}?w=${w}&h=${h}&fit=crop&crop=entropy&q=75&fm=jpg&auto=format`,
}));

type Photo = (typeof slides)[number];

/**
 * The credit Unsplash asks for, with links to the photographer and the photo. On the iMac's screen (hidden from screen
 * readers, which get it in the caption) its links stay out of the tab order.
 */
function Credit({ photo, onScreen = false }: { photo: Photo; onScreen?: boolean }) {
  const tab = onScreen ? -1 : undefined;
  return (
    <>
      Photo by{" "}
      <a
        href={`https://unsplash.com/@${photo.handle}?${utm}`}
        target="_blank"
        rel="noopener noreferrer"
        tabIndex={tab}
        className={linkClass}
      >
        {photo.by}
      </a>{" "}
      on{" "}
      <a
        href={`https://unsplash.com/photos/${photo.id}?${utm}`}
        target="_blank"
        rel="noopener noreferrer"
        tabIndex={tab}
        className={linkClass}
      >
        Unsplash
      </a>
    </>
  );
}

/** Photos that failed to load in this visit. */
const broken = new Set<string>();

/** The next photo after `i` that hasn't failed (or `i` itself if every other one has). */
function nextShown(i: number) {
  for (let step = 1; step < slides.length; step++) {
    const j = (i + step) % slides.length;
    if (!broken.has(slides[j]!.id)) return j;
  }
  return i;
}

/** True when a drag carries files (not text or a link from the page). */
const hasFiles = (e: DragEvent) => e.dataTransfer.types.includes("Files");

export function DeviceShowcase() {
  const [index, setIndex] = useState(0);
  const own = useOwnPhoto();
  const [dragging, setDragging] = useState(false);

  // Oriel's photos take turns, except while the visitor's own is up.
  useEffect(() => {
    if (
      own.url ||
      slides.length < 2 ||
      window.matchMedia("(prefers-reduced-motion: reduce)").matches
    )
      return;
    const tick = window.setInterval(() => {
      if (!document.hidden) setIndex(nextShown);
    }, every);
    return () => window.clearInterval(tick);
  }, [own.url]);

  // A photo failed on some screen: from now on it is skipped, and if it is the one on screen, the next comes right away.
  // The visitor's own failing means the browser can't read that file.
  const { url: ownUrl, onError: ownFailed } = own;
  const reportBroken = useCallback(
    (key: string) => {
      if (key === ownUrl) return ownFailed();
      broken.add(key);
      setIndex((i) => (slides[i]!.id === key ? nextShown(i) : i));
    },
    [ownUrl, ownFailed],
  );

  const photo = slides[index];
  if (!photo) return null;

  return (
    <figure className="w-full">
      <div
        onDragOver={(e) => {
          if (!hasFiles(e)) return;
          e.preventDefault();
          setDragging(true);
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={(e) => {
          if (!hasFiles(e)) return;
          e.preventDefault();
          setDragging(false);
          own.show(e.dataTransfer.files[0]);
        }}
        className={`rounded-[2rem] outline-2 outline-offset-[1.5rem] transition-[outline-color] duration-200 ${
          dragging ? "outline-dashed outline-accent" : "outline-transparent"
        }`}
      >
        <DeviceStage
          images={own.url ? [{ key: own.url, src: () => own.url! }] : images}
          index={own.url ? 0 : index}
          onBroken={reportBroken}
          credit={own.url ? null : <Credit photo={photo} onScreen />}
        />
      </div>

      {/* Under the devices: "Try yours", and the way back once it's used. The credit is on the iMac's new tab. */}
      <div className="mt-8 flex flex-wrap justify-center gap-2.5">
        <TryYoursButton />
        {own.url && (
          <Button variant="secondary" onClick={own.reset}>
            <RotateCcw size={14} aria-hidden />
            {sync.reset}
          </Button>
        )}
      </div>
      <figcaption aria-live="polite" className="sr-only">
        {sync.label}. {own.url ? null : <Credit photo={photo} />}
      </figcaption>
      {own.failed && (
        <p role="alert" className="mt-4 text-center text-xs text-muted">
          {sync.failed}
        </p>
      )}
    </figure>
  );
}
