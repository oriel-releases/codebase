"use client";

import { useState, useSyncExternalStore } from "react";
import { extension } from "@/config/extension";
import { ChromeIcon, EdgeIcon, FirefoxIcon } from "@/components/ui/icons";
import { SegmentedSwitch } from "@/components/download/SegmentedSwitch";
import { detectBrowser, type Browser } from "@/lib/platform";

const subscribe = () => () => {};
const getBrowser = (): Browser => detectBrowser(navigator.userAgent);
const getServerBrowser = (): Browser => "unknown";

type StoreId = (typeof extension.stores)[number]["id"];
const icons = { chrome: ChromeIcon, edge: EdgeIcon, firefox: FirefoxIcon };
const names: Record<StoreId, string> = { chrome: "Chrome", edge: "Edge", firefox: "Firefox" };

/**
 * The browser extension, chosen like the app: a Chrome / Edge / Firefox switch that starts on the visitor's own
 * browser, and one big button to its store. A store that hasn't approved the listing yet says so instead.
 */
export function ExtensionPicker() {
  const detected = useSyncExternalStore(subscribe, getBrowser, getServerBrowser);
  const [picked, setPicked] = useState<StoreId | null>(null);
  const chosen: StoreId = picked ?? (detected === "unknown" ? "chrome" : detected);
  const store = extension.stores.find((s) => s.id === chosen)!;
  const Icon = icons[chosen];
  const button = "mt-7 inline-flex items-center justify-center gap-2.5 rounded-full px-8 py-4 text-base font-medium whitespace-nowrap";

  return (
    <div className="flex w-full flex-col items-center">
      <SegmentedSwitch
        label="Your browser"
        value={chosen}
        onChange={setPicked}
        options={extension.stores.map((s) => ({ key: s.id, label: names[s.id], Icon: icons[s.id] }))}
      />

      {store.url ? (
        <a
          href={store.url}
          target="_blank"
          rel="noopener noreferrer"
          className={`${button} bg-ink text-paper active:scale-[0.98] focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-accent`}
        >
          <Icon width={18} height={18} />
          Add to {names[chosen]}
        </a>
      ) : (
        <span aria-disabled className={`${button} cursor-default bg-surface text-muted`}>
          <Icon width={18} height={18} />
          Coming soon to {names[chosen]}
        </span>
      )}

      <p className="mt-4 text-sm text-muted">
        Free · {store.name}
      </p>
      <p className="mt-1 text-xs text-pretty text-muted">
        {store.url ? `Works in ${store.browsers}` : "Waiting for the store to approve it. Check back soon."}
      </p>
    </div>
  );
}
