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
  dim: 18,
  blur: 0,
};

const key = "oriel.settings";

export function loadSettings(): Settings {
  try {
    return { ...defaults, ...JSON.parse(localStorage.getItem(key) ?? "{}") };
  } catch {
    return { ...defaults };
  }
}

export function saveSettings(settings: Settings): void {
  try {
    localStorage.setItem(key, JSON.stringify(settings));
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
