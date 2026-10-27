"use client";

import {
  createContext,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";

/**
 * Oriel's devices, drawn in CSS to their real proportions: a 24-inch iMac (with the new tab extension open in Chrome), a
 * 14-inch MacBook Pro and an iPhone 17 Pro, macOS 26 and iOS 26 on their screens. Every screen shows the same photo,
 * cropped to its own shape as the apps do, and they all crossfade together when it changes. Used by the home page (its
 * photos in turn, or a visitor's own) and by a wallpaper on /explore.
 */

/** One picture for the screens: `src` gives its address at a screen's size (in pixels), cropped to that shape. */
export type StageImage = { key: string; src: (width: number, height: number) => string };

/** What every screen shows: the images, which one is up, and who to tell when one can't be loaded. */
const Stage = createContext<{
  images: StageImage[];
  index: number;
  onBroken: (key: string) => void;
}>({ images: [], index: 0, onBroken: () => {} });

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
  return `${day}  ${d.toLocaleTimeString("en-US", { hour: "numeric", minute: "2-digit" })}`;
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

/**
 * The three devices on one 16:9 stage, the iMac at the back between the MacBook and the iPhone. On a phone the same
 * scene is drawn tighter: every device grows by the same amount (so they keep their proportions) on a stage a little
 * taller than wide, the iMac's screen in full view and the MacBook and the iPhone in front of its chin.
 * `credit` goes on the iMac's new tab (in the quote's place); without one the quote stays.
 */
export function DeviceStage({
  images,
  index,
  onBroken = () => {},
  credit = null,
}: {
  images: StageImage[];
  index: number;
  onBroken?: (key: string) => void;
  credit?: ReactNode;
}) {
  const now = useNow();
  return (
    <Stage.Provider value={{ images, index, onBroken }}>
      <div
        aria-hidden
        className="relative aspect-[16/9] w-full select-none [container-type:inline-size] max-sm:aspect-[8/7]"
      >
      {/* iMac 24-inch in black, at the back: the glass bezel with the camera, the chin with nothing on it, and the stand
        bent from one blade. On its screen the menu bar, Chrome on the new tab extension, the dock. */}
      <div className="absolute top-0 left-[22%] w-[60%] max-sm:left-[11%] max-sm:origin-top-left max-sm:scale-[1.3]">
        <div className="overflow-hidden rounded-[0.8cqw] shadow-2xl ring-[length:0.13cqw] shadow-ink/25 ring-[#2f2f32]">
          <div className="relative bg-black p-[1.45cqw]">
            <span className="absolute top-[0.62cqw] left-1/2 size-[0.22cqw] -translate-x-1/2 rounded-full bg-[#14182a] ring-[length:0.05cqw] ring-[#23262e]" />
            <Screen
              className="aspect-[16/9] rounded-[0.08cqw] ring-1 ring-black/25"
              size={[1600, 900]}
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
                height="0.92cqw"
              />
              <Browser credit={credit} />
              <Dock size="1.75cqw" chrome />
            </Screen>
          </div>
          <div className="h-[6.1cqw] border-t border-[#3d3d40] bg-gradient-to-b from-[#2e2e31] to-[#1c1c1e]" />
        </div>
        <div className="relative mx-auto h-[9.3cqw] w-[23%] bg-gradient-to-b from-[#0e0e0f] via-[#2c2c2f] via-25% to-[#232326]">
          <span className="absolute inset-x-0 bottom-0 h-[0.45cqw] rounded-b-[0.2cqw] bg-gradient-to-b from-[#4a4a4e] to-[#26262a]" />
        </div>
        <span className="mx-auto -mt-[0.25cqw] block h-[0.6cqw] w-[32%] rounded-[50%] bg-black/30 blur-[0.35cqw]" />
      </div>

      {/* MacBook Pro 14-inch in space black, front left: a thin even bezel with the notch (the menu bar is as tall as it),
        the hinge, and the base a little wider than the lid, with the scoop to open it and its feet. Finder in front. */}
      <div className="absolute bottom-[1.5%] left-0 w-[44%] max-sm:bottom-0 max-sm:origin-bottom-left max-sm:scale-[1.3]">
        <div className="relative mx-auto w-[90%] rounded-t-[1.05cqw] bg-black p-[0.55cqw] pb-[1cqw] shadow-2xl ring-[length:0.13cqw] shadow-ink/25 ring-[#2f2f32]">
          <Screen
            className="aspect-[3024/1964] rounded-t-[0.55cqw]"
            size={[1200, 780]}
          >
            <MenuBar
              now={now}
              app="Finder"
              menus={["File", "Edit", "View", "Go", "Window", "Help"]}
              height="0.9cqw"
              battery
            />
            {/* The notch, with the camera in it. */}
            <span className="absolute top-0 left-1/2 z-20 flex h-[0.9cqw] w-[12.4%] -translate-x-1/2 items-center justify-center rounded-b-[0.28cqw] bg-black">
              <span className="size-[0.2cqw] rounded-full bg-[#14182a] ring-[length:0.04cqw] ring-[#23262e]" />
            </span>
            <Dock size="1.6cqw" />
          </Screen>
        </div>
        <div className="mx-auto h-[0.28cqw] w-[84%] bg-gradient-to-b from-[#050505] to-[#232325]" />
        <div className="relative h-[1.05cqw] rounded-t-[0.12cqw] rounded-b-[0.9cqw] bg-gradient-to-b from-[#5b5b5f] via-[#2e2e31] via-30% to-[#19191b] shadow-xl shadow-ink/25">
          <div className="mx-auto h-[0.34cqw] w-[13%] rounded-b-[0.5cqw] bg-gradient-to-b from-[#161618] to-[#3a3a3d]" />
          <span className="absolute -bottom-[0.18cqw] left-[8%] h-[0.18cqw] w-[4.5%] rounded-b-full bg-[#2a2a2c]" />
          <span className="absolute right-[8%] -bottom-[0.18cqw] h-[0.18cqw] w-[4.5%] rounded-b-full bg-[#2a2a2c]" />
        </div>
      </div>

      {/* iPhone 17 Pro in black, front right: the dark frame with its buttons, a thin even black border, the Dynamic
        Island, and the home screen with Oriel's icon and the home indicator. */}
      <div className="absolute right-[9%] bottom-0 w-[15.5%] max-sm:right-[4%] max-sm:origin-bottom-right max-sm:scale-[1.3]">
        <div className="relative rounded-[2.42cqw] bg-gradient-to-br from-[#58585c] via-[#1c1c1e] to-[#46464a] p-[0.13cqw] shadow-2xl shadow-ink/30">
          {/* Left: the Action button and the volume buttons. Right: the side button and Camera Control. */}
          <PhoneButton side="left" top="17.5%" height="3.6%" />
          <PhoneButton side="left" top="24.5%" height="6.6%" />
          <PhoneButton side="left" top="33%" height="6.6%" />
          <PhoneButton side="right" top="25.5%" height="10%" />
          <PhoneButton side="right" top="52%" height="6.5%" flush />
          <div className="rounded-[2.29cqw] bg-black p-[0.25cqw]">
            <Screen
              className="aspect-[1206/2622] rounded-[2.04cqw]"
              size={[540, 1174]}
            >
              <StatusBar now={now} />
              <div className="absolute top-[2.55cqw] left-[0.95cqw] flex w-[2.3cqw] flex-col items-center gap-[0.2cqw]">
                <AppIcon size="2.3cqw" />
                <span className="text-[0.42cqw] leading-none font-medium text-white [text-shadow:0_0_0.15cqw_rgb(0_0_0/0.4)]">
                  Oriel
                </span>
              </div>
              {/* The home indicator. */}
              <span className="absolute bottom-[0.25cqw] left-1/2 h-[0.18cqw] w-[5.1cqw] -translate-x-1/2 rounded-full bg-white/90" />
            </Screen>
          </div>
        </div>
      </div>
      </div>
    </Stage.Provider>
  );
}

/**
 * A device's screen: the images stacked, the current one shown, all changing together. Only the one shown, the one
 * before (still leaving) and the next (loading ahead) are there, not the whole set.
 */
function Screen({
  className,
  size: [width, height],
  children,
}: {
  className: string;
  size: [number, number];
  children?: ReactNode;
}) {
  const { images, index, onBroken } = useContext(Stage);
  const n = images.length;
  return (
    <div className={`relative overflow-hidden bg-neutral-950 ${className}`}>
      {images.map((image, i) => {
        const near = i === index || (i + 1) % n === index || i === (index + 1) % n;
        return (
          near && (
            <ScreenImage
              key={image.key}
              src={image.src(width, height)}
              shown={i === index}
              onError={() => onBroken(image.key)}
            />
          )
        );
      })}
      {children}
    </div>
  );
}

/**
 * One picture on a screen. It arrives softly once it has loaded: from a little larger and blurred to sharp at its size,
 * fading in; the one before leaves the same way. With reduced motion it simply appears.
 */
function ScreenImage({ src, shown, onError }: { src: string; shown: boolean; onError: () => void }) {
  const [loaded, setLoaded] = useState(false);
  const visible = shown && loaded;
  return (
    // eslint-disable-next-line @next/next/no-img-element -- hotlinked Unsplash photos: there is no image server on static hosting
    <img
      draggable={false}
      src={src}
      alt=""
      ref={(el) => {
        if (el?.complete && el.naturalWidth > 0) setLoaded(true);
      }}
      onLoad={() => setLoaded(true)}
      onError={(e) => {
        // No broken-image icon: hide it, and let the page skip it.
        e.currentTarget.style.visibility = "hidden";
        onError();
      }}
      className={`absolute inset-0 size-full object-cover transition-[opacity,scale,filter] duration-[1600ms] ease-[cubic-bezier(0.22,1,0.36,1)] will-change-[opacity,scale,filter] motion-reduce:transition-none ${
        // Only the picture in view takes the pointer: the ones before and after are stacked over it, invisible, and
        // would otherwise get its right-click ("Copy image" copying the next wallpaper).
        visible ? "blur-0 scale-100 opacity-100" : "pointer-events-none blur-[16px] scale-110 opacity-0"
      }`}
    />
  );
}

/** One of the iPhone's buttons, standing out of the frame a little (Camera Control sits nearly flush, and darker). */
function PhoneButton({
  side,
  top,
  height,
  flush = false,
}: {
  side: "left" | "right";
  top: string;
  height: string;
  flush?: boolean;
}) {
  return (
    <span
      className={`absolute ${side === "left" ? "rounded-l-[0.06cqw]" : "rounded-r-[0.06cqw]"} ${
        flush
          ? "w-[0.07cqw] bg-[#2a2a2d]"
          : "w-[0.12cqw] bg-gradient-to-b from-[#55555a] via-[#222225] to-[#4a4a4e]"
      }`}
      style={{ top, height, [side]: flush ? "-0.04cqw" : "-0.09cqw" }}
    />
  );
}

/** Wi-Fi as Apple draws it on the status and menu bars: two arcs with round ends over a wedge, all at full strength. */
function WifiGlyph({ className }: { className: string }) {
  return (
    <svg viewBox="0 0 24 18" className={className} aria-hidden>
      <path
        d="M2.8 7.3a13 13 0 0 1 18.4 0M6.3 10.8a8 8 0 0 1 11.4 0"
        fill="none"
        stroke="currentColor"
        strokeWidth="2.7"
        strokeLinecap="round"
      />
      <path
        d="M12 16.6 9.6 14.2a3.4 3.4 0 0 1 4.8 0z"
        fill="currentColor"
        stroke="currentColor"
        strokeWidth="1.6"
        strokeLinejoin="round"
      />
    </svg>
  );
}

/** A battery as Apple draws it: a faint outline, the charge inside, and the terminal centred on its right end. */
function BatteryGlyph({
  level,
  className,
}: {
  level: number;
  className: string;
}) {
  return (
    <svg viewBox="0 0 28 13" className={className} aria-hidden>
      <rect
        x="0.75"
        y="0.75"
        width="23.5"
        height="11.5"
        rx="3.6"
        fill="none"
        stroke="currentColor"
        strokeOpacity="0.45"
        strokeWidth="1.5"
      />
      <rect
        x="2.5"
        y="2.5"
        width={20 * level}
        height="8"
        rx="2"
        fill="currentColor"
      />
      <path
        d="M25.8 4.6h.4a1.3 1.3 0 0 1 1.3 1.3v1.2a1.3 1.3 0 0 1-1.3 1.3h-.4z"
        fill="currentColor"
        fillOpacity="0.45"
      />
    </svg>
  );
}

/**
 * The iPhone's status bar, laid out as iOS 26 does: the Dynamic Island in the middle, the time centred in the space on its
 * left, and signal, Wi-Fi and a full battery centred in the space on its right, all at the island's height.
 */
function StatusBar({ now }: { now: Date | null }) {
  return (
    <div className="absolute inset-x-0 top-[0.4cqw] flex h-[1.36cqw] items-center text-white">
      <span className="flex-1 pl-[0.3cqw] text-center text-[0.62cqw] leading-none font-semibold tracking-tight tabular-nums">
        {now &&
          now
            .toLocaleTimeString("en-US", { hour: "numeric", minute: "2-digit" })
            .replace(/\s?[AP]M$/i, "")}
      </span>
      <span className="h-full w-[31.3%] rounded-full bg-black" />
      <span className="flex flex-1 items-center justify-center gap-[0.18cqw] pr-[0.3cqw]">
        <span className="flex h-[0.42cqw] items-end gap-[0.06cqw]">
          {[35, 55, 77, 100].map((h) => (
            <span
              key={h}
              className="w-[0.11cqw] rounded-[0.03cqw] bg-white"
              style={{ height: `${h}%` }}
            />
          ))}
        </span>
        <WifiGlyph className="h-[0.42cqw] w-auto" />
        <BatteryGlyph level={1} className="h-[0.46cqw] w-auto" />
      </span>
    </div>
  );
}

/** Oriel's app icon: the black mark on a white tile. */
function AppIcon({ size }: { size: string }) {
  return (
    <span
      className="flex shrink-0 items-center justify-center rounded-[22.4%] bg-white shadow-sm"
      style={{ width: size, height: size }}
    >
      {/* eslint-disable-next-line @next/next/no-img-element -- the small logo, decorative */}
      <img draggable={false} src="/logo.png" alt="" className="size-[60%]" />
    </span>
  );
}

/**
 * The Mac's menu bar along the top of the screen, as macOS 26 draws it: transparent over the photo, the Apple menu, the
 * front app's menus, and on the right Oriel's own icon, the battery on a laptop, Wi-Fi, Spotlight, Control Center and the
 * date. Sized in em, so the whole bar follows its font size.
 */
function MenuBar({
  now,
  app,
  menus,
  height,
  battery = false,
}: {
  now: Date | null;
  app: string;
  menus: string[];
  height: string;
  battery?: boolean;
}) {
  return (
    <div
      className="absolute inset-x-0 top-0 z-10 flex items-center gap-[1.35em] px-[1.1em] text-[0.48cqw] leading-none font-medium whitespace-nowrap text-white drop-shadow-[0_0_0.12cqw_rgb(0_0_0/0.45)]"
      style={{ height }}
    >
      <svg
        viewBox="0 0 24 24"
        className="-mt-[0.15em] h-[1.3em] w-auto fill-current"
        aria-hidden
      >
        <path d="M12.15 6.9c-.95 0-2.42-1.08-3.96-1.04-2.04.03-3.91 1.18-4.96 3.01-2.12 3.68-.55 9.1 1.52 12.09 1.01 1.45 2.2 3.09 3.79 3.04 1.52-.07 2.09-.99 3.93-.99 1.84 0 2.35.99 3.96.95 1.64-.03 2.68-1.48 3.68-2.95 1.16-1.69 1.63-3.33 1.66-3.42-.04-.01-3.18-1.22-3.22-4.86-.03-3.04 2.48-4.49 2.6-4.56-1.43-2.09-3.62-2.32-4.39-2.38-2-.16-3.67 1.09-4.61 1.09zM15.53 3.83c.84-1.01 1.4-2.43 1.25-3.83-1.21.05-2.66.8-3.53 1.82-.78.9-1.46 2.34-1.27 3.71 1.34.1 2.71-.69 3.55-1.7" />
      </svg>
      <span className="font-bold">{app}</span>
      {menus.map((m) => (
        <span key={m}>{m}</span>
      ))}
      <span className="ml-auto flex items-center gap-[1.15em]">
        {/* Oriel lives in the menu bar: its mark, in white. */}
        {/* eslint-disable-next-line @next/next/no-img-element -- the small logo, decorative */}
        <img
          draggable={false}
          src="/logo.png"
          alt=""
          className="h-[1.3em] w-auto invert"
        />
        {battery && <BatteryGlyph level={0.8} className="h-[1.05em] w-auto" />}
        <WifiGlyph className="h-[1.05em] w-auto" />
        <svg
          viewBox="0 0 24 24"
          className={`h-[1.1em] w-auto ${stroke} stroke-[2.6]`}
          aria-hidden
        >
          <circle cx="10" cy="10" r="7" />
          <path d="m20.5 20.5-5.5-5.5" />
        </svg>
        {/* Control Center: a switch off over a switch on. */}
        <svg viewBox="0 0 22 18" className="h-[1.05em] w-auto" aria-hidden>
          <rect
            x="1.1"
            y="1.1"
            width="19.8"
            height="6.8"
            rx="3.4"
            fill="none"
            stroke="currentColor"
            strokeWidth="1.5"
          />
          <circle cx="4.5" cy="4.5" r="2.1" fill="currentColor" />
          <path
            fill="currentColor"
            fillRule="evenodd"
            d="M4.5 9.7h13a3.4 3.4 0 0 1 0 6.8h-13a3.4 3.4 0 0 1 0-6.8zM17.5 11a2.1 2.1 0 1 0 0 4.2 2.1 2.1 0 1 0 0-4.2z"
          />
        </svg>
        <span className="tabular-nums">{now && menuBarDate(now)}</span>
      </span>
    </div>
  );
}

/**
 * The Mac's dock, macOS 26's glass shelf: Finder (always open), Oriel, Chrome (open) on the iMac, then past the divider
 * the Trash. Open apps have their dot underneath.
 */
function Dock({ size, chrome = false }: { size: string; chrome?: boolean }) {
  const tile = { width: size, height: size };
  const dot = (
    <span className="absolute -bottom-[0.24cqw] left-1/2 size-[0.15cqw] -translate-x-1/2 rounded-full bg-white/90" />
  );
  return (
    <div className="absolute bottom-[0.4cqw] left-1/2 flex -translate-x-1/2 items-center gap-[0.3cqw] rounded-[0.85cqw] bg-white/20 p-[0.32cqw] ring-[length:0.05cqw] ring-white/30 backdrop-blur-md">
      <span className="relative" style={tile}>
        <FinderIcon />
        {dot}
      </span>
      <AppIcon size={size} />
      {chrome && (
        <span
          className="relative flex items-center justify-center rounded-[22.4%] bg-white shadow-sm"
          style={tile}
        >
          <ChromeLogo className="size-[78%]" />
          {dot}
        </span>
      )}
      <span className="mx-[0.05cqw] w-[0.05cqw] self-stretch bg-white/35" />
      <span style={tile}>
        <TrashIcon />
      </span>
    </div>
  );
}

/** Finder's face: the deep blue half, the pale half with the profile between them, two eyes and the smile. */
function FinderIcon() {
  return (
    <svg viewBox="0 0 100 100" className="size-full drop-shadow-sm" aria-hidden>
      <rect width="100" height="100" rx="22.4" fill="#eef5fc" />
      <path
        fill="#1f8ff5"
        d="M0 22.4A22.4 22.4 0 0 1 22.4 0H61c-3 15-5.5 29-6.5 43L45 55h10c-.5 16 .5 30 3 45H22.4A22.4 22.4 0 0 1 0 77.6z"
      />
      <path
        d="M30 27v13M72 27v13M23 68c16 11 38 11 55 0"
        fill="none"
        stroke="#16273b"
        strokeWidth="5"
        strokeLinecap="round"
      />
    </svg>
  );
}

/** The empty Trash: a frosted bin with its rim and ribs. */
function TrashIcon() {
  return (
    <svg viewBox="0 0 100 100" className="size-full drop-shadow-sm" aria-hidden>
      <path
        fill="#e8e9ec"
        fillOpacity="0.85"
        d="M21 22h58l-5.2 70.5a6 6 0 0 1-6 5.5H32.2a6 6 0 0 1-6-5.5z"
      />
      <path
        d="M38 33l1 56M50 33v57M62 33l-1 56"
        stroke="#a3a6ad"
        strokeWidth="2.4"
        strokeLinecap="round"
      />
      <rect x="16" y="14" width="68" height="10" rx="5" fill="#f4f5f7" />
    </svg>
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
function Browser({ credit }: { credit: ReactNode }) {
  return (
    <div className="absolute inset-x-[7%] top-[6%] bottom-[13%] flex flex-col overflow-hidden rounded-[0.9cqw] bg-[#35363a] shadow-2xl ring-1 shadow-black/50 ring-white/15">
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
            className="size-[0.8cqw] rounded-full bg-white p-[0.12cqw]"
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
          <path d="M20.5 11H19V7a2 2 0 0 0-2-2h-4V3.5a2.5 2.5 0 0 0-5 0V5H4a2 2 0 0 0-2 2v3.8h1.5a2.7 2.7 0 0 1 0 5.4H2V20a2 2 0 0 0 2 2h3.8v-1.5a2.7 2.7 0 0 1 5.4 0V22H17a2 2 0 0 0 2-2v-4h1.5a2.5 2.5 0 0 0 0-5z" />
        </svg>
        <span className="size-[1.05cqw] shrink-0 rounded-full bg-gradient-to-br from-sky-300 to-indigo-500" />
        <svg viewBox="0 0 24 24" className={toolIcon}>
          <circle cx="12" cy="5" r="1" />
          <circle cx="12" cy="12" r="1" />
          <circle cx="12" cy="19" r="1" />
        </svg>
      </div>

      <Screen className="flex-1" size={[1400, 760]}>
        <NewTab credit={credit} />
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
 * The new tab's big clock and its greeting, kept to the minute by their own timer. Before each change they check
 * that the page still shows what they last drew: if not, someone set a time by hand (in the browser's tools, for a
 * screenshot), and it stays as they left it.
 */
function ClockAndGreeting() {
  const [now, setNow] = useState<Date | null>(null);
  const box = useRef<HTMLSpanElement>(null);
  /** The text the clock and greeting showed after the last render. */
  const drawn = useRef<string | null>(null);

  useEffect(() => {
    drawn.current = box.current?.textContent ?? null;
  });

  useEffect(() => {
    let timer: number;
    const tick = () => {
      const d = new Date();
      const edited = drawn.current !== null && box.current?.textContent !== drawn.current;
      if (!edited) setNow(d);
      timer = window.setTimeout(tick, 60_000 - (d.getSeconds() * 1000 + d.getMilliseconds()));
    };
    tick();
    return () => window.clearTimeout(timer);
  }, []);

  return (
    <span ref={box} className="flex flex-col items-center">
      <span className="flex h-[4.4cqw] items-center text-[4.4cqw] leading-none font-semibold tracking-tight tabular-nums">
        {now &&
          clockText(now)
            .split(":")
            .map((part, i) => (
              <span key={i} className="flex items-center">
                {/* The colon as two round dots: the font's own are square. */}
                {i > 0 && (
                  <span className="mx-[0.06em] flex h-[0.48em] flex-col justify-between">
                    <span className="size-[0.13em] rounded-full bg-current" />
                    <span className="size-[0.13em] rounded-full bg-current" />
                  </span>
                )}
                {part}
              </span>
            ))}
      </span>
      <span className="mt-[0.35cqw] h-[1cqw] text-[0.9cqw] leading-none font-medium">{now && greetingText(now)}</span>
    </span>
  );
}

/**
 * The extension's page scaled down, the same pieces in the same places as extension/newtab.html without an account:
 * the clock and greeting, the search bar, the photo info button, the photo's credit (in the
 * quote's place, the one spot the MacBook and the iPhone leave in view; the quote for a visitor's own photo) and the
 * shuffle button.
 */
function NewTab({ credit }: { credit: ReactNode }) {
  const soft =
    "grid size-[1.15cqw] place-items-center rounded-full border border-white/20 bg-white/15 backdrop-blur-sm";
  const glyph = `size-[0.6cqw] ${stroke}`;
  return (
    <div className="absolute inset-0 text-white [text-shadow:0_1px_3px_rgb(0_0_0/0.35)]">
      <div className="absolute inset-0 flex flex-col items-center justify-center pb-[1.5cqw]">
        <ClockAndGreeting />
        <span className="mt-[1.1cqw] flex h-[1.45cqw] w-[15cqw] items-center gap-[0.45cqw] rounded-full border border-white/25 bg-white/15 px-[0.7cqw] text-[0.55cqw] text-white/70 shadow-lg backdrop-blur-md [text-shadow:none]">
          <svg viewBox="0 0 24 24" className={`opacity-70 ${glyph}`}>
            <circle cx="11" cy="11" r="7" />
            <path d="m20 20-3.5-3.5" />
          </svg>
          Search the web
        </span>
      </div>

      <div className="absolute inset-x-[0.55cqw] bottom-[0.55cqw] grid grid-cols-[1fr_auto_1fr] items-end">
        <span className={soft}>
          <svg viewBox="0 0 24 24" className={glyph}>
            <circle cx="12" cy="12" r="10" />
            <path d="M12 16v-4M12 8h.01" />
          </svg>
        </span>
        {credit ? (
          <span className="pb-[0.2cqw] text-[0.55cqw] whitespace-nowrap [&_a]:decoration-white/50 [&_a]:underline-offset-[0.2em]">
            {credit}
          </span>
        ) : (
          <span className="pb-[0.2cqw] text-[0.45cqw] italic">
            Small steps, taken daily, move mountains.
          </span>
        )}
        <span className={`justify-self-end ${soft}`}>
          <svg viewBox="0 0 24 24" className={glyph}>
            <path d="M21 12a9 9 0 1 1-3-6.7L21 8" />
            <path d="M21 3v5h-5" />
          </svg>
        </span>
      </div>
    </div>
  );
}
