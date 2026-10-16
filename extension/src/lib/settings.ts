export type Mode = "desktop" | "own";
export type Change = "tab" | "hourly" | "daily";

export interface Settings {
  /** "desktop" shows what Oriel on your PC set as wallpaper; "own" picks its own. Only "own" works without an account. */
  mode: Mode;
  /** Topics for "own" mode. */
  topics: string[];
  change: Change;
  /** Bumped by Shuffle so the next pick differs within the same time slot. */
  bump: number;
  clock: boolean;
  hour12: boolean;
  greeting: boolean;
  /** The "i" button at the bottom left that opens the photo's credit. */
  photoInfo: boolean;
  /** A short quote at the bottom, new each day. */
  quote: boolean;
  search: boolean;
  /** Your profile picture at the top right once an account is connected. */
  avatar: boolean;
  /** The row of shortcuts under the clock. */
  shortcuts: boolean;
  /** The gear at the top left. Settings also open from the toolbar icon, so it can go. */
  settingsButton: boolean;
  /** The shuffle button at the bottom right: another photo. */
  shuffle: boolean;
  /** 0 to 70: how much the photo is darkened so text stays readable. */
  dim: number;
  /** 0 to 24 px of background blur. */
  blur: number;
}

export const defaults: Settings = {
  mode: "own",
  topics: ["nature", "mountains", "ocean", "space", "architecture"],
  change: "daily",
  bump: 0,
  clock: true,
  hour12: false,
  greeting: true,
  photoInfo: true,
  quote: true,
  search: false,
  avatar: true,
  shortcuts: true,
  settingsButton: true,
  shuffle: true,
  dim: 18,
  blur: 0,
};

const key = "oriel.settings";

/** What the account keeps for every browser: all of it, except Shuffle's counter, which only means something here. */
export function syncedSettings(settings: Settings): Partial<Settings> {
  const { bump: _bump, ...rest } = settings;
  return rest;
}

export function loadSettings(): Settings {
  try {
    return { ...defaults, ...JSON.parse(localStorage.getItem(key) ?? "{}") };
  } catch {
    return { ...defaults };
  }
}

/** Saves the settings here. `fromSync` is for settings that just came from the account, so they aren't sent straight back. */
export function saveSettings(settings: Settings, fromSync = false): void {
  try {
    const before = JSON.stringify(syncedSettings(loadSettings()));
    localStorage.setItem(key, JSON.stringify(settings));
    // Only a change the account keeps needs uploading (not Shuffle's counter).
    if (!fromSync && JSON.stringify(syncedSettings(settings)) !== before) localStorage.setItem("oriel.ext-dirty", JSON.stringify(Date.now()));
  } catch {
    // Storage blocked: settings just won't persist.
  }
}

/** Remembers a small value across tabs; reads never throw. */
export function remember<T>(name: string, value?: T | null): T | null {
  try {
    if (value === undefined) return JSON.parse(localStorage.getItem(`oriel.${name}`) ?? "null");
    if (value === null) localStorage.removeItem(`oriel.${name}`);
    else localStorage.setItem(`oriel.${name}`, JSON.stringify(value));
  } catch {
    // ignore
  }
  return value ?? null;
}
