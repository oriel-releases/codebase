"use client";

import { useSyncExternalStore } from "react";
import type { Release, Releases } from "@/config/release";
import { downloadPage } from "@/content/download";
import { DownloadIcon, LinuxIcon, WindowsIcon } from "@/components/ui/icons";
import { LatestVersion } from "@/components/download/LatestVersion";
import { PlatformNotice } from "@/components/download/PlatformNotice";
import { SharedElement } from "@/components/motion/SharedElement";
import type { AppPlatform } from "@/lib/latest-version";
import { detectPlatform, type Platform } from "@/lib/platform";
import { sharedElement } from "@/lib/transitions";

const subscribe = () => () => {};
const getPlatform = (): Platform => detectPlatform(navigator.userAgent);
const getServerPlatform = (): Platform => "unknown";

/**
 * Two identical cards in a fixed order (Windows, Linux), so nothing ever moves. The page is built before it knows
 * the visitor's system; once it does, the matching card eases into its highlight (ring, colors, tag) instead of
 * being swapped in. When the system isn't known (Mac, phone), neither is highlighted.
 */
export function DownloadCard({ releases }: { releases: Releases }) {
  const platform = useSyncExternalStore(subscribe, getPlatform, getServerPlatform);
  const known = platform === "windows" || platform === "linux";
  const order: AppPlatform[] = ["windows", "linux"];

  return (
    <div className="flex w-full flex-col items-center gap-5">
      <div className="grid w-full gap-3 sm:grid-cols-2 sm:gap-4">
        {order.map((key) => (
          <PlatformCard
            key={key}
            release={releases[key]}
            // The shared button morph needs exactly one button: the visitor's, or Windows' when unknown.
            primary={key === (platform === "linux" ? "linux" : "windows")}
            yours={known && key === platform}
            quiet={known && key !== platform}
          />
        ))}
      </div>
      <PlatformNotice message={downloadPage.unsupportedNotice} />
    </div>
  );
}

/** `yours`: the visitor's system, highlighted. `quiet`: the other one once the system is known, with an outlined button. */
function PlatformCard({
  release,
  primary,
  yours,
  quiet,
}: {
  release: Release;
  primary: boolean;
  yours: boolean;
  quiet: boolean;
}) {
  const Icon = release.platform === "linux" ? LinuxIcon : WindowsIcon;
  // Everything that changes between states animates, so finding the system reads as a calm highlight, not a jump.
  const ease = "transition-[color,background-color,border-color,box-shadow,opacity,translate] duration-500 ease-out";

  const button = (
    <a
      href={release.url}
      download={release.fileName}
      className={`inline-flex w-full items-center justify-center gap-2 rounded-full border px-5 py-3 text-sm font-medium whitespace-nowrap hover:opacity-85 focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-accent ${ease} ${
        yours
          ? "border-accent bg-accent text-paper"
          : quiet
            ? "border-ink/30 bg-transparent text-ink"
            : "border-ink bg-ink text-paper"
      }`}
    >
      <DownloadIcon width={16} height={16} />
      Download for {release.name}
    </a>
  );

  return (
    <section
      data-spotlight
      data-tilt
      aria-label={release.name}
      className={`flex flex-col rounded-[2rem] bg-surface p-6 text-left text-ink ring-2 hover:-translate-y-1 hover:shadow-xl hover:shadow-ink/10 motion-reduce:hover:translate-y-0 sm:p-8 ${ease} ${
        yours ? "ring-accent" : "ring-transparent"
      }`}
    >
      <div className="flex items-center justify-between">
        <span className={`flex size-12 items-center justify-center rounded-full ${ease} ${yours ? "bg-accent/15 text-accent" : "bg-ink/5"}`}>
          <Icon width={22} height={22} />
        </span>
        {/* Always in the layout, so it can fade in without moving anything. */}
        <span
          aria-hidden={!yours}
          className={`rounded-full bg-accent/15 px-3 py-1 text-xs font-medium text-accent transition-[opacity,transform] duration-500 ease-out ${
            yours ? "translate-y-0 opacity-100" : "translate-y-1 opacity-0"
          }`}
        >
          Your system
        </span>
      </div>

      <h2 className="mt-6 text-2xl font-semibold tracking-tight sm:mt-8">{release.name}</h2>
      <p className="mt-1 text-sm text-pretty text-muted sm:min-h-10">{release.requirements}</p>

      <p className="mt-5 text-sm text-muted sm:mt-6">
        <span className="text-ink">
          Version <LatestVersion platform={release.platform} initial={release.version} />
        </span>{" "}
        · {release.fileName}
      </p>

      <div className="mt-auto pt-5 sm:pt-6">{primary ? <SharedElement name={sharedElement.downloadButton}>{button}</SharedElement> : button}</div>
    </section>
  );
}
