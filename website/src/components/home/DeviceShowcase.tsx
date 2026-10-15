"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from "react";
import photos from "@/data/today.json";
import { showcase } from "@/content/home";

/**
 * What Oriel does, shown rather than told: an iMac (with the new tab extension open in Chrome), a MacBook and an iPhone with the
 * same photo, which changes on all of them at once. Each screen crops the photo to its own
 * shape, as the apps do. Drawn in CSS with hotlinked Unsplash photos, credited under the picture.
 */

const utm = "utm_source=oriel&utm_medium=referral";
const linkClass =
  "underline underline-offset-4 transition-colors hover:text-ink";
const slides = showcase.photoIds
  .map((id) => photos.find((p) => p.id === id))
  .filter((p) => p !== undefined);
const every = 6000;

const url = (path: string, w: number, h: number) =>
  `https://images.unsplash.com/${path}?w=${w}&h=${h}&fit=crop&crop=entropy&q=75&fm=jpg&auto=format`;

/** The time now, from the first render in the browser (null before, so the static HTML has no stale time), each minute. */
function useNow(): Date | null {
  const [now, setNow] = useState<Date | null>(null);
  useEffect(() => {
    let timer: number;
    const tick = () => {
      const d = new Date();
      setNow(d);
      timer = window.setTimeout(
        tick,
        60_000 - (d.getSeconds() * 1000 + d.getMilliseconds()),
      );
    };
    tick();
    return () => window.clearTimeout(timer);
  }, []);
  return now;
}

/** macOS's menu bar clock: "Wed Oct 14  9:41 AM". */
function menuBarDate(d: Date) {
  const day = d
    .toLocaleDateString("en-US", {
      weekday: "short",
      month: "short",
      day: "numeric",
    })
    .replace(",", "");
  return `${day}  ${d.toLocaleTimeString("en-US", { hour: "numeric", minute: "2-digit" })}`;
}

/** The extension's clock and greeting (extension/src/lib/clock.ts: 24-hour, as it starts out, and no name). */
const clockText = (d: Date) =>
  d.toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  });
function greetingText(d: Date) {
  const h = d.getHours();
  return `${h < 5 ? "Good night" : h < 12 ? "Good morning" : h < 18 ? "Good afternoon" : "Good evening"}.`;
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

/** Tells the showcase a photo failed to load (any screen), so it is hidden and skipped from then on. */
const ReportBroken = createContext<(id: string) => void>(() => {});

export function DeviceShowcase() {
  const [index, setIndex] = useState(0);
  const now = useNow();

  useEffect(() => {
    if (
      slides.length < 2 ||
      window.matchMedia("(prefers-reduced-motion: reduce)").matches
    )
      return;
    const tick = window.setInterval(() => {
      if (!document.hidden) setIndex(nextShown);
    }, every);
    return () => window.clearInterval(tick);
  }, []);

  // A photo failed on some screen: from now on it is skipped, and if it is the one on screen, the next comes right away.
  const reportBroken = useCallback((id: string) => {
    broken.add(id);
    setIndex((i) => (slides[i]!.id === id ? nextShown(i) : i));
  }, []);

  const photo = slides[index];
  if (!photo) return null;

  return (
    <ReportBroken.Provider value={reportBroken}>
      <figure className="w-full">
        <div
          aria-hidden
          className="relative aspect-[16/9] w-full select-none [container-type:inline-size]"
        >
          {/* iMac, at the back: the menu bar, a browser on the new tab extension, the dock, and the logo on its chin. */}
          <div className="absolute top-0 left-[22%] w-[60%]">
            <div className="overflow-hidden rounded-[1.1cqw] bg-neutral-950 shadow-2xl ring-1 shadow-ink/25 ring-black/10 dark:ring-white/10">
              <Screen
                className="m-[0.5cqw] aspect-[16/9] rounded-[0.7cqw]"
                index={index}
                src={(p) => url(p, 1600, 900)}
              >
                <MenuBar
                  now={now}
                  app="Chrome"
                  menus={[
                    "File",
                    "Edit",
                    "View",
                    "History",
                    "Bookmarks",
                    "Profiles",
                    "Tab",
                    "Window",
                    "Help",
                  ]}
                  scale={1}
                />
                <Browser index={index} now={now} />
                <Dock size="1.9cqw" chrome />
              </Screen>
              <div className="flex h-[4.2cqw] items-center justify-center bg-gradient-to-b from-neutral-200 to-neutral-300 dark:from-neutral-400 dark:to-neutral-500">
                {/* eslint-disable-next-line @next/next/no-img-element -- the small logo, decorative */}
                <img
                  draggable={false}
                  src="/logo.png"
                  alt=""
                  className="size-[1.6cqw] opacity-80"
                />
              </div>
            </div>
            <div className="mx-auto h-[4cqw] w-[18%] bg-gradient-to-b from-neutral-400 to-neutral-300 [clip-path:polygon(8%_0,92%_0,100%_100%,0_100%)] dark:from-neutral-600 dark:to-neutral-500" />
          </div>

          {/* MacBook Pro, front left: a thin even bezel with the notch, a space black deck a little wider than the lid.
            On its screen the Finder menu bar and the dock, Oriel in both. */}
          <div className="absolute bottom-[2%] left-0 w-[44%]">
            <div className="relative mx-auto w-[90%] rounded-t-[1.4cqw] bg-neutral-950 p-[0.55cqw] pb-[0.7cqw] shadow-2xl ring-1 shadow-ink/25 ring-neutral-700/60">
              <Screen
                className="aspect-[16/10] rounded-t-[0.9cqw] rounded-b-[0.2cqw]"
                index={index}
                src={(p) => url(p, 1200, 750)}
              >
                <MenuBar
                  now={now}
                  app="Finder"
                  menus={["File", "Edit", "View", "Go", "Window", "Help"]}
                  scale={0.72}
                  battery
                />
                <Dock size="1.5cqw" />
              </Screen>
              <span className="absolute top-0 left-1/2 h-[1.3cqw] w-[13%] -translate-x-1/2 rounded-b-[0.5cqw] bg-neutral-950" />
            </div>
            <div className="relative h-[1.2cqw] rounded-t-[0.2cqw] rounded-b-[1.2cqw] bg-gradient-to-b from-neutral-600 to-neutral-800 shadow-xl shadow-ink/25">
              <div className="mx-auto h-[0.4cqw] w-[14%] rounded-b-[0.5cqw] bg-neutral-900/70" />
              <span className="absolute -bottom-[0.25cqw] left-[7%] h-[0.25cqw] w-[5%] rounded-b-full bg-neutral-700" />
              <span className="absolute right-[7%] -bottom-[0.25cqw] h-[0.25cqw] w-[5%] rounded-b-full bg-neutral-700" />
            </div>
          </div>

          {/* iPhone, front right: the home screen, Oriel's icon with its name under it, and the home indicator. */}
          <div className="absolute right-[9%] bottom-0 w-[15.5%]">
            <div className="aspect-[9/19.5] rounded-[2.6cqw] bg-neutral-950 p-[0.45cqw] shadow-2xl ring-1 shadow-ink/25 ring-black/10 dark:ring-white/10">
              <Screen
                className="size-full rounded-[2.2cqw]"
                index={index}
                src={(p) => url(p, 540, 1170)}
              >
                <StatusBar now={now} />
                <div className="absolute top-[4.2cqw] left-[1.4cqw] flex flex-col items-center gap-[0.4cqw]">
                  <AppIcon size="2.3cqw" />
                  <span className="text-[0.65cqw] leading-none text-white drop-shadow">
                    Oriel
                  </span>
                </div>
                {/* The home indicator. */}
                <span className="absolute bottom-[0.5cqw] left-1/2 h-[0.3cqw] w-[5.2cqw] -translate-x-1/2 rounded-full bg-white/90" />
              </Screen>
            </div>
          </div>
        </div>

        <figcaption className="mt-6 text-center text-sm text-muted">
          <span className="sr-only">
            The same photo on an iMac, a MacBook and an iPhone.{" "}
          </span>
          <Swap
            active={index}
            className="justify-items-center"
            items={slides.map((p) => (
              <>
                Photo by{" "}
                <a
                  href={`https://unsplash.com/@${p.handle}?${utm}`}
                  target="_blank"
                  rel="noopener noreferrer"
                  className={linkClass}
                >
                  {p.by}
                </a>{" "}
                on{" "}
                <a
                  href={`https://unsplash.com/photos/${p.id}?${utm}`}
                  target="_blank"
                  rel="noopener noreferrer"
                  className={linkClass}
                >
                  Unsplash
                </a>
              </>
            ))}
          />
        </figcaption>
      </figure>
    </ReportBroken.Provider>
  );
}

/** A device's black bezel and its screen: the photos stacked, the current one shown, all crossfading together. */
function Screen({
  className,
  index,
  src,
  children,
}: {
  className: string;
  index: number;
  src: (path: string) => string;
  children?: ReactNode;
}) {
  const reportBroken = useContext(ReportBroken);
  return (
    <div className={`relative overflow-hidden bg-neutral-950 ${className}`}>
      {/* Only the photo shown, the one before (still fading out) and the next (loading ahead), not the whole set. */}
      {slides.map((p, i) => {
        const near =
          i === index ||
          (i + 1) % slides.length === index ||
          i === (index + 1) % slides.length;
        return (
          near && (
            // eslint-disable-next-line @next/next/no-img-element -- hotlinked Unsplash photos: there is no image server on static hosting
            <img
              draggable={false}
              key={p.id}
              src={src(p.path)}
              alt=""
              onError={(e) => {
                // No broken-image icon: hide it, and skip this photo on every screen.
                e.currentTarget.style.visibility = "hidden";
                reportBroken(p.id);
              }}
              className={`absolute inset-0 size-full object-cover transition-opacity duration-1000 ease-out motion-reduce:transition-none ${
                i === index ? "opacity-100" : "opacity-0"
              }`}
            />
          )
        );
      })}
      {children}
    </div>
  );
}

/**
 * The iPhone's status bar, laid out as iOS does: the Dynamic Island in the middle, the time centred in the space on its
 * left, and signal, Wi-Fi and a full battery centred in the space on its right.
 */
function StatusBar({ now }: { now: Date | null }) {
  return (
    <div className="absolute inset-x-0 top-[0.65cqw] flex h-[1.3cqw] items-center text-white">
      <span className="flex-1 text-center text-[0.78cqw] leading-none font-semibold tracking-tight tabular-nums">
        {now &&
          now
            .toLocaleTimeString("en-US", { hour: "numeric", minute: "2-digit" })
            .replace(/\s?[AP]M$/i, "")}
      </span>
      <span className="h-full w-[4.4cqw] rounded-full bg-black" />
      <span className="flex flex-1 items-center justify-center gap-[0.22cqw]">
        <span className="flex h-[0.55cqw] items-end gap-[0.07cqw]">
          {[40, 60, 80, 100].map((h) => (
            <span
              key={h}
              className="w-[0.14cqw] rounded-[0.04cqw] bg-white"
              style={{ height: `${h}%` }}
            />
          ))}
        </span>
        <svg
          viewBox="0 0 24 18"
          className="h-[0.55cqw] w-auto fill-current"
          aria-hidden
        >
          <path d="M12 18l-3.6-4.3a5.6 5.6 0 0 1 7.2 0zM4.6 9.2a11.4 11.4 0 0 1 14.8 0l-2.3 2.7a7.8 7.8 0 0 0-10.2 0zM.8 4.6a17.3 17.3 0 0 1 22.4 0l-2.3 2.7a13.7 13.7 0 0 0-17.8 0z" />
        </svg>
        <span className="flex items-center gap-[0.04cqw]">
          <span className="h-[0.52cqw] w-[1.05cqw] rounded-[0.16cqw] bg-white" />
          <span className="h-[0.2cqw] w-[0.06cqw] rounded-r-full bg-white/50" />
        </span>
      </span>
    </div>
  );
}

/** Oriel's app icon: the black mark on a white tile. */
function AppIcon({ size }: { size: string }) {
  return (
    <span
      className="flex shrink-0 items-center justify-center rounded-[22%] bg-white shadow-sm"
      style={{ width: size, height: size }}
    >
      {/* eslint-disable-next-line @next/next/no-img-element -- the small logo, decorative */}
      <img draggable={false} src="/logo.png" alt="" className="size-[60%]" />
    </span>
  );
}

/**
 * The Mac's menu bar along the top of the screen, as macOS draws it: the Apple menu, the front app's menus, and on the
 * right Oriel's own icon, Wi-Fi, the battery on a laptop, and the date. Sized in em, so `scale` shrinks it all.
 */
function MenuBar({
  now,
  app,
  menus,
  scale,
  battery = false,
}: {
  now: Date | null;
  app: string;
  menus: string[];
  scale: number;
  battery?: boolean;
}) {
  return (
    <div
      className="absolute inset-x-0 top-0 z-10 flex h-[2.1em] items-center gap-[1.3em] rounded-tl-[inherit] rounded-tr-[inherit] bg-black/15 px-[1.2em] leading-none whitespace-nowrap text-white backdrop-blur-md [text-shadow:0_0_2px_rgb(0_0_0/0.3)]"
      style={{ fontSize: `${0.62 * scale}cqw` }}
    >
      <svg
        viewBox="0 0 17 20"
        className="h-[1.2em] w-auto fill-current"
        aria-hidden
      >
        <path d="M14.1 10.6c0-2.6 2.1-3.8 2.2-3.9-1.2-1.8-3.1-2-3.7-2-1.6-.2-3.1.9-3.9.9-.8 0-2-.9-3.4-.9C3.6 4.8 2 5.8 1.1 7.3c-1.8 3.1-.5 7.8 1.3 10.3.9 1.2 1.9 2.6 3.2 2.5 1.3-.1 1.8-.8 3.3-.8 1.6 0 2 .8 3.4.8 1.4 0 2.3-1.3 3.1-2.5 1-1.4 1.4-2.8 1.4-2.9 0 0-2.7-1-2.7-4.1zM11.6 3c.7-.9 1.2-2 1-3.2-1 0-2.3.7-3 1.6-.7.8-1.2 2-1.1 3.1 1.2.1 2.3-.6 3.1-1.5z" />
      </svg>
      <span className="font-semibold">{app}</span>
      {menus.map((m) => (
        <span key={m}>{m}</span>
      ))}
      <span className="ml-auto flex items-center gap-[1.2em]">
        {/* Oriel lives in the menu bar: its mark, in white. */}
        {/* eslint-disable-next-line @next/next/no-img-element -- the small logo, decorative */}
        <img
          draggable={false}
          src="/logo.png"
          alt=""
          className="h-[1.25em] w-auto invert"
        />
        <svg
          viewBox="0 0 24 18"
          className="h-[1.1em] w-auto fill-current"
          aria-hidden
        >
          <path d="M12 18l-3.6-4.3a5.6 5.6 0 0 1 7.2 0zM4.6 9.2a11.4 11.4 0 0 1 14.8 0l-2.3 2.7a7.8 7.8 0 0 0-10.2 0zM.8 4.6a17.3 17.3 0 0 1 22.4 0l-2.3 2.7a13.7 13.7 0 0 0-17.8 0z" />
        </svg>
        {battery && (
          <span className="flex items-center gap-[0.1em]">
            <span className="flex h-[1.05em] w-[2.1em] rounded-[0.3em] p-[0.15em] ring-1 ring-white/70">
              <span className="w-[75%] rounded-[0.15em] bg-white" />
            </span>
            <span className="h-[0.4em] w-[0.12em] rounded-r-full bg-white/70" />
          </span>
        )}
        <span className="tabular-nums">{now && menuBarDate(now)}</span>
      </span>
    </div>
  );
}

/** The Mac's dock: Oriel, and Chrome (open, with its dot) on the iMac. */
function Dock({ size, chrome = false }: { size: string; chrome?: boolean }) {
  const tile = { width: size, height: size };
  return (
    <div className="absolute bottom-[0.6cqw] left-1/2 flex -translate-x-1/2 gap-[0.45cqw] rounded-[0.9cqw] bg-white/20 p-[0.4cqw] ring-1 ring-white/20 backdrop-blur-md">
      <AppIcon size={size} />
      {chrome && (
        <span
          className="relative flex items-center justify-center rounded-[22%] bg-white shadow-sm"
          style={tile}
        >
          <ChromeLogo className="size-[78%]" />
          <span className="absolute -bottom-[0.3cqw] size-[0.2cqw] rounded-full bg-white/90" />
        </span>
      )}
    </div>
  );
}

/** Chrome's mark: red, yellow and green around a white ring and a blue middle. */
function ChromeLogo({ className }: { className: string }) {
  return (
    <span
      className={`grid place-items-center rounded-full ${className}`}
      style={{
        background:
          "conic-gradient(from -60deg, #ea4335 0 120deg, #fbbc04 0 240deg, #34a853 0 360deg)",
      }}
    >
      <span className="grid size-[46%] place-items-center rounded-full bg-white">
        <span className="size-[78%] rounded-full bg-[#4285f4]" />
      </span>
    </span>
  );
}

const stroke =
  "fill-none stroke-current stroke-2 [stroke-linecap:round] [stroke-linejoin:round]";
const toolIcon = `h-[0.85cqw] w-auto shrink-0 ${stroke}`;

/**
 * Chrome (dark) on Oriel's new tab page, drawn like the real one: the tab strip and the toolbar, then the extension as
 * it looks without an account. Its photo stack changes with the others.
 */
function Browser({ index, now }: { index: number; now: Date | null }) {
  return (
    <div className="absolute inset-x-[7%] top-[8.5%] bottom-[15%] flex flex-col overflow-hidden rounded-[0.9cqw] bg-[#35363a] shadow-2xl ring-1 shadow-black/50 ring-white/15">
      {/* Tab strip: the window buttons, one tab with its curved feet, the new tab "+", and tab search on the right. */}
      <div className="flex h-[2.1cqw] shrink-0 items-end bg-[#202124] pr-[0.7cqw] pl-[0.8cqw] text-[0.62cqw] leading-none whitespace-nowrap text-[#e8eaed]">
        <span className="mb-[0.75cqw] flex gap-[0.4cqw]">
          {["bg-[#ff5f57]", "bg-[#febc2e]", "bg-[#28c840]"].map((c) => (
            <span key={c} className={`size-[0.6cqw] rounded-full ${c}`} />
          ))}
        </span>
        <span className="relative ml-[1.1cqw] flex h-[1.55cqw] w-[11cqw] items-center gap-[0.45cqw] rounded-t-[0.5cqw] bg-[#35363a] px-[0.7cqw] before:absolute before:bottom-0 before:-left-[0.5cqw] before:size-[0.5cqw] before:bg-[radial-gradient(circle_at_0_0,transparent_0.5cqw,#35363a_calc(0.5cqw+0.5px))] after:absolute after:-right-[0.5cqw] after:bottom-0 after:size-[0.5cqw] after:bg-[radial-gradient(circle_at_100%_0,transparent_0.5cqw,#35363a_calc(0.5cqw+0.5px))]">
          {/* eslint-disable-next-line @next/next/no-img-element -- the small logo, decorative */}
          <img
            draggable={false}
            src="/logo.png"
            alt=""
            className="size-[0.8cqw] rounded-[0.15cqw] bg-white p-[0.05cqw]"
          />
          New Tab
          <svg
            viewBox="0 0 24 24"
            className={`ml-auto h-[0.7cqw] w-auto ${stroke}`}
          >
            <path d="M18 6 6 18M6 6l12 12" />
          </svg>
        </span>
        <svg
          viewBox="0 0 24 24"
          className={`mb-[0.42cqw] ml-[0.7cqw] ${toolIcon}`}
        >
          <path d="M12 5v14M5 12h14" />
        </svg>
        <svg viewBox="0 0 24 24" className={`mb-[0.42cqw] ml-auto ${toolIcon}`}>
          <path d="m6 9 6 6 6-6" />
        </svg>
      </div>

      {/* Toolbar: back, forward (greyed), reload, the omnibox, extensions, the profile and the menu. */}
      <div className="flex h-[1.9cqw] shrink-0 items-center gap-[0.75cqw] border-b border-black/40 px-[0.75cqw] text-[#e8eaed]">
        <svg viewBox="0 0 24 24" className={toolIcon}>
          <path d="M19 12H5M12 19l-7-7 7-7" />
        </svg>
        <svg viewBox="0 0 24 24" className={`opacity-40 ${toolIcon}`}>
          <path d="M5 12h14M12 5l7 7-7 7" />
        </svg>
        <svg viewBox="0 0 24 24" className={toolIcon}>
          <path d="M21 12a9 9 0 1 1-3-6.7L21 8" />
          <path d="M21 3v5h-5" />
        </svg>
        <span className="flex h-[1.3cqw] flex-1 items-center gap-[0.5cqw] rounded-full bg-[#202124] px-[0.7cqw] text-[0.62cqw] whitespace-nowrap text-[#9aa0a6]">
          <GoogleG />
          Search Google or type a URL
        </span>
        <svg viewBox="0 0 24 24" className={toolIcon}>
          <path d="M19.4 7.5a1.5 1.5 0 0 0-2.4-1.3l-.8.6-1.8-1.8a2 2 0 0 0-2.8 0l-1.6 1.6.6.8a1.5 1.5 0 1 1-2.4 1.8l-.6-.8L6 10a2 2 0 0 0 0 2.8l1.8 1.8-.6.8a1.5 1.5 0 1 0 2.4 1.8l.6-.8 1.8 1.8a2 2 0 0 0 2.8 0l4-4a2 2 0 0 0 0-2.8l-1.8-1.8.8-.6a1.5 1.5 0 0 0 .6-1.5z" />
        </svg>
        <span className="size-[1.05cqw] shrink-0 rounded-full bg-gradient-to-br from-sky-300 to-indigo-500" />
        <svg viewBox="0 0 24 24" className={toolIcon}>
          <circle cx="12" cy="5" r="1" />
          <circle cx="12" cy="12" r="1" />
          <circle cx="12" cy="19" r="1" />
        </svg>
      </div>

      <Screen className="flex-1" index={index} src={(p) => url(p, 1400, 760)}>
        <NewTab now={now} />
      </Screen>
    </div>
  );
}

/** Google's "G", for the omnibox. */
function GoogleG() {
  return (
    <svg
      viewBox="0 0 24 24"
      className="h-[0.75cqw] w-auto shrink-0"
      aria-hidden
    >
      <path
        fill="#4285f4"
        d="M22.6 12.3c0-.8-.1-1.6-.2-2.3H12v4.3h5.9a5 5 0 0 1-2.2 3.3v2.8h3.6c2.1-1.9 3.3-4.8 3.3-8.1z"
      />
      <path
        fill="#34a853"
        d="M12 23c3 0 5.5-1 7.3-2.7l-3.6-2.8c-1 .7-2.2 1.1-3.7 1.1-2.9 0-5.3-1.9-6.2-4.5H2.1v2.9A11 11 0 0 0 12 23z"
      />
      <path
        fill="#fbbc04"
        d="M5.8 14.1a6.6 6.6 0 0 1 0-4.2V7H2.1a11 11 0 0 0 0 9.9z"
      />
      <path
        fill="#ea4335"
        d="M12 5.4c1.6 0 3.1.6 4.2 1.7l3.2-3.2A11 11 0 0 0 2.1 7l3.7 2.9C6.7 7.3 9.1 5.4 12 5.4z"
      />
    </svg>
  );
}

/**
 * The extension's page scaled down, the same pieces in the same places as extension/newtab.html without an account:
 * the settings button, the clock and greeting, the search bar, the photo info button, a quote and the shuffle button.
 */
function NewTab({ now }: { now: Date | null }) {
  const soft =
    "grid size-[1.15cqw] place-items-center rounded-full border border-white/20 bg-white/15 backdrop-blur-sm";
  const glyph = `size-[0.6cqw] ${stroke}`;
  return (
    <div className="absolute inset-0 text-white [text-shadow:0_1px_3px_rgb(0_0_0/0.35)]">
      <span className={`absolute top-[0.55cqw] left-[0.55cqw] ${soft}`}>
        <svg viewBox="0 0 24 24" className={glyph}>
          <path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z" />
          <circle cx="12" cy="12" r="3" />
        </svg>
      </span>

      <div className="absolute inset-0 flex flex-col items-center justify-center pb-[1.5cqw]">
        <span className="h-[4.4cqw] text-[4.4cqw] leading-none font-semibold tracking-tight tabular-nums">
          {now && clockText(now)}
        </span>
        <span className="mt-[0.35cqw] h-[1cqw] text-[0.9cqw] leading-none font-medium">
          {now && greetingText(now)}
        </span>
        <span className="mt-[1.1cqw] flex h-[1.45cqw] w-[15cqw] items-center gap-[0.45cqw] rounded-full border border-white/25 bg-white/15 px-[0.7cqw] text-[0.55cqw] text-white/70 shadow-lg backdrop-blur-md [text-shadow:none]">
          <svg viewBox="0 0 24 24" className={`opacity-70 ${glyph}`}>
            <circle cx="11" cy="11" r="7" />
            <path d="m20 20-3.5-3.5" />
          </svg>
          Search the web
        </span>
      </div>

      <div className="absolute inset-x-[0.55cqw] bottom-[0.55cqw] flex items-end justify-between">
        <span className={soft}>
          <svg viewBox="0 0 24 24" className={glyph}>
            <circle cx="12" cy="12" r="10" />
            <path d="M12 16v-4M12 8h.01" />
          </svg>
        </span>
        <span className="pb-[0.2cqw] text-[0.45cqw] italic">
          Small steps, taken daily, move mountains.
        </span>
        <span className={soft}>
          <svg viewBox="0 0 24 24" className={glyph}>
            <path d="M21 12a9 9 0 1 1-3-6.7L21 8" />
            <path d="M21 3v5h-5" />
          </svg>
        </span>
      </div>
    </div>
  );
}

/**
 * Lines of text in one spot, the active one shown: the old one fades and slides up as the new one arrives from below.
 * All of them share one grid cell, so the box is as big as the longest and nothing around it moves.
 */
function Swap({
  items,
  active,
  className = "",
}: {
  items: ReactNode[];
  active: number;
  className?: string;
}) {
  return (
    <span className={`grid ${className}`}>
      {items.map((item, i) => (
        <span
          key={i}
          aria-hidden={i !== active}
          inert={i !== active}
          className={`col-start-1 row-start-1 transition-[opacity,translate,filter] duration-700 ease-out motion-reduce:transition-none ${
            i === active
              ? "translate-y-0 opacity-100 blur-0"
              : i < active
                ? "-translate-y-2 opacity-0 blur-[2px]"
                : "translate-y-2 opacity-0 blur-[2px]"
          }`}
        >
          {item}
        </span>
      ))}
    </span>
  );
}
