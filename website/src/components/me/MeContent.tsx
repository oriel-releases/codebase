"use client";

import Image from "next/image";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { GlobeIcon, LinuxIcon, MonitorIcon, PuzzleIcon, WindowsIcon } from "@/components/ui/icons";
import { Logo } from "@/components/ui/Logo";
import { platformNames, shortDate, timeAgo, type SessionRow } from "@/lib/sessions";

export type Wallpaper = {
  imageUrl: string;
  photographer: string;
  photographerUrl: string;
  pageUrl: string;
  description: string;
  source: string;
  updatedAt: Date | null;
};

export type Profile = { name: string; email: string; photo: string; memberSince: Date | null };

/** What the open warning dialog is about: "signout", "others" (end all other sessions) or a session id. */
export type Asking = string | null;

const card = "rounded-[2rem] bg-surface text-left text-ink";
const pill =
  "inline-flex shrink-0 items-center justify-center gap-2 rounded-full px-3.5 py-1.5 text-[13px] font-medium whitespace-nowrap transition-colors focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-accent";
/** The wallpaper card's shape, shared by the real card, its empty state and its skeleton so nothing jumps. */
const wallpaperShape = "aspect-[4/3] sm:aspect-[2/1]";

type ContentProps = {
  profile: Profile;
  wallpaper: Wallpaper | null | undefined;
  sessions: SessionRow[] | null;
  currentId: string | null;
  asking: Asking;
  busy: boolean;
  onAsk: (value: Asking) => void;
  onEnd: (id: string) => void;
  onEndOthers: () => void;
  onSignOut: () => void;
};

/** Everything on the page, from plain data (no Firebase). */
export function MeContent({ profile, wallpaper, sessions, currentId, asking, busy, onAsk, onEnd, onEndOthers, onSignOut }: ContentProps) {
  const others = sessions?.filter((s) => s.id !== currentId) ?? [];
  const initial = (profile.name || profile.email).trim().charAt(0).toUpperCase();
  const target = asking && asking !== "signout" && asking !== "others" ? sessions?.find((s) => s.id === asking) : undefined;
  const warning: Warning | null =
    asking === "signout"
      ? {
          title: "Sign out of this browser?",
          message: "You'll be signed out of the Oriel website here. Your apps and extension stay signed in.",
          confirmLabel: "Sign out",
          onConfirm: onSignOut,
        }
      : asking === "others"
        ? {
            title: "End all other sessions?",
            message: `${others.length} other ${others.length === 1 ? "device" : "devices"} will be signed out of Oriel. This browser stays signed in.`,
            confirmLabel: "End all other sessions",
            onConfirm: onEndOthers,
          }
        : target
          ? {
              title: target.id === currentId ? "Sign out of this browser?" : "End this session?",
              message:
                target.id === currentId
                  ? "You'll be signed out of the Oriel website here."
                  : `${target.deviceName || platformNames[target.platform] || "This device"} will be signed out of Oriel.`,
              confirmLabel: target.id === currentId ? "Sign out" : "End session",
              onConfirm: () => onEnd(target.id),
            }
          : null;
  // Closing clears `asking`, but the dialog still needs its words while it fades out.
  const [viewing, setViewing] = useState<string | null>(null);
  const viewed = viewing ? (sessions?.find((x) => x.id === viewing) ?? null) : null;
  const [lastViewed, setLastViewed] = useState<SessionRow | null>(viewed);
  if (viewed && lastViewed !== viewed) setLastViewed(viewed);
  const [lastWarning, setLastWarning] = useState<Warning | null>(warning);
  if (warning && (lastWarning?.title !== warning.title || lastWarning?.message !== warning.message)) setLastWarning(warning);

  return (
    <div className="flex flex-col gap-5 sm:gap-6">
      {/* Profile */}
      <section data-spotlight aria-label="Profile" className={`${card} overflow-hidden`}>
        {/* Banner: the current wallpaper, blurred into a colour wash (or Oriel's accent while there is none). */}
        <div className="relative h-28 overflow-hidden sm:h-36">
          {wallpaper && (
            <FadeImage src={sizedPhoto(wallpaper.imageUrl, 640, 256)} alt="" className="absolute inset-0 size-full scale-125 object-cover blur-2xl saturate-150" />
          )}
          <div aria-hidden className="absolute inset-0 bg-gradient-to-br from-accent/30 via-accent/10 to-transparent" />
          <div aria-hidden className="absolute inset-x-0 bottom-0 h-1/2 bg-gradient-to-t from-surface to-transparent" />
        </div>

        <div className="relative px-6 pb-6 sm:px-8 sm:pb-8">
          <div className="-mt-12 flex items-end justify-between gap-4 sm:-mt-14">
            <div className="rounded-full bg-surface p-1.5">
              {profile.photo ? (
                // eslint-disable-next-line @next/next/no-img-element
                <img src={profile.photo} alt="" referrerPolicy="no-referrer" className="size-20 rounded-full object-cover sm:size-24" />
              ) : (
                <span className="grid size-20 place-items-center rounded-full bg-ink/10 text-3xl font-semibold sm:size-24 sm:text-4xl">{initial}</span>
              )}
            </div>
            <button
              type="button"
              onClick={() => onAsk("signout")}
              className={`${pill} mb-1 bg-ink/5 px-4 py-2 text-ink hover:bg-ink/10`}
            >
              <svg viewBox="0 0 24 24" width={15} height={15} fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden>
                <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9" />
              </svg>
              Sign out
            </button>
          </div>

          <h1 className="mt-4 truncate text-2xl font-semibold tracking-tight sm:text-3xl">{profile.name || profile.email}</h1>
          {profile.name && <p className="truncate text-sm text-muted sm:text-base">{profile.email}</p>}

          <ul className="mt-5 flex flex-wrap gap-2">
            <Chip icon={<Image src="/google-g-logo.webp" alt="" width={14} height={14} />}>Google account</Chip>
            {profile.memberSince && <Chip>Member since {shortDate(profile.memberSince)}</Chip>}
            {sessions && sessions.length > 0 && (
              <Chip>
                {sessions.length} {sessions.length === 1 ? "device" : "devices"}
              </Chip>
            )}
          </ul>
        </div>
      </section>

      {/* Current wallpaper */}
      <WallpaperCard wallpaper={wallpaper} />

      {/* Sessions */}
      <section data-spotlight aria-labelledby="sessions-title" className={`${card} p-5 sm:p-6`}>
        <div className="flex flex-wrap items-center justify-between gap-3 px-1">
          <h2 id="sessions-title" className="text-lg font-semibold tracking-tight">
            Where you&apos;re signed in
          </h2>
          {others.length > 0 && (
            <button type="button" onClick={() => onAsk("others")} className={`${pill} bg-ink/5 text-ink hover:bg-ink/10`}>
              End all other sessions
            </button>
          )}
        </div>

        {sessions === null ? (
          <SessionSkeleton rows={2} />
        ) : sessions.length === 0 ? (
          <p className="mt-4 px-1 text-sm text-muted">No sessions.</p>
        ) : (
          <ul className="mt-3 space-y-0.5">
            {sessions.map((x) => (
              <SessionButton key={x.id} session={x} isThisDevice={x.id === currentId} onOpen={setViewing} />
            ))}
          </ul>
        )}
      </section>

      {/* Session details */}
      <SessionDialog
        open={Boolean(viewed)}
        session={viewed ?? lastViewed}
        isThisDevice={(viewed ?? lastViewed)?.id === currentId}
        onClose={() => setViewing(null)}
        onEnd={(id) => {
          setViewing(null);
          onAsk(id);
        }}
      />

      {/* Warning */}
      <ConfirmDialog
        open={Boolean(warning)}
        warning={warning ?? lastWarning}
        busy={busy}
        onCancel={() => onAsk(null)}
      />
    </div>
  );
}

/** The current wallpaper as the card itself: the photo fills it, with the details laid over it. */
function WallpaperCard({ wallpaper }: { wallpaper: Wallpaper | null | undefined }) {
  if (wallpaper === undefined) return <WallpaperSkeleton />;

  if (wallpaper === null) {
    return (
      <section aria-label="Current wallpaper" className={`${card} ${wallpaperShape} grid place-items-center border border-dashed border-line bg-transparent p-6 text-center`}>
        <div className="flex flex-col items-center gap-3">
          <span className="grid size-14 place-items-center rounded-full bg-ink/5">
            <MonitorIcon width={24} height={24} />
          </span>
          <h2 className="text-lg font-semibold tracking-tight">No wallpaper yet</h2>
          <p className="max-w-xs text-sm text-pretty text-muted">Change the wallpaper in Oriel and it shows up here.</p>
        </div>
      </section>
    );
  }

  const SourceIcon = wallpaper.source === "linux" ? LinuxIcon : WindowsIcon;
  const glass = "rounded-full bg-black/35 px-3.5 py-1.5 text-[13px] font-medium text-white backdrop-blur-md ring-1 ring-white/15";
  return (
    <section aria-label="Current wallpaper" className={`group relative isolate overflow-hidden rounded-[2rem] bg-surface text-white ${wallpaperShape}`}>
      <FadeImage
        src={sizedPhoto(wallpaper.imageUrl)}
        alt={wallpaper.description || "Your current wallpaper"}
        className="absolute inset-0 -z-10 size-full object-cover transition-[opacity,transform] duration-700 ease-out group-hover:scale-[1.03]"
      />
      <div aria-hidden className="absolute inset-0 -z-10 bg-gradient-to-t from-black/75 via-black/10 to-black/20" />

      <div className="flex h-full flex-col justify-between p-5 sm:p-7">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <h2 className={glass}>Current wallpaper</h2>
          <span className={`${glass} inline-flex items-center gap-2`}>
            <SourceIcon width={14} height={14} />
            {wallpaper.source === "linux" ? "Linux app" : "Windows app"}
            {wallpaper.updatedAt ? ` · ${timeAgo(wallpaper.updatedAt)}` : ""}
          </span>
        </div>

        <figcaption className="max-w-xl">
          {wallpaper.description && <p className="text-xl font-semibold tracking-tight text-balance sm:text-2xl">{capitalize(wallpaper.description)}</p>}
          {wallpaper.photographer && (
            <p className="mt-1.5 text-sm text-white/80">
              Photo by{" "}
              <a href={wallpaper.photographerUrl} target="_blank" rel="noopener noreferrer" className="font-medium text-white underline underline-offset-4">
                {wallpaper.photographer}
              </a>{" "}
              on{" "}
              <a href={wallpaper.pageUrl} target="_blank" rel="noopener noreferrer" className="font-medium text-white underline underline-offset-4">
                Unsplash
              </a>
            </p>
          )}
        </figcaption>
      </div>
    </section>
  );
}

/** An image that fades in once it has loaded, so it never pops onto the card. */
function FadeImage({ src, alt, className }: { src: string; alt: string; className: string }) {
  const [loaded, setLoaded] = useState(false);
  return (
    // eslint-disable-next-line @next/next/no-img-element
    <img
      src={src}
      alt={alt}
      onLoad={() => setLoaded(true)}
      className={`${className} ${loaded ? "opacity-100" : "opacity-0"}`}
    />
  );
}

function Chip({ icon, children }: { icon?: ReactNode; children: ReactNode }) {
  return (
    <li className="inline-flex items-center gap-2 rounded-full bg-ink/5 px-3.5 py-1.5 text-xs font-medium text-ink/80">
      {icon}
      {children}
    </li>
  );
}

function PlatformIcon({ platform, size }: { platform: string; size: number }) {
  const props = { width: size, height: size };
  if (platform === "windows") return <WindowsIcon {...props} />;
  if (platform === "linux") return <LinuxIcon {...props} />;
  if (platform === "extension") return <PuzzleIcon {...props} />;
  if (platform === "web") return <GlobeIcon {...props} />;
  return <MonitorIcon {...props} />;
}

/** Active in the last ten minutes. */
const isActive = (date: Date | null) => Boolean(date && Date.now() - date.getTime() < 10 * 60_000);

/** One compact row; the whole row opens the session's details. */
function SessionButton({ session: x, isThisDevice, onOpen }: { session: SessionRow; isThisDevice: boolean; onOpen: (id: string) => void }) {
  const kind = platformNames[x.platform] ?? "Device";
  return (
    <li>
      <button
        type="button"
        onClick={() => onOpen(x.id)}
        aria-label={`${x.deviceName || kind}: details`}
        className="group flex w-full items-center gap-3 rounded-2xl px-3 py-2.5 text-left transition-colors hover:bg-ink/5 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
      >
        <span className="relative grid size-9 shrink-0 place-items-center rounded-full bg-ink/5">
          <PlatformIcon platform={x.platform} size={17} />
          {isActive(x.lastSeenAt) && <span aria-hidden className="absolute -right-0.5 -bottom-0.5 size-2.5 rounded-full bg-emerald-500 ring-2 ring-surface" />}
        </span>
        <span className="min-w-0 flex-1">
          <span className="flex items-center gap-2">
            <span className="truncate text-sm font-medium">{x.deviceName || kind}</span>
            {isThisDevice && <span className="shrink-0 rounded-full bg-accent/15 px-2 py-0.5 text-[11px] font-medium text-accent">This device</span>}
          </span>
          <span className="block truncate text-xs text-muted">
            {kind}
            {x.appVersion ? ` ${x.appVersion}` : ""}
            {x.lastSeenAt ? ` · ${isActive(x.lastSeenAt) ? "Active now" : `Active ${timeAgo(x.lastSeenAt)}`}` : ""}
          </span>
        </span>
        <svg viewBox="0 0 24 24" width={16} height={16} fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden className="shrink-0 text-muted transition-transform group-hover:translate-x-0.5">
          <path d="m9 18 6-6-6-6" />
        </svg>
      </button>
    </li>
  );
}

/** The session's details in a window that animates like the warnings; ending it goes through the warning next. */
function SessionDialog({
  open,
  session: x,
  isThisDevice,
  onClose,
  onEnd,
}: {
  open: boolean;
  session: SessionRow | null;
  isThisDevice: boolean;
  onClose: () => void;
  onEnd: (id: string) => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    if (open && !dialog.open) dialog.showModal();
    if (!open && dialog.open) dialog.close();
  }, [open]);

  const kind = x ? (platformNames[x.platform] ?? "Device") : "";
  const when = (d: Date | null) => (d ? d.toLocaleString("en", { dateStyle: "medium", timeStyle: "short" }) : "Unknown");
  return (
    <dialog
      ref={ref}
      aria-labelledby="session-title"
      onCancel={(e) => {
        e.preventDefault();
        onClose();
      }}
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
      className="confirm m-auto w-[min(26rem,calc(100vw-2rem))] rounded-[2rem] bg-paper p-0 text-ink shadow-2xl ring-1 ring-line"
    >
      {x && (
        <div className="p-6 sm:p-7">
          <div className="flex items-center gap-4">
            <span className="grid size-14 shrink-0 place-items-center rounded-full bg-ink/5">
              <PlatformIcon platform={x.platform} size={24} />
            </span>
            <div className="min-w-0">
              <h2 id="session-title" className="truncate text-xl font-semibold tracking-tight">
                {x.deviceName || kind}
              </h2>
              <div className="mt-1 flex flex-wrap items-center gap-2 text-sm text-muted">
                <span>
                  {kind}
                  {x.appVersion ? ` ${x.appVersion}` : ""}
                </span>
                {isThisDevice && <span className="rounded-full bg-accent/15 px-2 py-0.5 text-[11px] font-medium text-accent">This device</span>}
              </div>
            </div>
          </div>

          <dl className="mt-6 grid grid-cols-[auto_1fr] gap-x-6 gap-y-3 rounded-2xl bg-surface p-4 text-sm">
            <dt className="text-muted">Last active</dt>
            <dd className="text-right font-medium">{isActive(x.lastSeenAt) ? "Now" : when(x.lastSeenAt)}</dd>
            <dt className="text-muted">Signed in</dt>
            <dd className="text-right font-medium">{when(x.createdAt)}</dd>
          </dl>

          <div className="mt-6 grid grid-cols-2 gap-3">
            <button type="button" onClick={onClose} className={`${pill} bg-surface px-5 py-3 text-sm text-ink hover:bg-line`}>
              Close
            </button>
            <button type="button" onClick={() => onEnd(x.id)} className={`${pill} bg-red-600/10 px-5 py-3 text-sm text-red-600 hover:bg-red-600/20 dark:bg-red-400/10 dark:text-red-400 dark:hover:bg-red-400/20`}>
              {isThisDevice ? "Sign out" : "End session"}
            </button>
          </div>
        </div>
      )}
    </dialog>
  );
}

type Warning = { title: string; message: ReactNode; confirmLabel: string; onConfirm: () => void };

/** A warning in its own window: it fades and rises in, and out again; the page behind dims. Esc or a click outside cancels. */
function ConfirmDialog({ open, warning, busy, onCancel }: { open: boolean; warning: Warning | null; busy: boolean; onCancel: () => void }) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    if (open && !dialog.open) dialog.showModal();
    // close() runs the exit transition: the dialog stays visible until it has finished (display is transitioned too).
    if (!open && dialog.open) dialog.close();
  }, [open]);

  return (
    <dialog
      ref={ref}
      aria-labelledby="confirm-title"
      onCancel={(e) => {
        e.preventDefault();
        if (!busy) onCancel();
      }}
      onClick={(e) => {
        if (e.target === e.currentTarget && !busy) onCancel();
      }}
      className="confirm m-auto w-[min(26rem,calc(100vw-2rem))] rounded-[2rem] bg-paper p-0 text-ink shadow-2xl ring-1 ring-line"
    >
      {warning && (
        <div className="p-7 text-center sm:p-8">
          <span className="mx-auto grid size-14 place-items-center rounded-full bg-red-600/10 text-red-600 dark:bg-red-400/10 dark:text-red-400">
            <svg viewBox="0 0 24 24" width={26} height={26} fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden>
              <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3" />
              <path d="M12 9v4M12 17h.01" />
            </svg>
          </span>
          <h2 id="confirm-title" className="mt-5 text-xl font-semibold tracking-tight text-balance">
            {warning.title}
          </h2>
          <p className="mt-2 text-sm text-pretty text-muted">{warning.message}</p>
          <div className="mt-7 grid grid-cols-2 gap-3">
            <button type="button" onClick={onCancel} disabled={busy} className={`${pill} bg-surface px-5 py-3 text-sm text-ink hover:bg-line disabled:opacity-60`}>
              Cancel
            </button>
            <button type="button" onClick={warning.onConfirm} disabled={busy} className={`${pill} bg-red-600 px-5 py-3 text-sm text-white hover:bg-red-700 disabled:opacity-60`}>
              {busy ? "Working…" : warning.confirmLabel}
            </button>
          </div>
        </div>
      )}
    </dialog>
  );
}

// ---------------------------------------------------------------- loading

const ghost = "animate-pulse rounded bg-ink/10";

function WallpaperSkeleton() {
  return <div aria-hidden className={`${wallpaperShape} animate-pulse rounded-[2rem] bg-ink/10`} />;
}

function SessionSkeleton({ rows }: { rows: number }) {
  return (
    <div aria-hidden className="mt-3 space-y-0.5">
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className="flex items-center gap-3 px-3 py-2.5">
          <span className="size-9 shrink-0 animate-pulse rounded-full bg-ink/10" />
          <div className="flex-1 space-y-2">
            <div className={`${ghost} h-3.5 w-36 max-w-full`} />
            <div className={`${ghost} h-3 w-56 max-w-full`} />
          </div>
        </div>
      ))}
    </div>
  );
}

/**
 * The loading mark: the logo in a soft disc with a comet of light circling it. Wordless on purpose (the screen reader
 * still gets a label). It is the only thing on the page while loading; it sits over the page's area (absolutely
 * positioned, so it takes no space) and fades out in place when `leaving`.
 */
export function Spinner({ leaving = false }: { leaving?: boolean }) {
  return (
    <div aria-hidden={leaving} className="pointer-events-none absolute inset-0 z-10">
      <div
        role="status"
        aria-live="polite"
        className={`sticky top-[38svh] flex justify-center transition-[opacity,scale] duration-300 ease-out motion-reduce:transition-none ${
          leaving ? "scale-90 opacity-0" : "scale-100 opacity-100"
        }`}
      >
        <span className="sr-only">Loading your account</span>
        <div aria-hidden className="relative grid size-[4.5rem] place-items-center">
          {/* A slow glow behind, so the mark feels alive rather than busy. */}
          <span className="absolute inset-1 animate-pulse rounded-full bg-accent/25 blur-xl motion-reduce:animate-none" />
          {/* The disc. */}
          <span className="absolute inset-0 rounded-full bg-paper shadow-md ring-1 ring-line" />
          {/* The comet: a conic gradient shaped into a ring, turning. */}
          <span
            className="absolute inset-0 animate-spin rounded-full motion-reduce:animate-none [-webkit-mask:radial-gradient(farthest-side,transparent_calc(100%-3px),#000_calc(100%-3px))] [background:conic-gradient(from_0deg,transparent_0deg,transparent_120deg,var(--color-accent)_340deg,transparent_360deg)] [mask:radial-gradient(farthest-side,transparent_calc(100%-3px),#000_calc(100%-3px))]"
            style={{ animationDuration: "1.1s" }}
          />
          <Logo size={28} className="relative" />
        </div>
      </div>
    </div>
  );
}

/** The wallpaper's Unsplash link sized for the card (Unsplash crops on its side), keeping the ixid Unsplash counts views with. */
function sizedPhoto(imageUrl: string, width = 1600, height = 800): string {
  try {
    const url = new URL(imageUrl);
    url.search = new URLSearchParams({
      ixid: url.searchParams.get("ixid") ?? "",
      w: String(width),
      h: String(height),
      fit: "crop",
      crop: "entropy",
      q: "80",
      fm: "jpg",
      auto: "format",
    }).toString();
    return url.toString();
  } catch {
    return imageUrl;
  }
}

const capitalize = (s: string) => s.charAt(0).toUpperCase() + s.slice(1) + (/[.!?]$/.test(s) ? "" : ".");
