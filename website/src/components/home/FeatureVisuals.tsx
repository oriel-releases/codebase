import type { ReactNode } from "react";
import { Check, Heart, Moon, Plus, Sun } from "lucide-react";
import photos from "@/data/today.json";
import { showcase, type FeatureVisual } from "@/content/home";
import { AndroidIcon, AppleIcon, LinuxIcon, WindowsIcon } from "@/components/ui/icons";

/**
 * The small scenes above the home page's feature cards and getting-started steps: what each one means, drawn with the
 * site's own surfaces and a few of the Wallpaper of the day photos (hotlinked from Unsplash, credited site-wide in the
 * footer). Decorative: the card's words say it all, so the scenes are hidden from screen readers.
 */

const pool = showcase.photoIds.map((id) => photos.find((p) => p.id === id)).filter((p) => p !== undefined);

/** A pool photo, cropped to `w`×`h` (twice the size it shows at most). */
function Photo({ i, w, h, className = "" }: { i: number; w: number; h: number; className?: string }) {
  const p = pool[i % pool.length]!;
  return (
    // eslint-disable-next-line @next/next/no-img-element -- hotlinked Unsplash photos: there is no image server on static hosting
    <img
      src={`https://images.unsplash.com/${p.path}?w=${w}&h=${h}&fit=crop&crop=entropy&q=70&fm=jpg&auto=format`}
      alt=""
      loading="lazy"
      decoding="async"
      draggable={false}
      className={`object-cover ${className}`}
    />
  );
}

/** A small rounded label, on the page's surface or over a photo. */
function Pill({ children, className = "" }: { children: ReactNode; className?: string }) {
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full bg-paper/85 px-2.5 py-1 text-[11px] leading-none font-medium text-ink shadow-sm ring-1 ring-line backdrop-blur-md ${className}`}
    >
      {children}
    </span>
  );
}

const fill = "absolute inset-0 size-full";

/** The scene for one feature card. */
export function FeatureScene({ visual }: { visual: FeatureVisual }) {
  switch (visual) {
    case "devices":
      // A laptop and a phone with the same photo, and the word that they match.
      return (
        <div className="flex size-full items-center justify-center">
          <div className="relative w-[min(60%,19rem)]">
            <div className="rounded-t-[0.6rem] bg-black p-[3%] pb-[4%] shadow-xl shadow-ink/20">
              <Photo i={0} w={640} h={400} className="aspect-[16/10] w-full rounded-[0.3rem]" />
            </div>
            <div className="mx-[-5%] h-2 rounded-b-lg bg-gradient-to-b from-neutral-500 to-neutral-700" />
            <div className="absolute -right-[16%] -bottom-[8%] w-[27%] rounded-[0.9rem] bg-black p-[3px] shadow-xl shadow-ink/30">
              <Photo i={0} w={200} h={420} className="aspect-[9/19] w-full rounded-[0.75rem]" />
            </div>
          </div>
          <Pill className="absolute top-4 left-4">
            <span className="size-1.5 rounded-full bg-emerald-500" />
            Synced just now
          </Pill>
        </div>
      );

    case "schedule":
      // The daily time, then every interval after it.
      return (
        <div className="flex size-full flex-col items-center justify-center">
          <span className="text-[11px] font-medium tracking-wide text-muted uppercase">Every day at</span>
          <span className="mt-1 text-5xl font-semibold tracking-tight tabular-nums">08:00</span>
          <div className="mt-4 flex items-center gap-1.5">
            {["11:00", "14:00", "17:00", "20:00"].map((t, i) => (
              <span key={t} className="flex items-center gap-1.5">
                {i > 0 && <span className="h-px w-2 bg-line" />}
                <span className="rounded-full bg-surface px-2 py-1 text-[11px] text-muted tabular-nums">{t}</span>
              </span>
            ))}
          </div>
        </div>
      );

    case "morning":
      return (
        <>
          <Photo i={3} w={720} h={400} className={fill} />
          <div className="absolute inset-0 bg-gradient-to-t from-black/50 to-black/0" />
          <Pill className="absolute top-4 left-4">
            <Sun size={12} aria-hidden />
            7:00 AM
          </Pill>
          <span className="absolute bottom-4 left-4 text-sm font-medium text-white">Today&apos;s photo</span>
        </>
      );

    case "topics": {
      // Interests: the ones on, the ones off, and a search of your own.
      const topics: [string, boolean, number?][] = [
        ["Nature", true, 1],
        ["Space", true, 4],
        ["Cities", false],
        ["Ocean", true, 5],
        ["Mountains", false],
        ["Minimal", true, 6],
        ["Architecture", false],
        ["Night sky", false],
      ];
      return (
        <div className="flex size-full items-center justify-center px-6">
          <div className="flex max-w-md flex-wrap justify-center gap-2">
            {topics.map(([name, on, photo]) => (
              <span
                key={name}
                className={`inline-flex items-center gap-1.5 rounded-full py-1 pr-3 text-[13px] font-medium ${
                  on ? "bg-ink pl-1 text-paper" : "bg-surface pl-3 text-muted"
                }`}
              >
                {on && photo !== undefined && <Photo i={photo} w={48} h={48} className="size-6 rounded-full" />}
                {name}
                {on && <Check size={13} aria-hidden />}
              </span>
            ))}
            <span className="inline-flex items-center gap-1 rounded-full border border-dashed border-muted/50 px-3 py-1 text-[13px] text-muted">
              <Plus size={13} aria-hidden />
              Your own search
            </span>
          </div>
        </div>
      );
    }

    case "history":
      // The photos already shown, fanned out: each one remembered.
      return (
        <div className="relative size-full">
          {[
            { i: 7, turn: "-rotate-[10deg] -translate-x-[62%]" },
            { i: 8, turn: "rotate-[10deg] -translate-x-[38%]" },
            { i: 2, turn: "-translate-x-1/2 -translate-y-[4%]" },
          ].map(({ i, turn }) => (
            <div
              key={i}
              className={`absolute top-1/2 left-1/2 w-[46%] max-w-[13rem] -translate-y-1/2 overflow-hidden rounded-xl shadow-lg ring-4 shadow-ink/20 ring-paper ${turn}`}
            >
              <Photo i={i} w={420} h={280} className="aspect-[3/2] w-full" />
            </div>
          ))}
        </div>
      );

    case "screens":
      // Two monitors and a phone's lock screen, all one photo.
      return (
        <div className="flex size-full items-end justify-center gap-3 px-6 pb-6">
          {[0, 1].map((m) => (
            <div key={m} className="flex w-[34%] flex-col items-center">
              <div className="w-full rounded-md bg-black p-[3%] shadow-lg shadow-ink/20">
                <Photo i={9} w={360} h={200} className="aspect-[16/9] w-full rounded-[0.2rem]" />
              </div>
              <div className="h-3 w-[14%] bg-neutral-400 dark:bg-neutral-600" />
              <div className="h-1 w-[34%] rounded-full bg-neutral-400 dark:bg-neutral-600" />
            </div>
          ))}
          <div className="relative mb-1 w-[14%] rounded-[0.7rem] bg-black p-[2px] shadow-lg shadow-ink/25">
            <Photo i={9} w={120} h={250} className="aspect-[9/19] w-full rounded-[0.6rem]" />
            <span className="absolute inset-x-0 top-[18%] text-center text-[11px] font-semibold text-white">9:41</span>
          </div>
        </div>
      );

    case "quiet":
      // Asleep in the menu bar until it's time.
      return (
        <>
          <Photo i={10} w={720} h={400} className={`${fill} opacity-80 blur-[2px]`} />
          <div className="absolute inset-x-0 top-0 flex h-7 items-center justify-end gap-3 bg-black/25 px-4 text-white backdrop-blur-md">
            {/* eslint-disable-next-line @next/next/no-img-element -- the small logo, decorative */}
            <img src="/logo.png" alt="" className="size-3.5 invert" />
            <span className="text-[11px] font-medium tabular-nums">9:41</span>
          </div>
          <div className="absolute top-9 right-3 w-48 rounded-xl bg-paper/90 p-1.5 text-[12px] shadow-xl ring-1 shadow-ink/20 ring-line backdrop-blur-xl">
            <div className="flex items-center gap-2 px-2 py-1.5 text-muted">
              <Moon size={12} aria-hidden />
              Sleeping until 08:00
            </div>
            <div className="mx-2 h-px bg-line" />
            <div className="rounded-md px-2 py-1.5">Change now</div>
            <div className="rounded-md px-2 py-1.5">Pause</div>
          </div>
        </>
      );

    case "browser":
      // The new tab extension: the same photo, a clock and a greeting.
      return (
        <div className="flex size-full items-end justify-center px-6 pt-6">
          <div className="w-full max-w-lg overflow-hidden rounded-t-xl bg-[#35363a] shadow-2xl ring-1 shadow-ink/25 ring-black/20">
            <div className="flex h-7 items-center gap-1.5 bg-[#202124] px-3">
              {["bg-[#ff5f57]", "bg-[#febc2e]", "bg-[#28c840]"].map((c) => (
                <span key={c} className={`size-2 rounded-full ${c}`} />
              ))}
              <span className="ml-3 rounded-t-md bg-[#35363a] px-3 py-1 text-[10px] text-[#e8eaed]">New Tab</span>
            </div>
            <div className="relative h-36 sm:h-40">
              <Photo i={11} w={960} h={320} className={fill} />
              <div className="absolute inset-0 flex flex-col items-center justify-center text-white [text-shadow:0_1px_3px_rgb(0_0_0/0.35)]">
                <span className="text-4xl font-semibold tracking-tight tabular-nums">09:41</span>
                <span className="mt-1 text-xs font-medium">Good morning.</span>
              </div>
            </div>
          </div>
        </div>
      );

    case "favorites":
      // A star tap: the heart fills, and every device has it.
      return (
        <>
          <Photo i={12} w={720} h={400} className={fill} />
          <div className="absolute inset-0 bg-gradient-to-t from-black/40 to-black/0" />
          <div className="absolute right-5 bottom-5">
            {[
              "-top-3 left-1/2",
              "top-1/2 -left-3",
              "-right-3 top-1/2",
              "-top-1 -left-1",
              "-top-1 -right-1",
            ].map((spot) => (
              <span key={spot} className={`absolute size-1.5 rounded-full bg-white/90 ${spot}`} />
            ))}
            <span className="grid size-11 place-items-center rounded-full bg-white shadow-lg">
              <Heart size={20} aria-hidden className="fill-[#ff3b5c] text-[#ff3b5c]" />
            </span>
          </div>
          <Pill className="absolute top-4 left-4">On all your devices</Pill>
        </>
      );
  }
}

/** The scene for one getting-started step: download, sign in, and the first wallpaper arriving. */
export function StepScene({ step }: { step: number }) {
  if (step === 0)
    return (
      <div className="flex size-full flex-col items-center justify-center gap-4">
        <div className="flex gap-2.5">
          {[WindowsIcon, AppleIcon, LinuxIcon, AndroidIcon].map((Icon, i) => (
            <span key={i} className="grid size-12 place-items-center rounded-2xl bg-surface text-ink">
              <Icon width={20} height={20} />
            </span>
          ))}
        </div>
        <span className="rounded-full bg-ink px-4 py-2 text-xs font-medium text-paper">Download Oriel</span>
      </div>
    );
  if (step === 1)
    return (
      <div className="flex size-full items-center justify-center">
        <div className="w-56 rounded-2xl bg-surface p-4 text-left">
          <div className="flex items-center gap-2">
            {/* eslint-disable-next-line @next/next/no-img-element -- the small logo, decorative */}
            <img src="/logo.png" alt="" className="size-5 dark:invert" />
            <span className="text-sm font-semibold">Sign in</span>
          </div>
          <div className="mt-3 flex items-center justify-center gap-2 rounded-full bg-paper py-2 text-[11px] font-medium ring-1 ring-line">
            {/* eslint-disable-next-line @next/next/no-img-element -- Google's mark, decorative */}
            <img src="/google-g-logo.webp" alt="" className="size-3.5" />
            Continue with Google
          </div>
          <div className="my-2 text-center text-[10px] text-muted">or</div>
          <div className="rounded-full bg-paper px-3 py-2 text-[11px] text-muted ring-1 ring-line">you@email.com</div>
        </div>
      </div>
    );
  // The apps' own words while a photo arrives, all done, over the photo they brought.
  return (
    <>
      <Photo i={13} w={720} h={400} className={fill} />
      <div className="absolute inset-0 bg-gradient-to-t from-black/45 to-black/0" />
      <div className="absolute bottom-4 left-4 flex flex-col gap-1.5">
        {["Finding a photo", "Downloading", "Changing"].map((word) => (
          <Pill key={word} className="self-start">
            <Check size={11} aria-hidden className="text-emerald-600" />
            {word}
          </Pill>
        ))}
      </div>
    </>
  );
}
