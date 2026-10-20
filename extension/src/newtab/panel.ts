import { escapeHtml } from "../lib/html";
import { poolTopics } from "../lib/photo";
import type { Profile } from "../lib/account";
import type { Change, Mode, Settings } from "../lib/settings";
import { imageUrl, type Photo } from "../lib/photo";
import { todayPhoto } from "../lib/today";
import { siteUrl, supportUrl } from "../lib/config";

export interface PanelState {
  settings: Settings;
  profile: Profile | null;
  /** True while a sign-in is being finished. */
  busy: boolean;
  /** True from confirming "Sign out" until it has finished: the button shows it and can't be pressed again. */
  signingOut: boolean;
  /** What the desktop app has published so far: null means nothing yet. */
  desktop: { source: string; updatedAt: number } | null;
  /** The photo on screen, small, for the Account banner; null before there is one. */
  banner: string | null;
  /** The photo on screen, for Home; null before there is one. */
  photo?: Photo | null;
  /** True while Change now or "Set as wallpaper" is working. */
  changing?: boolean;
  /** Only in the toolbar popup: the page you're on, offered as a new shortcut. */
  here?: { title: string; url: string; favicon: string | null; added: boolean; full: boolean } | "checking" | null;
}

export interface PanelActions {
  change(patch: Partial<Settings>): void;
  /** A slider is being dragged: show the effect on the photo right away, without saving or redrawing the dialog. */
  preview(patch: Partial<Settings>): void;
  /** Google: finished on the Oriel website, which hands the sign-in back. */
  connect(): void;
  /** An Oriel account's email and password, right here. Throws the reason (in words) when it doesn't work. */
  signInEmail(email: string, password: string): Promise<void>;
  resetPassword(email: string): Promise<void>;
  disconnect(): void;
  openApp(): void;
  /** Toolbar popup: adds the page you're on to the new tab's shortcuts. */
  addHere?(): void;
  /** Home: another photo (on every device while following the account), like the shuffle button. */
  changeNow(): void;
  /** Home: makes this photo the wallpaper (Wallpaper of the day). */
  setPhoto(photo: Photo): void;
}

export type Page = "home" | "interests" | "settings" | "account" | "help";

/** Outline icons (24px grid, 2px stroke). */
const icons: Record<Page, string> = {
  home: '<path d="M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8"/><path d="M3 10a2 2 0 0 1 .709-1.528l7-5.999a2 2 0 0 1 2.582 0l7 5.999A2 2 0 0 1 21 10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>',
  interests:
    '<path d="M9.937 15.5A2 2 0 0 0 8.5 14.063l-6.135-1.582a.5.5 0 0 1 0-.962L8.5 9.936A2 2 0 0 0 9.937 8.5l1.582-6.135a.5.5 0 0 1 .963 0L14.063 8.5A2 2 0 0 0 15.5 9.937l6.135 1.581a.5.5 0 0 1 0 .964L15.5 14.063a2 2 0 0 0-1.437 1.437l-1.582 6.135a.5.5 0 0 1-.963 0z"/>',
  settings:
    '<path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"/><circle cx="12" cy="12" r="3"/>',
  account: '<circle cx="12" cy="12" r="10"/><circle cx="12" cy="10" r="3"/><path d="M7 20.662V19a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v1.662"/>',
  help: '<circle cx="12" cy="12" r="10"/><path d="m4.93 4.93 4.24 4.24M14.83 9.17l4.24-4.24M14.83 14.83l4.24 4.24M9.17 14.83l-4.24 4.24"/><circle cx="12" cy="12" r="4"/>',
};
const menu: [Page, string][] = [
  ["home", "Home"],
  ["interests", "Interests"],
  ["settings", "Settings"],
  ["help", "Help"],
];
const titles: Record<Page, string> = { home: "Home", interests: "Interests", settings: "Settings", account: "Account", help: "Help" };
const svg = (p: Page) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${icons[p]}</svg>`;

const emoji: Record<string, string> = {
  nature: "🌿",
  mountains: "🏔️",
  ocean: "🌊",
  space: "🪐",
  cities: "🌆",
  architecture: "🏛️",
  minimal: "◻️",
  dark: "🌑",
  abstract: "🎨",
  forest: "🌲",
  desert: "🏜️",
  aerial: "🛩️",
  "night sky": "🌌",
  snow: "❄️",
};

let page: Page = "home";
/** True while the "Disconnect this browser?" question is showing. */
let confirmingDisconnect = false;
let renderedPage: Page | null = null;

/** The email sign-in form: what's typed (kept across redraws), and how the last try went. */
const emailForm = { email: "", password: "", busy: false, error: "", note: "" };
/** Asks main.ts to draw the panel again with the current state. */
const redraw = (): void => void document.dispatchEvent(new CustomEvent("oriel:redraw-panel"));

/** Google's "G", in its colors. */
const googleG =
  '<svg class="g" viewBox="0 0 48 48" aria-hidden="true"><path fill="#EA4335" d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z"/><path fill="#4285F4" d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z"/><path fill="#FBBC05" d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z"/><path fill="#34A853" d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z"/></svg>';

const esc = escapeHtml;
const label = (t: string) => t.charAt(0).toUpperCase() + t.slice(1);
const device = (source: string) =>
  source === "linux" ? "Linux PC" : source === "windows" ? "Windows PC" : source === "android" ? "phone" : source === "extension" ? "browser" : "computer";

const changeOptions: [Change, string][] = [
  ["tab", "Every tab"],
  ["hourly", "Hourly"],
  ["daily", "Daily"],
];

/** A segmented control. The pill behind the options slides to the chosen one (see .seg in style.css). */
function seg(id: string, aria: string, options: { value: string; text: string; disabled?: boolean; title?: string }[], active: string, act: string): string {
  const index = Math.max(0, options.findIndex((o) => o.value === active));
  return `<div class="seg" data-seg="${id}" style="--n:${options.length};--i:${index}" role="group" aria-label="${esc(aria)}">${options
    .map((o) => `<button aria-pressed="${o.value === active}" data-act="${act}" data-v="${o.value}" ${o.disabled ? "disabled" : ""} ${o.title ? `title="${esc(o.title)}"` : ""}>${o.text}</button>`)
    .join("")}</div>`;
}

/** A settings row: title and hint on the left, the control on the right. */
const row = (title: string, hint: string, control: string, icon?: keyof typeof iconPaths) =>
  `<div class="card">${icon ? `<span class="tile-ico sm round" aria-hidden="true">${ico(icon)}</span>` : ""}<div class="min-w-0 flex-1"><div class="t">${title}</div>${hint ? `<div class="d">${hint}</div>` : ""}</div>${control}</div>`;

function toggle(c: Settings, name: "clock" | "hour12" | "clockAnimation" | "greeting" | "quote" | "search" | "photoInfo" | "avatar" | "settingsButton" | "shuffle" | "shortcuts", title: string, hint = "", icon?: keyof typeof iconPaths): string {
  return row(title, hint, `<label class="switch"><input type="checkbox" data-act="toggle" data-v="${name}" ${c[name] ? "checked" : ""} aria-label="${esc(title)}" /><span></span></label>`, icon);
}

/** The popup's first card: the page you're on, one click from becoming a shortcut on the new tab. */
function herePage(h: NonNullable<PanelState["here"]>): string {
  // Drawn at once while the popup asks the browser which page it's on (a moment), so nothing jumps in later.
  if (h === "checking") {
    return `<div class="card here">
      <span class="tile-ico sm round" aria-hidden="true">${ico("link")}</span>
      <div class="min-w-0 flex-1"><div class="t">This page</div><div class="d">&nbsp;</div></div>
      <span class="checking">Checking…</span>
    </div>`;
  }
  let host = h.url;
  try {
    host = new URL(h.url).hostname.replace(/^www\./, "");
  } catch {
    // keep the address as it is
  }
  const action = h.added
    ? `<span class="pill">${ico("check", "size-3")}In shortcuts</span>`
    : h.full
      ? `<span class="pill warn">Shortcuts full</span>`
      : `<button class="btn btn-primary" data-act="add-here">Add to shortcuts</button>`;
  return `<div class="card here">
      <span class="tile-ico sm round" aria-hidden="true">${h.favicon ? `<img src="${esc(h.favicon)}" alt="" loading="lazy" data-keep />` : ""}${ico("link")}</span>
      <div class="min-w-0 flex-1"><div class="t truncate">${esc(h.title)}</div><div class="d truncate">${esc(host)}</div></div>
      ${action}
    </div>`;
}

/** Change now's icon turns once after a click. */
let turnUntil = 0;

/**
 * Home, the first page, like the apps' Home: the photo on screen with its credit and Change now, the Wallpaper of the
 * day, and the page you're on (toolbar popup only) one click from becoming a shortcut.
 */
function homePage(s: PanelState): string {
  const c = s.settings;
  const following = c.mode === "desktop" && !!s.profile;
  // Under the credit: where the photo comes from, live while following the account's devices.
  const from = following
    ? s.desktop
      ? `<span class="live-dot" aria-hidden="true"></span>Live · last changed on your ${device(s.desktop.source)}`
      : `Waiting for your other devices · <a href="https://oriel.akbarjon.uz/download" target="_blank" rel="noopener">Get Oriel</a>`
    : `A new photo ${c.change === "tab" ? "every tab" : c.change === "hourly" ? "every hour" : "every day"}`;
  // The wallpaper source sits on the photo itself. Without an account, "Same as my devices" leads to Account.
  const source = `<div class="home-source">${seg(
    "source",
    "Wallpaper source",
    [
      { value: "desktop", text: `${ico("monitor")}My devices`, title: s.profile ? "Same wallpaper as your devices" : "Connect your account first" },
      { value: "own", text: `${ico("sparkle")}Its own`, title: "Photos picked here, from your interests" },
    ],
    following ? "desktop" : "own",
    "mode",
  )}</div>`;
  const p = s.photo;
  const credit = (x: Photo) =>
    `Photo by <a class="credit-name" href="${esc(x.photographerUrl)}" target="_blank" rel="noopener">${esc(x.photographer)}</a> on <a href="${esc(x.pageUrl)}" target="_blank" rel="noopener">Unsplash</a>`;
  const busy = s.changing ? "disabled aria-busy=\"true\"" : "";
  const icon = s.changing ? "spinning" : Date.now() < turnUntil ? "turn" : "";
  // The image itself is placed by showHomePhoto (after each draw), so a new photo can fade in once it has loaded.
  // Following the account's devices, the photo comes from them: no Change now here.
  const photo = `<div class="home-photo" data-src="${p ? esc(imageUrl(p, 1200, 560, 75)) : ""}"><div class="layers" data-keep></div>${source}</div>
    <div class="home-bar">
      <div class="min-w-0 flex-1">
        <div class="t truncate home-credit" data-photo="${p?.photographer ? esc(p.id) : ""}">${p?.photographer ? credit(p) : "&nbsp;"}</div>
        <div class="d truncate">${from}</div>
      </div>
      ${following ? "" : `<button class="btn btn-accent" data-act="change-now" ${busy}>${ico("refresh", icon)}${s.changing ? "Changing…" : "Change now"}</button>`}
    </div>`;
  const t = todayPhoto();
  const isToday = p?.id === t.id;
  const today = `<div class="card today">
      <img class="today-thumb" src="${esc(imageUrl(t, 320, 200, 60))}" alt="" loading="lazy" />
      <div class="min-w-0 flex-1"><div class="t">Wallpaper of the day</div><div class="d truncate">${credit(t)}</div></div>
      ${isToday ? `<span class="pill">${ico("check", "size-3")}On your screen</span>` : `<button class="btn btn-ghost" data-act="set-today" ${busy}>Set as wallpaper</button>`}
    </div>`;
  const here = s.here ? `<div class="group-title">This page</div>${herePage(s.here)}` : "";
  return photo + today + here;
}

/** Interests: the topics "Its own" picks its photos from. */
function interestsPage(s: PanelState): string {
  const c = s.settings;
  const following = c.mode === "desktop" && !!s.profile;
  return `${
    following
      ? `<div class="note">${ico("monitor")}<span>Your devices choose the photo while it's <b>Same as my devices</b>. These are used for <b>Its own</b>.</span></div>`
      : ""
  }
    <div class="card stack"><div class="flex flex-wrap gap-2" role="group" aria-label="Interests">
      ${poolTopics.map((t) => `<button class="chip" aria-pressed="${c.topics.includes(t)}" data-act="topic" data-v="${esc(t)}"><span class="emoji" aria-hidden="true">${emoji[t] ?? "🖼️"}</span>${esc(label(t))}</button>`).join("")}
    </div></div>`;
}

/** Settings: how often the photo changes, and what the new tab shows. Each group is one card, its rows split by lines. */
function settingsPage(s: PanelState): string {
  const c = s.settings;
  const following = c.mode === "desktop" && !!s.profile;
  const slider = (name: "dim" | "blur", title: string, _hint: string, max: number, icon: keyof typeof iconPaths) =>
    `<div class="card slider-card"><span class="tile-ico sm round" aria-hidden="true">${ico(icon)}</span><div class="t">${title}</div><input class="slider-input" type="range" min="0" max="${max}" value="${c[name]}" style="--p:${(c[name] / max) * 100}%" data-act="range" data-v="${name}" aria-label="${title}" /><output class="val" data-out="${name}">${c[name]}</output></div>`;
  return `<div class="compact">
    <div class="group-title" style="margin-top:0">Schedule</div>
    <div class="list">${row(
      "New photo",
      "",
      seg("change", "How often the photo changes", changeOptions.map(([value, text]) => ({ value, text, disabled: following, title: following ? "Your devices decide while it's Same as my devices" : "" })), c.change, "change"),
      "calendar",
    )}</div>
    <div class="group-title">Clock</div>
    <div class="list">
    <div class="card stack clock-card">
      <div class="clock-head"><span class="tile-ico sm round" aria-hidden="true">${ico("clock")}</span><div class="t min-w-0 flex-1">Clock</div><label class="switch"><input type="checkbox" data-act="toggle" data-v="clock" ${c.clock ? "checked" : ""} aria-label="Clock" /><span></span></label></div>
      <div class="fold ${c.clock ? "open" : ""}" ${c.clock ? "" : "inert"}><div class="inner clock-format">${seg("hour12", "Time format", [{ value: "24", text: "24-hour" }, { value: "12", text: "12-hour" }], c.hour12 ? "12" : "24", "hour12")}</div></div>
    </div>
    ${toggle(c, "clockAnimation", "Animate clock", "", "sparkle")}
    </div>
    <div class="group-title">On the page</div>
    <div class="list">
    ${toggle(c, "greeting", "Greeting", "", "smile")}
    ${toggle(c, "quote", "Quote", "", "quote")}
    ${toggle(c, "search", "Search bar", "", "search")}
    ${toggle(c, "shortcuts", "Shortcuts", "", "link")}
    ${toggle(c, "photoInfo", "Photo info button", "", "info")}
    ${toggle(c, "avatar", "Profile picture", "", "user")}
    ${toggle(c, "shuffle", "Shuffle button", "", "shuffle")}
    ${toggle(c, "settingsButton", "Settings button", "", "gear")}
    </div>
    <div class="group-title">Photo</div>
    <div class="list">
    ${slider("dim", "Dim", "", 70, "dim")}
    ${slider("blur", "Blur", "", 24, "blur")}
    </div>
    </div>`;
}

const iconPaths = {
  sparkle: '<path d="M9.937 15.5A2 2 0 0 0 8.5 14.063l-6.135-1.582a.5.5 0 0 1 0-.962L8.5 9.936A2 2 0 0 0 9.937 8.5l1.582-6.135a.5.5 0 0 1 .963 0L14.063 8.5A2 2 0 0 0 15.5 9.937l6.135 1.581a.5.5 0 0 1 0 .964L15.5 14.063a2 2 0 0 0-1.437 1.437l-1.582 6.135a.5.5 0 0 1-.963 0z"/>',
  calendar: '<rect width="18" height="18" x="3" y="4" rx="2"/><path d="M16 2v4M8 2v4M3 10h18"/>',
  refresh: '<path d="M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8"/><path d="M21 3v5h-5"/>',
  link: '<path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"/><path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"/>',
  shuffle: '<path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8"/><path d="M21 3v5h-5"/><path d="M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16"/><path d="M8 16H3v5"/>',
  gear: '<path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"/><circle cx="12" cy="12" r="3"/>',
  clock: '<circle cx="12" cy="12" r="10"/><path d="M12 6v6l4 2"/>',
  smile: '<circle cx="12" cy="12" r="10"/><path d="M8 14s1.5 2 4 2 4-2 4-2M9 9h.01M15 9h.01"/>',
  quote: '<path d="M3 21c3 0 7-1 7-8V5c0-1.25-.756-2.017-2-2H4c-1.25 0-2 .75-2 1.972V11c0 1.25.75 2 2 2 1 0 1 0 1 1v1c0 1-1 2-2 2s-1 .008-1 1.031V20c0 1 0 1 1 1z"/><path d="M15 21c3 0 7-1 7-8V5c0-1.25-.757-2.017-2-2h-4c-1.25 0-2 .75-2 1.972V11c0 1.25.75 2 2 2h.75c0 2.25.25 4-2.75 4v3c0 1 0 1 1 1z"/>',
  search: '<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>',
  info: '<circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/>',
  user: '<path d="M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>',
  dim: '<circle cx="12" cy="12" r="4"/><path d="M12 4h.01M20 12h.01M12 20h.01M4 12h.01M17.657 6.343h.01M17.657 17.657h.01M6.343 17.657h.01M6.343 6.343h.01"/>',
  blur: '<path d="M12 22a7 7 0 0 0 7-7c0-2-1-3.9-3-5.5s-3.5-4-4-6.5c-.5 2.5-2 4.9-4 6.5C6 11.1 5 13 5 15a7 7 0 0 0 7 7z"/>',
  monitor: '<rect width="20" height="14" x="2" y="3" rx="2"/><path d="M8 21h8M12 17v4"/>',
  globe: '<circle cx="12" cy="12" r="10"/><path d="M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20M2 12h20"/>',
  check: '<path d="M20 6 9 17l-5-5"/>',
  send: '<path d="m22 2-7 20-4-9-9-4Z"/><path d="M22 2 11 13"/>',
  logout: '<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9"/>',
  external: '<path d="M15 3h6v6M10 14 21 3M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/>',
};
const ico = (name: keyof typeof iconPaths, cls = "") =>
  `<svg class="${cls}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${iconPaths[name]}</svg>`;

/** "5 min ago", "2 h ago", "yesterday" */
function ago(ms: number): string {
  if (!ms) return "";
  const m = Math.max(0, Math.round((Date.now() - ms) / 60_000));
  if (m < 1) return "just now";
  if (m < 60) return `${m} min ago`;
  const h = Math.round(m / 60);
  return h < 24 ? `${h} h ago` : `${Math.round(h / 24)} d ago`;
}

function accountPage(s: PanelState): string {
  const p = s.profile;
  if (p) {
    const following = s.settings.mode === "desktop";
    const desk = s.desktop ? `${device(s.desktop.source)} · ${ago(s.desktop.updatedAt) || "recently"}` : "Waiting for your other devices";
    return `<div class="acct">
        <div class="acct-banner" aria-hidden="true" data-src="${esc(s.banner ?? "")}">
          <div class="layers" data-keep></div>
          <i></i>
        </div>
        <div class="acct-body">
          <div class="acct-top">
            <div class="acct-avatar">${avatar(p, "size-16")}</div>
            ${
              s.signingOut
                ? `<button class="signout" disabled aria-busy="true"><i class="spin"></i>Signing out…</button>`
                : `<button class="signout" data-act="disconnect">${ico("logout")}Sign out</button>`
            }
          </div>
          <div class="who truncate">${esc(p.name || p.email)}</div>
          ${p.name ? `<div class="mail truncate">${esc(p.email)}</div>` : ""}
          <div class="facts">
            <span class="fact">${ico("monitor")}${esc(desk)}</span>
            <span class="fact">${ico("globe")}${following ? "Matches your desktop" : "Picks its own photos"}</span>
          </div>
        </div>
      </div>
      <div class="acct-actions">
        <a class="btn btn-primary btn-wide" href="oriel://open" data-act="open-app">Open Oriel</a>
        <a class="row-link" href="${siteUrl()}/me" target="_blank" rel="noopener"><span>Manage account</span>${ico("external")}</a>
      </div>`;
  }
  const perks = ["The same photo on your PC and in your browser", "Updates the moment your desktop wallpaper changes", "Free, with Google or email"];
  const f = emailForm;
  const waiting = s.busy && !f.busy;
  return `<div class="hero">
      <div class="link-art" aria-hidden="true">
        <span class="tile-ico">${ico("monitor")}</span>
        <span class="flow"><i></i></span>
        <span class="tile-ico">${ico("globe")}</span>
      </div>
      <h3>Same wallpaper, everywhere</h3>
      <ul class="perks">${perks.map((t) => `<li>${ico("check")}${t}</li>`).join("")}</ul>
      <button class="btn btn-primary btn-lg" data-act="connect" ${s.busy ? "disabled" : ""}>${waiting ? "Waiting for you to sign in…" : `${googleG}Continue with Google`}</button>
      <div class="or"><span>or</span></div>
      <form class="email-form" data-form="email" novalidate>
        <input class="field" type="email" data-field="email" placeholder="Email" autocomplete="email" aria-label="Email" value="${esc(f.email)}" />
        <input class="field" type="password" data-field="password" placeholder="Password" autocomplete="current-password" aria-label="Password" value="${esc(f.password)}" />
        <button class="btn btn-ghost btn-lg" type="submit" ${s.busy ? "disabled" : ""}>${f.busy ? '<i class="spin"></i>Signing in…' : "Sign in with email"}</button>
        ${f.error ? `<p class="form-msg err" role="alert">${esc(f.error)}</p>` : f.note ? `<p class="form-msg">${esc(f.note)}</p>` : ""}
        <div class="form-links">
          <button type="button" class="link" data-act="reset-password">Forgot password?</button>
          <a class="link" href="${siteUrl()}/login" target="_blank" rel="noopener">Create an account</a>
        </div>
      </form>
    </div>`;
}

async function submitEmail(actions: PanelActions): Promise<void> {
  const f = emailForm;
  if (f.busy) return;
  f.note = "";
  if (!f.email.trim() || !f.password) {
    f.error = "Enter your email and password.";
    return redraw();
  }
  f.busy = true;
  f.error = "";
  redraw();
  try {
    await actions.signInEmail(f.email, f.password);
    f.password = "";
  } catch (e) {
    f.error = e instanceof Error ? e.message : "Couldn't sign in. Please try again.";
  } finally {
    f.busy = false;
    redraw();
  }
}

async function forgotPassword(actions: PanelActions): Promise<void> {
  const f = emailForm;
  f.error = "";
  f.note = "";
  if (!f.email.trim()) {
    f.error = "Enter your email first, then tap Forgot password.";
    return redraw();
  }
  try {
    await actions.resetPassword(f.email);
    f.note = "Check your inbox for a link to set a new password.";
  } catch (e) {
    f.error = e instanceof Error ? e.message : "Couldn't send the email. Please try again.";
  }
  redraw();
}

/** Chrome (not Edge, Brave or Firefox) adds its own footer under an extension's new tab since version 138. */
const chromeFooter = /Chrome\//.test(navigator.userAgent) && !/Edg\/|OPR\//.test(navigator.userAgent) && !("brave" in navigator);

function helpPage(): string {
  return `
    <div class="card">
      <div class="flex min-w-0 items-center gap-3">
        <span class="tg" aria-hidden="true"><svg viewBox="0 0 24 24"><path fill="#2AABEE" d="M11.944 0A12 12 0 0 0 0 12a12 12 0 0 0 12 12 12 12 0 0 0 12-12A12 12 0 0 0 12 0a12 12 0 0 0-.056 0zm4.962 7.224c.1-.002.321.023.465.14a.506.506 0 0 1 .171.325c.016.093.036.306.02.472-.18 1.898-.962 6.502-1.36 8.627-.168.9-.499 1.201-.82 1.23-.696.065-1.225-.46-1.9-.902-1.056-.693-1.653-1.124-2.678-1.8-1.185-.78-.417-1.21.258-1.91.177-.184 3.247-2.977 3.307-3.23.007-.032.014-.15-.056-.212s-.174-.041-.249-.024c-.106.024-1.793 1.14-5.061 3.345-.48.33-.913.49-1.302.48-.428-.008-1.252-.241-1.865-.44-.752-.245-1.349-.374-1.297-.789.027-.216.325-.437.893-.663 3.498-1.524 5.83-2.529 6.998-3.014 3.332-1.386 4.025-1.627 4.476-1.635z"/></svg></span>
        <div class="min-w-0"><div class="t">Contact support</div></div>
      </div>
      <a class="btn btn-primary" href="${supportUrl}" target="_blank" rel="noopener">Open Telegram</a>
    </div>
    ${chromeFooter ? `<div class="tip"><span class="tile-ico xs" aria-hidden="true">${ico("info")}</span><div>Chrome shows its own bar at the bottom of the new tab. To hide it, <b>right-click it</b> and choose <b>Hide footer on New Tab page</b>.</div></div>` : ""}
    <p class="foot">Oriel ${esc(chrome.runtime.getManifest().version)} · <a href="https://oriel.akbarjon.uz" target="_blank" rel="noopener">Website</a> · <a href="https://oriel.akbarjon.uz/privacy" target="_blank" rel="noopener">Privacy</a> · Photos from <a href="https://unsplash.com/?utm_source=oriel&utm_medium=referral" target="_blank" rel="noopener">Unsplash</a></p>`;
}

function avatar(p: Profile, size: string): string {
  return p.photo
    ? `<img src="${esc(p.photo)}" alt="" referrerpolicy="no-referrer" class="${size} shrink-0 rounded-full object-cover" />`
    : `<div class="${size} grid shrink-0 place-items-center rounded-full bg-white/15">${esc((p.name || p.email || "?")[0].toUpperCase())}</div>`;
}

function html(s: PanelState): string {
  const body = {
    home: () => homePage(s),
    interests: () => interestsPage(s),
    settings: () => settingsPage(s),
    account: () => accountPage(s),
    help: helpPage,
  }[page]();
  const p = s.profile;
  return `
    <aside class="side">
      <div class="brand"><img src="icons/icon-48.png" alt="" />Oriel<span class="beta">Beta</span></div>
      <nav class="nav" aria-label="Settings">
        ${menu.map(([id, text]) => `<button class="nav-item" data-act="nav" data-v="${id}" ${id === page ? 'aria-current="page"' : ""}>${svg(id)}<span>${text}</span></button>`).join("")}
      </nav>
      <span class="bar" data-keep></span>
      <div class="nav-bottom"><button class="nav-item" data-act="nav" data-v="account" ${page === "account" ? 'aria-current="page"' : ""}>${p ? avatar(p, "size-[1.1rem]") : svg("account")}<span>Account</span></button></div>
    </aside>
    <section class="content" aria-labelledby="page-title">
      <button class="icon-btn close-x" style="color:rgb(255 255 255/.8)" data-act="close" aria-label="Close settings"><svg viewBox="0 0 24 24" class="size-5" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M6 6l12 12M18 6 6 18"/></svg></button>
      <h2 class="page-title" id="page-title">${titles[page]}</h2>
      ${body}
    </section>
    <div class="confirm-layer ${confirmingDisconnect ? "open" : ""}" aria-hidden="${!confirmingDisconnect}">
      <div class="confirm-card" role="alertdialog" aria-label="Sign out">
        <span class="confirm-icon">${ico("logout")}</span>
        <h3>Sign out of this browser?</h3>
        <div class="confirm-buttons">
          <button class="btn btn-ghost" data-act="cancel-disconnect">Cancel</button>
          <button class="btn btn-danger solid" data-act="confirm-disconnect">Sign out</button>
        </div>
      </div>
    </div>`;
}

// ---- Updating the dialog in place, so switches, chips and the segmented pill animate instead of being rebuilt.

function morphChildren(from: Element, to: Element): void {
  const a = [...from.childNodes];
  const b = [...to.childNodes];
  for (let i = 0; i < b.length; i++) {
    const x = a[i];
    const y = b[i];
    if (!x) from.appendChild(y.cloneNode(true));
    else if (x instanceof Element && y instanceof Element && x.tagName === y.tagName) morph(x, y);
    else if (x.nodeType === 3 && y.nodeType === 3) {
      if (x.nodeValue !== y.nodeValue) x.nodeValue = y.nodeValue;
    } else from.replaceChild(y.cloneNode(true), x);
  }
  for (let i = a.length - 1; i >= b.length; i--) from.removeChild(a[i]);
}

function morph(from: Element, to: Element): void {
  if (from.hasAttribute("data-keep")) return; // owned by script (the menu marker, a site icon that faded in)
  for (const attr of [...from.attributes]) if (!to.hasAttribute(attr.name)) from.removeAttribute(attr.name);
  for (const attr of [...to.attributes]) if (from.getAttribute(attr.name) !== attr.value) from.setAttribute(attr.name, attr.value);
  if (from instanceof HTMLInputElement && to instanceof HTMLInputElement) {
    from.checked = to.checked;
    if (document.activeElement !== from) from.value = to.value;
    return;
  }
  morphChildren(from, to);
}

/** Draws the dialog. Same page: patched in place (animated). New page: the menu is patched and the content swaps in with a soft stagger. */
export function renderPanel(root: HTMLElement, state: PanelState, actions: PanelActions): void {
  const next = document.createElement("div");
  next.innerHTML = html(state);

  const content = root.querySelector<HTMLElement>(".content");
  // Nothing should glide or fade while the dialog is hidden: it just gets drawn in its final state.
  const visible = root.classList.contains("open");
  if (!content) {
    root.innerHTML = next.innerHTML; // first open: appears with the dialog, no extra motion
  } else if (renderedPage !== page) {
    morph(root.querySelector("aside")!, next.querySelector("aside")!);
    content.innerHTML = next.querySelector(".content")!.innerHTML;
    content.scrollTop = 0;
    if (visible) fade(content);
  } else {
    morphChildren(root, next);
  }
  renderedPage = page;
  placeBar(root, !content || !visible);
  showHomePhoto(root);
  showCredit(root, visible);
  bind(root, state, actions);
}

/**
 * Home's photo. A new one (or Home's first) is added on top and only appears once it has fully loaded: it fades in and
 * settles from a slight zoom over the one before, which is then removed. Only opacity and scale animate (cheap).
 */
/** The photo Home showed last: coming back to Home with the same one shows it at once, without the fade. */
const shownIn: Record<string, string> = {};

/** Home's photo and the Account page's banner: both change with the wallpaper, crossfading (showPhotoIn). */
function showHomePhoto(root: HTMLElement): void {
  // The toolbar popup only appears once its page has loaded, pictures included: start the download after that.
  if (document.readyState !== "complete") {
    window.addEventListener("load", () => showHomePhoto(root), { once: true });
    return;
  }
  showPhotoIn(root, ".home-photo", true);
  showPhotoIn(root, ".acct-banner", false);
}

/**
 * Fades a new photo in over the old one. Home's settles from a slight zoom too (`zoom`); the Account banner only fades.
 * Coming back to a page with the same photo shows it at once.
 */
function showPhotoIn(root: HTMLElement, selector: string, zoom: boolean): void {
  const box = root.querySelector<HTMLElement>(selector);
  const layers = box?.querySelector<HTMLElement>(".layers");
  const src = box?.dataset.src;
  if (!box || !layers || !src || layers.lastElementChild?.getAttribute("src") === src) return;
  const img = new Image();
  img.alt = "";
  img.src = src;
  // Switching back to the page (the layers were rebuilt) with the same photo: no animation, it's simply there.
  const animate = src !== shownIn[selector];
  shownIn[selector] = src;
  if (!animate) {
    layers.replaceChildren(img);
    return;
  }
  img.style.opacity = "0";
  layers.append(img);
  void img
    .decode()
    .catch(() => {}) // a broken image still replaces the old one
    .then(() => {
      if (!img.isConnected) return;
      img.style.opacity = "";
      const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
      const done = () => {
        while (img.previousElementSibling) img.previousElementSibling.remove();
      };
      if (reduced) return done();
      const frames = zoom ? [{ opacity: 0, scale: 1.06 }, { opacity: 1, scale: 1 }] : [{ opacity: 0 }, { opacity: 1 }];
      img.animate(frames, { duration: zoom ? 900 : 600, easing: "cubic-bezier(0.2, 0.7, 0.2, 1)" }).finished.then(done, done);
    });
}

/** The photo Home's credit names, and how wide that name is drawn. */
let creditFor = "";
let nameWidth = 0;

/**
 * A new photo (Change now, or one from another device): only the author's name changes, rising in while its box glides
 * from the old name's width to the new one, so "on Unsplash" after it slides along. "Photo by" stays still.
 */
function showCredit(root: HTMLElement, visible: boolean): void {
  const credit = root.querySelector<HTMLElement>(".home-credit");
  const name = credit?.querySelector<HTMLElement>(".credit-name");
  const id = credit?.dataset.photo;
  if (!name || !id || id === creditFor) return;
  const from = nameWidth;
  const changed = creditFor !== "";
  creditFor = id;
  nameWidth = name.offsetWidth;
  if (!changed || !visible || !from || matchMedia("(prefers-reduced-motion: reduce)").matches) return;
  name.animate(
    [
      { width: `${from}px`, opacity: 0, translate: "0 0.4em", filter: "blur(3px)" },
      { width: `${nameWidth}px`, opacity: 1, translate: "0 0", filter: "blur(0)" },
    ],
    { duration: 450, easing: "cubic-bezier(0.2, 0.7, 0.2, 1)" },
  );
}

/** Moves the vertical marker to the current menu item. On the very first draw it jumps there instead of gliding in from the top. */
function placeBar(root: HTMLElement, instant: boolean): void {
  const bar = root.querySelector<HTMLElement>(".bar");
  const current = root.querySelector<HTMLElement>(".side [aria-current=\"page\"]");
  if (!bar || !current) return;
  const height = Math.round(current.offsetHeight * 0.5);
  const y = current.offsetTop + Math.round((current.offsetHeight - height) / 2) + (current.offsetParent === bar.offsetParent ? 0 : current.offsetParent instanceof HTMLElement ? current.offsetParent.offsetTop : 0);
  if (instant) bar.style.transition = "none";
  bar.style.height = `${height}px`;
  bar.style.translate = `0 ${y}px`;
  bar.style.opacity = "1";
  if (instant) {
    void bar.offsetWidth;
    bar.style.transition = "";
  }
}

/** Switching pages: a quick fade only, nothing moves. */
function fade(content: HTMLElement): void {
  content.classList.remove("enter");
  void content.offsetWidth; // restart the animation
  content.classList.add("enter");
}

/** Forgets any open question (the sign-out confirmation); called once the panel has finished closing. */
export function resetQuestions(): void {
  confirmingDisconnect = false;
}

export function openPage(next: Page): void {
  page = next;
}

/**
 * While a slider is held, only its row stays: the dialog, its menu and the dim backdrop fade away (style.css), so the
 * photo can be judged as it really looks.
 */
const isRange = (t: EventTarget | null): t is HTMLInputElement => t instanceof HTMLInputElement && t.dataset.act === "range";
let adjustingTimer: number | undefined;
let adjustingBound = false;

function startAdjusting(root: HTMLElement, e: Event): void {
  if (!isRange(e.target) || document.documentElement.hasAttribute("data-popup")) return; // the popup has no photo to look at
  root.dataset.adjusting = e.target.dataset.v;
  document.body.classList.add("adjusting");
}

function stopAdjusting(root: HTMLElement): void {
  clearTimeout(adjustingTimer);
  delete root.dataset.adjusting;
  document.body.classList.remove("adjusting");
}

let imageErrorsBound = false;

/**
 * The Android app's tab animations (ui/NavBar.kt), same keyframes and timings, scaled to these smaller icons: Home hops,
 * Interests beats (like AI), the gear springs half a turn (like Settings), Help winds back a full turn (like History),
 * Account wiggles (like Search). Compose keyframes move in straight lines between points, as these do.
 */
const navMotion: Record<Page, { frames: Keyframe[]; duration: number; easing?: string }> = {
  home: {
    frames: [
      { transform: "translateY(0) scale(1)" },
      { transform: "translateY(-5px) scale(1.15)", offset: 160 / 520 },
      { transform: "translateY(0) scale(0.94)", offset: 320 / 520 },
      { transform: "translateY(-1.5px) scale(1.03)", offset: 420 / 520 },
      { transform: "translateY(0) scale(1)" },
    ],
    duration: 520,
  },
  interests: {
    frames: [
      { transform: "scale(1)" },
      { transform: "scale(1.28)", offset: 120 / 620 },
      { transform: "scale(0.95)", offset: 240 / 620 },
      { transform: "scale(1.15)", offset: 360 / 620 },
      { transform: "scale(1)", offset: 480 / 620 },
      { transform: "scale(1)" },
    ],
    duration: 620,
  },
  // spring(dampingRatio 0.5, stiffness 110) to 180 degrees: overshoots to about 209, swings back to 175, settles.
  settings: {
    frames: [
      { transform: "rotate(0)", easing: "cubic-bezier(0.3, 0.6, 0.5, 1)" },
      { transform: "rotate(209deg)", offset: 0.32, easing: "ease-in-out" },
      { transform: "rotate(175deg)", offset: 0.63, easing: "ease-in-out" },
      { transform: "rotate(181deg)", offset: 0.9, easing: "ease-in-out" },
      { transform: "rotate(180deg)" },
    ],
    duration: 1100,
  },
  help: { frames: [{ transform: "rotate(0)" }, { transform: "rotate(-360deg)" }], duration: 750, easing: "cubic-bezier(0.4, 0, 0.2, 1)" },
  account: {
    frames: [
      { transform: "rotate(0)" },
      { transform: "rotate(-20deg)", offset: 110 / 560 },
      { transform: "rotate(16deg)", offset: 240 / 560 },
      { transform: "rotate(-8deg)", offset: 360 / 560 },
      { transform: "rotate(4deg)", offset: 460 / 560 },
      { transform: "rotate(0)" },
    ],
    duration: 560,
  },
};

/** Plays the clicked menu item's icon motion (Web Animations, so a redraw of the menu doesn't cut it short). */
function playNavIcon(root: HTMLElement, id: Page): void {
  if (matchMedia("(prefers-reduced-motion: reduce)").matches) return;
  const icon = root.querySelector<HTMLElement>(`.nav-item[data-v="${id}"] > svg`);
  const { frames, duration, easing = "linear" } = navMotion[id];
  icon?.animate(frames, { duration, easing });
}

function bind(root: HTMLElement, state: PanelState, actions: PanelActions): void {
  if (!imageErrorsBound) {
    imageErrorsBound = true;
    // A favicon that fails to load (this page, on Home) is removed, which shows the letter or icon behind it.
    root.addEventListener("error", (e) => e.target instanceof HTMLImageElement && e.target.closest(".here") && e.target.remove(), true);
    root.addEventListener("load", (e) => e.target instanceof HTMLImageElement && e.target.closest(".here") && e.target.classList.add("loaded"), true);
  }
  root.onclick = (e) => {
    if ((e.target as HTMLElement).classList.contains("confirm-layer")) {
      confirmingDisconnect = false;
      return renderPanel(root, state, actions);
    }
    const el = (e.target as HTMLElement).closest<HTMLElement>("[data-act]");
    if (!el || el instanceof HTMLInputElement) return;
    const v = el.dataset.v ?? "";
    switch (el.dataset.act) {
      case "nav": {
        const moved = page !== v; // the page already open doesn't play again
        page = v as Page;
        confirmingDisconnect = false;
        renderPanel(root, state, actions);
        return moved ? playNavIcon(root, page) : undefined;
      }
      case "open-app":
        return actions.openApp(); // the link itself launches Oriel; this only tells the person
      case "connect":
        return actions.connect();
      case "reset-password":
        return void forgotPassword(actions);
      case "disconnect":
        confirmingDisconnect = true;
        return renderPanel(root, state, actions);
      case "cancel-disconnect":
        confirmingDisconnect = false;
        return renderPanel(root, state, actions);
      case "confirm-disconnect":
        confirmingDisconnect = false;
        return actions.disconnect();
      case "close":
        return void document.dispatchEvent(new CustomEvent("oriel:close-panel"));
      case "add-here":
        return actions.addHere?.();
      case "change-now": {
        // Restart the turn even if the icon still has it from the last click.
        turnUntil = Date.now() + 650;
        const icon = el.querySelector("svg");
        icon?.classList.remove("turn");
        void icon?.getBoundingClientRect();
        icon?.classList.add("turn");
        return actions.changeNow();
      }
      case "set-today":
        return actions.setPhoto(todayPhoto());
      case "mode":
        if (v === "desktop" && !state.profile) {
          page = "account";
          return renderPanel(root, state, actions);
        }
        return actions.change({ mode: v as Mode });
      case "change":
        return actions.change({ change: v as Change });
      case "hour12":
        return actions.change({ hour12: v === "12" });
      case "topic": {
        const topics = state.settings.topics.includes(v) ? state.settings.topics.filter((t) => t !== v) : [...state.settings.topics, v];
        return topics.length ? actions.change({ topics }) : undefined; // at least one topic stays on
      }
    }
  };
  root.onchange = (e) => {
    const el = e.target as HTMLInputElement;
    if (el.dataset.act === "toggle") actions.change({ [el.dataset.v!]: el.checked } as Partial<Settings>);
    // A slider was let go (or a key press finished): now the value is saved and the dialog redrawn.
    if (el.dataset.act === "range") actions.change({ [el.dataset.v!]: Number(el.value) } as Partial<Settings>);
  };
  root.onpointerdown = (e) => startAdjusting(root, e);
  root.onkeydown = (e) => {
    if (isRange(e.target) && e.key.startsWith("Arrow")) {
      startAdjusting(root, e);
      clearTimeout(adjustingTimer);
      adjustingTimer = window.setTimeout(() => stopAdjusting(root), 900);
    }
  };
  if (!adjustingBound) {
    adjustingBound = true;
    // The pointer may be released anywhere, even outside the window.
    for (const type of ["pointerup", "pointercancel", "blur"]) window.addEventListener(type, () => stopAdjusting(root));
  }

  root.onsubmit = (e) => {
    if (!(e.target instanceof HTMLFormElement) || e.target.dataset.form !== "email") return;
    e.preventDefault();
    void submitEmail(actions);
  };
  root.oninput = (e) => {
    const el = e.target as HTMLInputElement;
    if (el.dataset.field === "email" || el.dataset.field === "password") emailForm[el.dataset.field] = el.value;
    if (el.dataset.act === "range") {
      // The number and the filled part of the track follow the thumb.
      root.querySelector<HTMLOutputElement>(`[data-out="${el.dataset.v}"]`)!.value = el.value;
      el.style.setProperty("--p", `${(Number(el.value) / Number(el.max)) * 100}%`);
    }
    if (el.dataset.act === "range") actions.preview({ [el.dataset.v!]: Number(el.value) } as Partial<Settings>);
  };
}
