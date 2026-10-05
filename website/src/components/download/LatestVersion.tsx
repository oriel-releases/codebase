"use client";

import { useEffect, useState } from "react";
import { fetchLatestVersion } from "@/lib/latest-version";

/** Shows the version baked in at build time, then swaps in the one currently in Firestore. */
export function LatestVersion({ initial }: { initial: string }) {
  const [version, setVersion] = useState(initial);

  useEffect(() => {
    const controller = new AbortController();
    fetchLatestVersion(controller.signal).then((latest) => {
      if (latest) setVersion(latest);
    });
    return () => controller.abort();
  }, []);

  return <>{version}</>;
}
