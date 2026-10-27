"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { keptFeed, loadFeed, refreshFeed, type ExploreFeed } from "@/lib/explore";

export type FeedState = {
  feed: ExploreFeed | null;
  /** Still asking for it the first time. */
  loading: boolean;
  /** Refresh is under way (it always takes a moment, see refreshFeed). */
  refreshing: boolean;
  refresh: () => void;
};

/**
 * This hour's Explore feed in a component: what the browser kept shows at once, a newer one replaces it when it
 * arrives, and Refresh asks again.
 */
export function useExploreFeed(): FeedState {
  const [feed, setFeed] = useState<ExploreFeed | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const current = useRef<ExploreFeed | null>(null);

  useEffect(() => {
    let live = true;
    Promise.resolve(keptFeed()).then((kept) => {
      if (!live || !kept || current.current) return;
      current.current = kept;
      setFeed(kept);
      setLoading(false);
    });
    loadFeed().then((f) => {
      if (!live) return;
      current.current = f;
      setFeed(f);
      setLoading(false);
    });
    return () => {
      live = false;
    };
  }, []);

  const refresh = useCallback(() => {
    if (refreshing) return;
    setRefreshing(true);
    refreshFeed(current.current).then((f) => {
      current.current = f;
      setFeed(f);
      setLoading(false);
      setRefreshing(false);
    });
  }, [refreshing]);

  return { feed, loading, refreshing, refresh };
}
