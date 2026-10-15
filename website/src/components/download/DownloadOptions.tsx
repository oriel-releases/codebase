"use client";

import { useState, useSyncExternalStore } from "react";
import type { Releases } from "@/config/release";
import { extension } from "@/config/extension";
import { downloadPage } from "@/content/download";
import {
  AndroidIcon,
  AppleIcon,
  ChromeIcon,
  DownloadIcon,
  EdgeIcon,
  FirefoxIcon,
  GlobeIcon,
  LinuxIcon,
  MonitorIcon,
  WindowsIcon,
} from "@/components/ui/icons";
import { LatestVersion } from "@/components/download/LatestVersion";
import { PlatformNotice } from "@/components/download/PlatformNotice";
import { SegmentedSwitch } from "@/components/download/SegmentedSwitch";
import {
  SmoothWidth,
  GlidingBox,
  useSwitchMotion,
} from "@/components/download/SwitchMotion";
import { SharedElement } from "@/components/motion/SharedElement";
import type { AppPlatform } from "@/lib/latest-version";
import {
  detectBrowser,
  detectPlatform,
  type Browser,
  type Platform,
} from "@/lib/platform";
import { sharedElement } from "@/lib/transitions";

const subscribe = () => () => {};
const getPlatform = (): Platform => detectPlatform(navigator.userAgent);
const getBrowser = (): Browser => detectBrowser(navigator.userAgent);

type Kind = "apps" | "browser";
type StoreId = (typeof extension.stores)[number]["id"];
type Choice = AppPlatform | StoreId;

const appIcons = {
  windows: WindowsIcon,
  macos: AppleIcon,
  linux: LinuxIcon,
  android: AndroidIcon,
};
const storeIcons = { chrome: ChromeIcon, edge: EdgeIcon, firefox: FirefoxIcon };
const storeNames: Record<StoreId, string> = {
  chrome: "Chrome",
  edge: "Edge",
  firefox: "Firefox",
};
/** Every choice in one order, so the button's words slide from the side that was picked, across both switches. */
const order: readonly Choice[] = [
  "windows",
  "macos",
  "linux",
  "android",
  "chrome",
  "edge",
  "firefox",
];

/**
 * Two switches and one button. The first picks the app or the browser extension; the second picks the system or the
 * browser (starting on the visitor's own); the button downloads the app or opens the store. The install steps appear
 * once an app's download has started.
 */
export function DownloadOptions({ releases }: { releases: Releases }) {
  const platform = useSyncExternalStore(
    subscribe,
    getPlatform,
    () => "unknown" as Platform,
  );
  const browser = useSyncExternalStore(
    subscribe,
    getBrowser,
    () => "unknown" as Browser,
  );
  const [kind, setKind] = useState<Kind>("apps");
  const [app, setApp] = useState<AppPlatform | null>(null);
  const [store, setStore] = useState<StoreId | null>(null);
  const [downloaded, setDownloaded] = useState<AppPlatform | null>(null);
  const motion = useSwitchMotion(order);

  const chosenApp: AppPlatform =
    app ??
    (platform === "mac"
      ? "macos"
      : platform === "linux" || platform === "android"
        ? platform
        : "windows");
  const chosenStore: StoreId =
    store ?? (browser === "unknown" ? "chrome" : browser);
  const chosen: Choice = kind === "apps" ? chosenApp : chosenStore;
  const release = releases[chosenApp];
  const listing = extension.stores.find((s) => s.id === chosenStore)!;
  const ButtonIcon = kind === "apps" ? DownloadIcon : storeIcons[chosenStore];
  const live = kind === "apps" || Boolean(listing.url);

  return (
    <div className="flex w-full flex-col items-center">
      <SegmentedSwitch
        label="What to get"
        value={kind}
        onChange={(next) =>
          motion.change(chosen, next === "apps" ? chosenApp : chosenStore, () =>
            setKind(next),
          )
        }
        options={[
          { key: "apps", label: "Apps", Icon: MonitorIcon },
          { key: "browser", label: "Browser", Icon: GlobeIcon },
        ]}
      />

      {/* The second switch follows the first (systems for the app, browsers for the extension); it doesn't slide in,
          its pill just widens or narrows to the new set. */}
      <GlidingBox swapKey={kind} className="mt-3 rounded-full bg-surface">
        {kind === "apps" ? (
          <SegmentedSwitch
            bare
            label="Your system"
            value={chosenApp}
            onChange={(next) =>
              motion.change(chosenApp, next, () => setApp(next))
            }
            options={(["windows", "macos", "linux", "android"] as const).map(
              (k) => ({
                key: k,
                label: releases[k].name,
                Icon: appIcons[k],
              }),
            )}
          />
        ) : (
          <SegmentedSwitch
            bare
            label="Your browser"
            value={chosenStore}
            onChange={(next) =>
              motion.change(chosenStore, next, () => setStore(next))
            }
            options={extension.stores.map((s) => ({
              key: s.id,
              label: storeNames[s.id],
              Icon: storeIcons[s.id],
            }))}
          />
        )}
      </GlidingBox>

      {/* One button for whatever is chosen; its width glides between labels. */}
      <SharedElement name={sharedElement.downloadButton}>
        <a
          href={kind === "apps" ? release.url : listing.url || undefined}
          {...(kind === "apps"
            ? {
                download: release.fileName,
                onClick: () => setDownloaded(chosenApp),
              }
            : {
                target: "_blank",
                rel: "noopener noreferrer",
                "aria-disabled": !listing.url,
              })}
          className={`mt-7 inline-flex items-center justify-center rounded-full px-8 py-4 text-base font-medium whitespace-nowrap transition-colors duration-300 focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-accent ${
            live
              ? "bg-ink text-paper hover:bg-ink/85 active:scale-[0.98]"
              : "cursor-default bg-surface text-muted"
          }`}
        >
          <SmoothWidth>
            {/* The download icon is the same for every system: it stays still. Browser logos turn in. */}
            <span
              key={kind === "apps" ? "icon-download" : `icon-${chosenStore}`}
              className={`inline-flex ${kind === "apps" ? "" : motion.iconIn}`}
            >
              <ButtonIcon width={18} height={18} className="shrink-0" />
            </span>
            <span key={chosen} className={`inline-block ${motion.textIn}`}>
              {kind === "apps"
                ? `Download for ${release.name}`
                : `${listing.url ? "Add to" : "Coming soon to"} ${storeNames[chosenStore]}`}
            </span>
          </SmoothWidth>
        </a>
      </SharedElement>

      <p
        key={`info-${chosen}`}
        className={`mt-4 text-sm text-muted ${motion.textIn}`}
      >
        {kind === "apps" ? (
          <>
            Version{" "}
            <LatestVersion platform={chosenApp} initial={release.version} /> ·{" "}
            {release.requirements}
          </>
        ) : (
          `Free · Works in ${listing.browsers}`
        )}
      </p>

      <div className="mt-6 empty:hidden">
        <PlatformNotice message={downloadPage.unsupportedNotice} />
      </div>

      {/* Rises in once, on the first download; after that it stays put (hidden while Browser is picked) and only its
          words change, so switching systems or downloading another one doesn't replay it. */}
      {downloaded && (
        <InstallSteps
          hidden={kind !== "apps"}
          steps={downloadPage.installSteps[downloaded]}
          name={releases[downloaded].name}
        />
      )}
    </div>
  );
}

/** The three steps for the app being downloaded, rising in once its download started. */
function InstallSteps({
  steps,
  name,
  hidden,
}: {
  steps: readonly string[];
  name: string;
  hidden: boolean;
}) {
  return (
    <section
      aria-label={`Installing on ${name}`}
      aria-live="polite"
      hidden={hidden}
      className="mt-12 w-full animate-[fade-up_0.4s_ease-out] text-left motion-reduce:animate-none"
    >
      <ol className="grid gap-3 sm:grid-cols-3 sm:gap-4">
        {steps.map((step, i) => (
          <li key={step} className="rounded-[1.5rem] bg-surface p-5">
            <span className="flex size-7 items-center justify-center rounded-full bg-ink text-xs font-semibold text-paper">
              {i + 1}
            </span>
            <p className="mt-4 text-sm text-pretty text-muted">{step}</p>
          </li>
        ))}
      </ol>
    </section>
  );
}
