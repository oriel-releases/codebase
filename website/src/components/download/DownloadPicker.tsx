"use client";

import { useState, useSyncExternalStore } from "react";
import type { Releases } from "@/config/release";
import { downloadPage } from "@/content/download";
import { AndroidIcon, AppleIcon, DownloadIcon, LinuxIcon, WindowsIcon } from "@/components/ui/icons";
import { LatestVersion } from "@/components/download/LatestVersion";
import { PlatformNotice } from "@/components/download/PlatformNotice";
import { SegmentedSwitch } from "@/components/download/SegmentedSwitch";
import { SmoothWidth, useSwitchMotion } from "@/components/download/SwitchMotion";
import { SharedElement } from "@/components/motion/SharedElement";
import type { AppPlatform } from "@/lib/latest-version";
import { detectPlatform, type Platform } from "@/lib/platform";
import { sharedElement } from "@/lib/transitions";

const subscribe = () => () => {};
const getPlatform = (): Platform => detectPlatform(navigator.userAgent);
const getServerPlatform = (): Platform => "unknown";
const order: readonly AppPlatform[] = ["windows", "macos", "linux", "android"];

/**
 * One decision instead of four cards: a Windows / Mac / Linux / Android switch that starts on the visitor's own system and
 * one big download button for it. Switching grows or shrinks the button to its new label and slides the new words in
 * from the side that was picked (the download icon stays still); the install
 * steps appear once the download has started, when they are what you need next.
 */
export function DownloadPicker({ releases }: { releases: Releases }) {
  const detected = useSyncExternalStore(subscribe, getPlatform, getServerPlatform);
  const [picked, setPicked] = useState<AppPlatform | null>(null);
  const [downloaded, setDownloaded] = useState(false);
  const chosen: AppPlatform = picked ?? (detected === "mac" ? "macos" : detected === "linux" || detected === "android" ? detected : "windows");
  const release = releases[chosen];
  const motion = useSwitchMotion(order);

  return (
    <div className="flex w-full flex-col items-center">
      <SegmentedSwitch
        label="Your system"
        value={chosen}
        onChange={(next) => motion.change(chosen, next, setPicked)}
        options={[
          { key: "windows", label: releases.windows.name, Icon: WindowsIcon },
          { key: "macos", label: releases.macos.name, Icon: AppleIcon },
          { key: "linux", label: releases.linux.name, Icon: LinuxIcon },
          { key: "android", label: releases.android.name, Icon: AndroidIcon },
        ]}
      />

      <SharedElement name={sharedElement.downloadButton}>
        <a
          href={release.url}
          download={release.fileName}
          onClick={() => setDownloaded(true)}
          className="mt-7 inline-flex items-center justify-center rounded-full bg-ink px-8 py-4 text-base font-medium whitespace-nowrap text-paper active:scale-[0.98] focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-accent"
        >
          <SmoothWidth>
            <DownloadIcon width={18} height={18} className="shrink-0" />
            <span key={chosen} className={`inline-block ${motion.textIn}`}>
              Download for {release.name}
            </span>
          </SmoothWidth>
        </a>
      </SharedElement>

      <div key={chosen} className={`flex flex-col items-center ${motion.textIn}`}>
        <p className="mt-4 text-sm text-muted">
          Version <LatestVersion platform={chosen} initial={release.version} /> · {release.fileName}
        </p>
        <p className="mt-1 text-xs text-pretty text-muted">{release.requirements}</p>
      </div>

      <ul className="mt-6 flex flex-wrap justify-center gap-x-5 gap-y-2 text-sm text-muted">
        {downloadPage.promises.map((p) => (
          <li key={p} className="inline-flex items-center gap-1.5">
            <svg viewBox="0 0 24 24" width={14} height={14} fill="none" stroke="currentColor" strokeWidth={2.5} strokeLinecap="round" strokeLinejoin="round" aria-hidden className="text-accent">
              <path d="M20 6 9 17l-5-5" />
            </svg>
            {p}
          </li>
        ))}
      </ul>

      <div className="mt-6 empty:hidden">
        <PlatformNotice message={downloadPage.unsupportedNotice} />
      </div>

      {downloaded && <InstallSteps key={`steps-${chosen}`} steps={downloadPage.installSteps[chosen]} name={release.name} />}
    </div>
  );
}

/** The three steps for the chosen system, rising in once the download started (and again when switching system). */
function InstallSteps({ steps, name }: { steps: readonly string[]; name: string }) {
  return (
    <section aria-label={`Installing on ${name}`} aria-live="polite" className="mt-12 w-full animate-[fade-up_0.4s_ease-out] motion-reduce:animate-none sm:mt-14">
      <h2 className="text-center text-sm font-medium tracking-wide text-muted uppercase">Install in three steps</h2>
      <ol className="mt-6 grid gap-3 text-left sm:grid-cols-3 sm:gap-4">
        {steps.map((step, i) => (
          <li key={step} className="rounded-[1.5rem] bg-surface p-5">
            <span className="flex size-7 items-center justify-center rounded-full bg-ink text-xs font-semibold text-paper">{i + 1}</span>
            <p className="mt-4 text-sm text-pretty text-muted">{step}</p>
          </li>
        ))}
      </ol>
    </section>
  );
}
