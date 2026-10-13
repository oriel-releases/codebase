"use client";

import { useSyncExternalStore } from "react";
import { canRunOriel, detectPlatform, type Platform } from "@/lib/platform";

const subscribe = () => () => {};
const getPlatform = (): Platform => detectPlatform(navigator.userAgent);
const getServerPlatform = (): Platform => "unknown";

/** Shown only to visitors who aren't on Windows, Linux or Android. Renders nothing on the server. */
export function PlatformNotice({ message }: { message: string }) {
  const platform = useSyncExternalStore(subscribe, getPlatform, getServerPlatform);
  if (canRunOriel(platform)) return null;

  return <p className="rounded-3xl bg-surface px-5 py-2.5 text-sm text-balance text-muted sm:rounded-full">{message}</p>;
}
