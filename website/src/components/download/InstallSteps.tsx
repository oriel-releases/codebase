"use client";

import { useSyncExternalStore } from "react";
import type { AppPlatform } from "@/lib/latest-version";
import { detectPlatform, type Platform } from "@/lib/platform";

const subscribe = () => () => {};
const getPlatform = (): Platform => detectPlatform(navigator.userAgent);
const getServerPlatform = (): Platform => "unknown";

/** Three short steps for the visitor's system (Windows when it can't tell), in a row on wide screens. */
export function InstallSteps({ steps }: { steps: Record<AppPlatform, readonly string[]> }) {
  const platform = useSyncExternalStore(subscribe, getPlatform, getServerPlatform);
  const list = steps[platform === "linux" ? "linux" : "windows"];

  return (
    <ol className="grid gap-4 text-left sm:grid-cols-3 sm:gap-6">
      {list.map((step, i) => (
        <li key={step} className="flex items-start gap-3 text-sm text-muted">
          <span className="flex size-6 shrink-0 items-center justify-center rounded-full border border-accent/40 text-xs font-medium text-accent">
            {i + 1}
          </span>
          <span className="text-pretty">{step}</span>
        </li>
      ))}
    </ol>
  );
}
