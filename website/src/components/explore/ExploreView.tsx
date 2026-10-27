"use client";

import { useSearchParams } from "next/navigation";
import { ExploreGrid } from "@/components/explore/ExploreGrid";
import { ExploreWallpaper } from "@/components/explore/ExploreWallpaper";
import { useExploreFeed } from "@/components/explore/useExploreFeed";

/**
 * /explore is one page: the grid, or one wallpaper when the address names it (`?wallpaper=ID`). The feed is asked for
 * once here, so going from the grid to a wallpaper and back keeps it (and Refresh's order).
 */
export function ExploreView() {
  const id = useSearchParams().get("wallpaper");
  const state = useExploreFeed();
  return id ? <ExploreWallpaper id={id} {...state} /> : <ExploreGrid {...state} />;
}
