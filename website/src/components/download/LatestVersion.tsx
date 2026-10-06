"use client";

import { useEffect, useState } from "react";
import { fetchLatestVersion, type AppPlatform } from "@/lib/latest-version";

/** Shows the version baked in at build time, then swaps in the one currently in Firestore. */
export function LatestVersion({ platform, initial }: { platform: AppPlatform; initial: string }) {
  const [version, setVersion] = useState(initial);

  useEffect(() => {
    const controller = new AbortController();
    fetchLatestVersion(platform, controller.signal).then((latest) => {
      if (latest) setVersion(latest);
    });
    return () => controller.abort();
  }, [platform]);

  return <>{version}</>;
}
