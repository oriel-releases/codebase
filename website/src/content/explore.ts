/** Copy for /explore. UI components only render what's here. */

export const explorePage = {
  title: "Explore",
  computers: "Computers",
  phones: "Phones",
  refresh: "Refresh",
  failed: "Explore couldn't be loaded. Check your connection and try again.",
  back: "Explore",
  set: "Set as wallpaper",
  /** A wallpaper link from an earlier hour, no longer in the feed. */
  gone: "This wallpaper has moved on",
  goneBody: "Explore changes every hour. Oriel can still set it for you, or see what's new this hour.",
  seeNew: "See this hour's wallpapers",
  /** iPhones and iPads: Oriel has no app there yet. */
  phoneNote: "Open this page on your computer or Android phone to set it as your wallpaper.",
} as const;
