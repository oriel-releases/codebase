import { poolTopics } from "../lib/photo";
import type { Profile } from "../lib/account";
import type { Change, Mode, Settings } from "../lib/settings";
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
}

export type Page = "wallpaper" | "appearance" | "account" | "help";

/** Outline icons (24px grid, 2px stroke). */
const icons: Record<Page, string> = {
  wallpaper: '<rect width="18" height="18" x="3" y="3" rx="2"/><circle cx="9" cy="9" r="2"/><path d="m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21"/>',
  appearance:
    '<circle cx="13.5" cy="6.5" r=".5" fill="currentColor"/><circle cx="17.5" cy="10.5" r=".5" fill="currentColor"/><circle cx="8.5" cy="7.5" r=".5" fill="currentColor"/><circle cx="6.5" cy="12.5" r=".5" fill="currentColor"/><path d="M12 2C6.5 2 2 6.5 2 12s4.5 10 10 10c.926 0 1.648-.746 1.648-1.688 0-.437-.18-.835-.437-1.125-.29-.289-.438-.652-.438-1.125a1.64 1.64 0 0 1 1.668-1.668h1.996c3.051 0 5.555-2.503 5.555-5.554C21.965 6.012 17.461 2 12 2z"/>',
  account: '<circle cx="12" cy="12" r="10"/><circle cx="12" cy="10" r="3"/><path d="M7 20.662V19a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v1.662"/>',
  help: '<circle cx="12" cy="12" r="10"/><path d="m4.93 4.93 4.24 4.24M14.83 9.17l4.24-4.24M14.83 14.83l4.24 4.24M9.17 14.83l-4.24 4.24"/><circle cx="12" cy="12" r="4"/>',
};
const menu: [Page, string][] = [
  ["wallpaper", "Wallpaper"],
  ["appearance", "Appearance"],
  ["help", "Help"],
];
const titles: Record<Page, string> = { wallpaper: "Wallpaper", appearance: "Appearance", account: "Account", help: "Help" };
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

let page: Page = "wallpaper";
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

const escapes: Record<string, string> = { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" };
const esc = (s: string) => s.replace(/[&<>"']/g, (c) => escapes[c]);
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
const row = (title: string, _hint: string, control: string) => `<div class="card"><div class="min-w-0"><div class="t">${title}</div></div>${control}</div>`;

function toggle(c: Settings, name: "clock" | "hour12" | "greeting" | "quote" | "search" | "photoInfo", title: string, hint = ""): string {
  return row(title, hint, `<label class="switch"><input type="checkbox" data-act="toggle" data-v="${name}" ${c[name] ? "checked" : ""} aria-label="${esc(title)}" /><span></span></label>`);
}

function wallpaperPage(s: PanelState): string {
  const { settings: c, profile: p } = s;
  const desktopOn = c.mode === "desktop" && !!p;
  const status = !desktopOn
    ? ""
    : s.desktop
      ? `<div class="status"><span class="tile-ico xs">${ico("monitor")}</span><div class="t min-w-0 flex-1 truncate">Last changed on your ${device(s.desktop.source)}</div><span class="pill"><i class="live"></i>Live</span></div>`
      : `<div class="status"><span class="tile-ico xs">${ico("monitor")}</span><div class="t min-w-0 flex-1 truncate">Waiting for your other devices</div><a class="pill warn" href="https://oriel.akbarjon.uz/download" target="_blank" rel="noopener">Get Oriel</a></div>`;
  const source = `<div class="card stack">
      ${seg("source", "Wallpaper source", [{ value: "desktop", text: "Same as my devices", disabled: !p, title: p ? "" : "Connect your account first" }, { value: "own", text: "Its own" }], desktopOn ? "desktop" : "own", "mode")}
      ${status}
    </div>`;
  if (desktopOn) return source;
  return `${source}
    <div class="group-title">Topics</div>
    <div class="card stack"><div class="flex flex-wrap gap-2" role="group" aria-label="Topics">
      ${poolTopics.map((t) => `<button class="chip" aria-pressed="${c.topics.includes(t)}" data-act="topic" data-v="${esc(t)}"><span class="emoji" aria-hidden="true">${emoji[t] ?? "🖼️"}</span>${esc(label(t))}</button>`).join("")}
    </div></div>
    <div class="group-title">Schedule</div>
    ${row("New photo", "How often the photo changes.", seg("change", "How often the photo changes", changeOptions.map(([value, text]) => ({ value, text })), c.change, "change"))}`;
}

function appearancePage(c: Settings): string {
  const slider = (name: "dim" | "blur", title: string, _hint: string, max: number) =>
    row(title, "", `<input type="range" min="0" max="${max}" value="${c[name]}" data-act="range" data-v="${name}" aria-label="${title}" />`);
  return `
    <div class="group-title" style="margin-top:0">New tab</div>
    ${toggle(c, "clock", "Clock")}
    <div class="fold ${c.clock ? "open" : ""}" ${c.clock ? "" : "inert"}><div class="inner">${toggle(c, "hour12", "12-hour clock")}</div></div>
    ${toggle(c, "greeting", "Greeting", "Good morning, afternoon or evening, with your first name once connected.")}
    ${toggle(c, "quote", "Quote", "A new short quote each day.")}
    ${toggle(c, "search", "Search bar", "Uses your browser's default search engine.")}
    ${toggle(c, "photoInfo", "Photo info button", "")}
    <div class="group-title">Photo</div>
    ${slider("dim", "Dim", "Darkens the photo so text stays easy to read.", 70)}
    ${slider("blur", "Blur", "Softens the photo behind everything.", 24)}`;
}

const iconPaths = {
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
        <div class="acct-banner" aria-hidden="true">
          ${s.banner ? `<img src="${esc(s.banner)}" alt="" />` : ""}
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

function helpPage(): string {
  return `
    <div class="card">
      <div class="flex min-w-0 items-center gap-3">
        <span class="tg" aria-hidden="true"><svg viewBox="0 0 24 24"><path fill="#2AABEE" d="M11.944 0A12 12 0 0 0 0 12a12 12 0 0 0 12 12 12 12 0 0 0 12-12A12 12 0 0 0 12 0a12 12 0 0 0-.056 0zm4.962 7.224c.1-.002.321.023.465.14a.506.506 0 0 1 .171.325c.016.093.036.306.02.472-.18 1.898-.962 6.502-1.36 8.627-.168.9-.499 1.201-.82 1.23-.696.065-1.225-.46-1.9-.902-1.056-.693-1.653-1.124-2.678-1.8-1.185-.78-.417-1.21.258-1.91.177-.184 3.247-2.977 3.307-3.23.007-.032.014-.15-.056-.212s-.174-.041-.249-.024c-.106.024-1.793 1.14-5.061 3.345-.48.33-.913.49-1.302.48-.428-.008-1.252-.241-1.865-.44-.752-.245-1.349-.374-1.297-.789.027-.216.325-.437.893-.663 3.498-1.524 5.83-2.529 6.998-3.014 3.332-1.386 4.025-1.627 4.476-1.635z"/></svg></span>
        <div class="min-w-0"><div class="t">Contact support</div></div>
      </div>
      <a class="btn btn-primary" href="${supportUrl}" target="_blank" rel="noopener">Open Telegram</a>
    </div>
    <p class="foot">Oriel ${esc(chrome.runtime.getManifest().version)} · <a href="https://oriel.akbarjon.uz" target="_blank" rel="noopener">Website</a> · <a href="https://oriel.akbarjon.uz/privacy" target="_blank" rel="noopener">Privacy</a> · Photos from <a href="https://unsplash.com/?utm_source=oriel&utm_medium=referral" target="_blank" rel="noopener">Unsplash</a></p>`;
}

function avatar(p: Profile, size: string): string {
  return p.photo
    ? `<img src="${esc(p.photo)}" alt="" referrerpolicy="no-referrer" class="${size} shrink-0 rounded-full object-cover" />`
    : `<div class="${size} grid shrink-0 place-items-center rounded-full bg-white/15">${esc((p.name || p.email || "?")[0].toUpperCase())}</div>`;
}

function html(s: PanelState): string {
  const body = {
    wallpaper: () => wallpaperPage(s),
    appearance: () => appearancePage(s.settings),
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
  if (from.hasAttribute("data-keep")) return; // owned by script (the menu marker)
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
  bind(root, state, actions);
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
  if (!isRange(e.target)) return;
  root.dataset.adjusting = e.target.dataset.v;
  document.body.classList.add("adjusting");
}

function stopAdjusting(root: HTMLElement): void {
  clearTimeout(adjustingTimer);
  delete root.dataset.adjusting;
  document.body.classList.remove("adjusting");
}

function bind(root: HTMLElement, state: PanelState, actions: PanelActions): void {
  root.onclick = (e) => {
    if ((e.target as HTMLElement).classList.contains("confirm-layer")) {
      confirmingDisconnect = false;
      return renderPanel(root, state, actions);
    }
    const el = (e.target as HTMLElement).closest<HTMLElement>("[data-act]");
    if (!el || el instanceof HTMLInputElement) return;
    const v = el.dataset.v ?? "";
    switch (el.dataset.act) {
      case "nav":
        page = v as Page;
        confirmingDisconnect = false;
        return renderPanel(root, state, actions);
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
      case "mode":
        return actions.change({ mode: v as Mode });
      case "change":
        return actions.change({ change: v as Change });
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
    if (el.dataset.act === "range") actions.preview({ [el.dataset.v!]: Number(el.value) } as Partial<Settings>);
  };
}
