import "./style.css";
import { imageUrl, pick, placeholderUrl, slotKey, type Photo } from "../lib/photo";
import { remember, loadSettings, saveSettings, defaults, type Settings } from "../lib/settings";
import { faviconUrl, loadShortcuts, maxShortcuts, normalizeUrl, saveShortcuts, siteIconUrl, titleFor, type Shortcut } from "../lib/shortcuts";
import { siteUrl } from "../lib/config";
import { randomQuote } from "../data/quotes";
import { applyTone, forget, measure } from "../lib/tone";
import { accentFor } from "../lib/accent";
import { greetingText, timeText } from "../lib/clock";
import { parseSearch } from "../lib/search";
import { openPage, renderPanel, resetQuestions, type PanelActions } from "./panel";
import { createAccountSync } from "./account-sync";

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
const bg = $("bg");
const lo = $<HTMLImageElement>("lo");
const scrim = $("scrim");
const panel = $("panel");
const backdrop = $("backdrop");
const infoBtn = $("info-btn");
const info = $("info");

let settings = loadSettings();
let shortcuts: Shortcut[] = loadShortcuts();
let signingOut = false;
let shown: Photo | null = null;
let paintToken = 0;
/** The photo layer on screen right now (a new photo fades in over it, then it is removed). */
let layer: HTMLImageElement | null = null;
/** From this much blur on, the photo is soft anyway, so the blurred placeholder is skipped. */
const softEnough = 6;

/** Merges and saves new settings. */
function updateSettings(patch: Partial<typeof settings>): void {
  settings = { ...settings, ...patch };
  saveSettings(settings);
}

/** The Oriel account: who is linked and what the desktop wallpaper is. Its logic lives in account-sync.ts. */
const sync = createAccountSync({
  settings: () => settings,
  update: updateSettings,
  changed() {
    shortcuts = loadShortcuts(); // signing out erases them
    noticeIncoming();
    renderLayout();
    refreshPhoto();
    drawPanel();
  },
  busyChanged: () => drawPanel(),
  toast: (message) => toast(message),
  applySynced(remote, items) {
    const before = settings.mode;
    // Only settings this build knows, of the right type: an older or newer browser may send others.
    const known = Object.fromEntries(
      Object.entries(remote).filter(([k, v]) => k !== "bump" && k in defaults && typeof v === typeof defaults[k as keyof Settings]),
    ) as Partial<Settings>;
    const next = { ...settings, ...known };
    if (JSON.stringify(next) === JSON.stringify(settings) && JSON.stringify(items) === JSON.stringify(shortcuts)) return;
    settings = next;
    saveSettings(settings, true);
    shortcuts = items;
    saveShortcuts(items, true);
    styleBackground();
    renderLayout();
    drawPanel();
    if (settings.mode !== before) {
      refreshPhoto();
      void sync.syncWatcher();
    }
  },
});

/** Desktop mode needs an account; without one the extension always picks for itself. */
const followsDesktop = () => settings.mode === "desktop" && !!sync.profile;

const escapeHtml = (s: string) => s.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]!);

// ---------------------------------------------------------------- photo

/**
 * "Every tab": the photo this tab picked. Asked again (the account loading, a synced setting), the tab keeps showing
 * it instead of picking another random one, which made the photo change twice as a tab opened.
 */
let tabPhoto: Photo | null = null;

function currentPhoto(): Photo {
  if (followsDesktop() && sync.desktop) return sync.desktop.photo;
  // One photo per slot: changing topics later doesn't swap the photo you are looking at; it applies from the next slot.
  const key = slotKey(settings.change, settings.bump);
  if (!key) return (tabPhoto ??= pick(settings.topics, settings.change, settings.bump));
  const kept = remember<{ key: string; photo: Photo }>("slot");
  if (kept?.key === key) return kept.photo;
  const photo = pick(settings.topics, settings.change, settings.bump);
  remember("slot", { key, photo });
  return photo;
}

/** The desktop app's accent color for the wallpaper (a vivid color from the photo) tints the page's controls. Own photos keep the default. */
function applyAccent(photo: Photo): void {
  const accent = accentFor(photo, followsDesktop(), sync.desktop?.photo.id);
  const root = document.documentElement;
  if (accent) root.style.setProperty("--accent", accent);
  else root.style.removeProperty("--accent");
  document.querySelector<HTMLMetaElement>('meta[name="theme-color"]')?.setAttribute("content", accent ?? "#000000");
}

// ---------------------------------------------------------------- sync status

/** The photo this tab is publishing right now: its own echo from Firestore isn't "received". */
let publishing: string | null = null;
let lastDesktopId: string | null | undefined;
let statusTimer: number | undefined;
/** Called once the photo now on screen has loaded (for "Receiving…" → "From your phone"). */
let onPainted: (() => void) | null = null;

/** Shows (or, with null, hides) the sync pill. The words slide: old ones up and out, new ones in from below. */
function setStatus(text: string | null, done = false): void {
  const el = $("status");
  clearTimeout(statusTimer);
  if (!text) {
    el.classList.remove("show");
    return;
  }
  el.classList.toggle("done", done);
  const words = el.querySelector(".words")!;
  const current = words.querySelector("span:not(.leave)");
  if (current?.textContent !== text) {
    const next = document.createElement("span");
    next.textContent = text;
    next.className = el.classList.contains("show") ? "enter" : "";
    words.appendChild(next);
    if (current) {
      current.classList.add("leave");
      window.setTimeout(() => current.remove(), 320);
    }
    requestAnimationFrame(() => requestAnimationFrame(() => next.classList.remove("enter")));
  }
  el.classList.add("show");
  if (done) statusTimer = window.setTimeout(() => el.classList.remove("show"), 2200);
}

const deviceName = (source: string) =>
  source === "android" ? "your phone" : source === "windows" || source === "linux" ? "your PC" : source === "extension" ? "another browser" : "your other device";

/** Another device changed the wallpaper while this tab is open: "Receiving…", then where it came from. */
function noticeIncoming(): void {
  const current = sync.desktop;
  const id = followsDesktop() ? current?.photo.id ?? null : null;
  const before = lastDesktopId;
  lastDesktopId = id;
  if (before === undefined || !id || !current || id === before) return; // first look, or nothing new
  if (id === publishing) return; // our own photo coming back
  setStatus("Receiving…");
  onPainted = () => setStatus(`From ${deviceName(current.source)}`, true);
}

/** The shuffle button while following the account: a new photo here, then on every device. */
let changing = false;

/** A photo (a new one, or the one given) on every device of the account. */
async function shuffleEverywhere(chosen?: Photo): Promise<void> {
  const photo = chosen ?? pick(settings.topics, "tab", 0);
  publishing = photo.id;
  setStatus("Downloading…");
  onPainted = () => setStatus("Syncing…");
  showPhoto(photo);
  try {
    await sync.publish(photo);
    lastDesktopId = photo.id;
    setStatus("Synced to your devices", true);
  } catch {
    setStatus(null);
    toast("Couldn't sync this photo. Check your connection.");
  } finally {
    onPainted = null;
  }
}

function showPhoto(photo: Photo): void {
  const same = shown?.id === photo.id && shown.url === photo.url;
  shown = photo;
  applyAccent(photo);
  // The toolbar popup draws its settings over the same photo and accent (src/popup/popup.ts).
  remember("shown", { photo, accent: document.documentElement.style.getPropertyValue("--accent") });
  renderCredit();
  renderSync();
  if (panel.classList.contains("open")) drawPanel(); // Home shows the photo on screen
  if (same) return;

  const token = ++paintToken;
  const dpr = window.devicePixelRatio || 1;
  const scale = Math.min(1, 3200 / (window.innerWidth * dpr));
  const url = imageUrl(photo, window.innerWidth * dpr * scale, window.innerHeight * dpr * scale);

  if (photo.color) bg.style.backgroundColor = photo.color;
  // The blurred sliver is only for the very first paint, and only when the photo itself isn't blurred by the setting.
  // Changing photos never goes through it: the old photo stays until the new one is ready, then they cross-fade.
  if (!layer && settings.blur < softEnough) {
    lo.src = placeholderUrl(photo);
    lo.onload = () => token === paintToken && !layer && (lo.style.opacity = "1");
  }

  // Loaded as a CORS image so its brightness can be measured for the text colours; if that's refused, show it anyway.
  loadFull(url, true)
    .catch(() => {
      forget();
      return loadFull(url, false);
    })
    .then((img) => {
      if (token !== paintToken || !img) return;
      fadeIn(img);
      const painted = onPainted;
      onPainted = null;
      painted?.();
      if (img.crossOrigin) measure(img);
      applyTone(settings.dim / 100);
    })
    .catch(() => {}); // offline: the blurred sliver stays
}

function blurStyle(el: HTMLElement): void {
  el.style.filter = settings.blur ? `blur(${settings.blur}px)` : "";
  el.style.transform = settings.blur ? `scale(${1 + settings.blur / 200})` : "";
}

/** Puts the loaded photo on a new layer above the current one and cross-fades; the old layer goes once it is covered. */
function fadeIn(img: HTMLImageElement): void {
  const next = document.createElement("img");
  next.className = "photo";
  next.alt = "";
  if (img.crossOrigin) next.crossOrigin = img.crossOrigin;
  next.src = img.src;
  blurStyle(next);
  bg.appendChild(next);
  const old = bg.querySelectorAll<HTMLImageElement>("img.photo");
  layer = next;
  requestAnimationFrame(() => requestAnimationFrame(() => next.classList.add("show")));
  lo.style.opacity = "0";
  window.setTimeout(() => old.forEach((el) => el !== next && el.remove()), 1300);
}

function loadFull(url: string, cors: boolean): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image();
    if (cors) img.crossOrigin = "anonymous";
    img.onload = () => resolve(img);
    img.onerror = reject;
    img.src = url;
  });
}

function styleBackground(): void {
  const dim = settings.dim / 100;
  scrim.style.backgroundImage = "radial-gradient(ellipse at center, rgb(0 0 0 / 0) 35%, rgb(0 0 0 / 0.34) 100%)";
  scrim.style.backgroundColor = `rgb(0 0 0 / ${dim})`;
  bg.querySelectorAll<HTMLElement>("img.photo").forEach(blurStyle);
  applyTone(dim);
}

/** Bottom left: the "i" button, and the photo credit it opens. */
function renderCredit(): void {
  infoBtn.hidden = !settings.photoInfo || !shown?.photographer;
  if (!shown?.photographer) return;
  const p = shown;
  info.innerHTML = `
    <button class="icon-btn close-x" style="color:rgb(255 255 255/.8)" data-info-close aria-label="Close"><svg viewBox="0 0 24 24" class="size-5" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M6 6l12 12M18 6 6 18"/></svg></button>
    <h2>Photo</h2>
    ${p.description ? `<p>${escapeHtml(p.description.charAt(0).toUpperCase() + p.description.slice(1))}.</p>` : ""}
    <p>Photo by <a href="${escapeHtml(p.photographerUrl)}" target="_blank" rel="noopener">${escapeHtml(p.photographer)}</a> on <a href="${escapeHtml(p.pageUrl)}" target="_blank" rel="noopener">Unsplash</a></p>
    <a class="btn btn-primary" href="${escapeHtml(p.pageUrl)}" target="_blank" rel="noopener">View on Unsplash</a>`;
  applyTone(settings.dim / 100);
}

function openInfo(): void {
  info.classList.add("open");
  info.removeAttribute("inert");
  backdrop.classList.add("open");
  document.body.classList.add("modal-open");
}

function closeInfo(): void {
  info.classList.remove("open");
  info.setAttribute("inert", "");
}
infoBtn.addEventListener("click", openInfo);
info.addEventListener("click", (e) => {
  if ((e.target as HTMLElement).closest("[data-info-close]")) closePanel();
});

/** Top right: your avatar once an account is connected. */
function renderSync(): void {
  const avatar = $<HTMLImageElement>("avatar");
  if (settings.avatar && sync.profile?.photo) {
    avatar.src = sync.profile.photo;
    avatar.hidden = false;
  } else {
    avatar.hidden = true;
  }
}

function refreshPhoto(): void {
  showPhoto(currentPhoto());
}

// ---------------------------------------------------------------- widgets

function renderClock(): void {
  const now = new Date();
  $("clock").textContent = settings.clock ? timeText(now, settings.hour12) : "";
  $("greeting").textContent = settings.greeting ? greetingText(now, sync.profile?.name ?? "") : "";
}

function scheduleClock(): void {
  renderClock();
  setTimeout(scheduleClock, 60_000 - (Date.now() % 60_000) + 50);
}

// A new line every time a tab opens (never the same one twice in a row); settings changes keep the one on screen.
const line = randomQuote(remember<number>("quote-index") ?? undefined);
remember("quote-index", line.index);

/** Clicking the quote copies it. */
async function copyQuote(): Promise<void> {
  const text = `\u201c${line.text}\u201d`;
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    // Clipboard API refused (rare in extension pages): fall back to the old way.
    const area = document.createElement("textarea");
    area.value = text;
    document.body.appendChild(area);
    area.select();
    document.execCommand("copy");
    area.remove();
  }
  toast("Copied to clipboard");
}
$("quote").addEventListener("click", () => void copyQuote());
$("quote").addEventListener("keydown", (e) => {
  if (e.key === "Enter" || e.key === " ") {
    e.preventDefault();
    void copyQuote();
  }
});

function renderQuote(): void {
  $("quote").textContent = settings.quote ? `“${line.text}”` : "";
  applyTone(settings.dim / 100);
}

function renderLayout(): void {
  $("search").style.display = settings.search ? "flex" : "none";
  infoBtn.hidden = !settings.photoInfo || !shown?.photographer;
  if (!settings.photoInfo) closeInfo();
  // Following the account, the shuffle changes the wallpaper on every device; otherwise only here.
  $("shuffle").hidden = !settings.shuffle;
  $("open-panel").hidden = !settings.settingsButton;
  $("shuffle").title = followsDesktop() ? "Another photo, on all your devices" : "Another photo";
  renderClock();
  renderQuote();
  renderSync();
  renderShortcuts();
}

// Search: the browser's own default engine where the extension API allows it.
$("search").addEventListener("submit", (e) => {
  e.preventDefault();
  const target = parseSearch($<HTMLInputElement>("q").value);
  if (!target) return;
  if (target.kind === "address") {
    location.href = target.url;
    return;
  }
  const text = target.text;
  const firefox = (globalThis as unknown as { browser?: { search?: { search(o: object): Promise<void> } } }).browser;
  if (chrome.search?.query) chrome.search.query({ text, disposition: "CURRENT_TAB" });
  else if (firefox?.search?.search) void firefox.search.search({ query: text, disposition: "CURRENT_TAB" });
  else location.href = `https://www.google.com/search?q=${encodeURIComponent(text)}`;
});

// Typing anywhere starts a search (when the search bar is on).
document.addEventListener("keydown", (e) => {
  if (e.key === "Escape") return closePanel();
  const target = e.target as HTMLElement;
  if (e.ctrlKey || e.metaKey || e.altKey || e.key.length !== 1 || target.closest("input, textarea") || !settings.search) return;
  if (panel.classList.contains("open")) return;
  $<HTMLInputElement>("q").focus();
});

let resizeTimer: number | undefined;
window.addEventListener("resize", () => {
  clearTimeout(resizeTimer);
  resizeTimer = window.setTimeout(() => {
    if (shown && layer?.complete && layer.crossOrigin) measure(layer);
    applyTone(settings.dim / 100);
  }, 200);
});

// ---------------------------------------------------------------- settings dialog

function openPanel(): void {
  // Draw first, while it is still hidden, so the menu marker and page are already in place when the dialog appears.
  drawPanel();
  panel.classList.add("open");
  document.body.classList.add("modal-open");
  panel.removeAttribute("inert");
  backdrop.classList.add("open");
}

function closePanel(): void {
  closeInfo();
  closeShortcutEditor();
  panel.classList.remove("open");
  document.body.classList.remove("modal-open");
  // Once the panel has finished fading out (not while it does), a pending "Sign out?" question is dropped too.
  window.setTimeout(() => {
    if (panel.classList.contains("open")) return; // reopened in the meantime
    resetQuestions();
    drawPanel();
  }, 320);
  panel.setAttribute("inert", "");
  backdrop.classList.remove("open");
}

let toneFrame = 0;

const actions: PanelActions = {
  // Dragging a slider: only the photo's dim and blur change, at most once per frame. Saving, redrawing the dialog and
  // re-measuring every text colour on each tick is what made the two sliders feel different from each other.
  preview(patch) {
    settings = { ...settings, ...patch };
    scrim.style.backgroundColor = `rgb(0 0 0 / ${settings.dim / 100})`;
    bg.querySelectorAll<HTMLElement>("img.photo").forEach(blurStyle);
    if (!toneFrame) {
      toneFrame = requestAnimationFrame(() => {
        toneFrame = 0;
        applyTone(settings.dim / 100);
      });
    }
  },
  change(patch) {
    const modeChanged = patch.mode !== undefined && patch.mode !== settings.mode;
    updateSettings(patch);
    sync.pushExtensionSoon();
    renderLayout();
    styleBackground();
    // Topics and frequency apply to the next photo; only switching source changes what is on screen now.
    if (modeChanged) {
      refreshPhoto();
      void sync.syncWatcher();
    }
    drawPanel();
  },
  connect() {
    window.open(`${siteUrl()}/login?for=extension`, "_blank");
    sync.setBusy(true);
    toast("Finish signing in on the Oriel tab");
    // If the sign-in tab is closed without finishing, make the button usable again.
    window.setTimeout(() => {
      if (!sync.profile && sync.busy) sync.setBusy(false);
    }, 3 * 60_000);
  },
  signInEmail: (email, password) => sync.signInWithEmail(email, password),
  resetPassword: (email) => sync.resetPassword(email),
  openApp() {
    toast("Opening Oriel…");
  },
  changeNow: () => void changePhoto(),
  setPhoto: (photo) => void changePhoto(photo),
  async disconnect() {
    // The button shows "Signing out…" (disabled) until it is done; onAuthStateChanged then redraws the page as signed out.
    signingOut = true;
    drawPanel();
    try {
      await sync.signOut();
    } catch {
      toast("Couldn't sign out. Please try again.");
    } finally {
      signingOut = false;
      drawPanel();
    }
  },
};

function drawPanel(): void {
  renderPanel(
    panel,
    {
      settings,
      profile: sync.profile,
      busy: sync.busy,
      signingOut,
      desktop: sync.desktop,
      banner: shown ? imageUrl(shown, 560, 224, 45) : null,
      photo: shown,
      changing,
    },
    actions,
  );
}

$("open-panel").addEventListener("click", openPanel);
// Your profile picture opens settings on the Account page.
const openAccount = () => {
  openPage("account");
  openPanel();
};
// The toolbar icon: on this page it opens the dialog here rather than the popup (see src/background.ts).
void chrome.tabs.getCurrent().then(async (tab) => {
  if (tab?.id !== undefined) await chrome.action.setPopup({ tabId: tab.id, popup: "" });
});
chrome.runtime.onMessage.addListener((message, _sender, reply) => {
  if (message?.type !== "oriel:open-settings") return;
  if (panel.classList.contains("open")) closePanel();
  else openPanel();
  reply(true);
});
$("avatar").addEventListener("click", openAccount);
$("avatar").addEventListener("keydown", (e) => {
  if (e.key === "Enter" || e.key === " ") {
    e.preventDefault();
    openAccount();
  }
});
backdrop.addEventListener("click", closePanel);
document.addEventListener("oriel:close-panel", closePanel);
document.addEventListener("oriel:redraw-panel", () => drawPanel());
$("shuffle").addEventListener("click", (e) => {
  const button = e.currentTarget as HTMLElement;
  button.classList.remove("spin");
  void button.offsetWidth; // restart the animation
  button.classList.add("spin");
  void changePhoto();
});

/**
 * Another photo (Shuffle, Home's Change now), or the one given (Home's Wallpaper of the day). Following the account:
 * on every device. Otherwise here: pinned to this time slot, and every open tab of this browser switches to it.
 */
async function changePhoto(chosen?: Photo): Promise<void> {
  if (changing) return;
  changing = true;
  drawPanel();
  try {
    if (followsDesktop()) return await shuffleEverywhere(chosen);
    let photo = chosen;
    if (!photo) {
      updateSettings({ bump: settings.bump + 1 });
      tabPhoto = null; // Shuffle: a new one, even with "Every tab"
      photo = currentPhoto();
    } else {
      const key = slotKey(settings.change, settings.bump);
      if (key) remember("slot", { key, photo });
      else tabPhoto = photo;
    }
    showPhoto(photo);
    // Tell the other open tabs, so they all switch to this photo (see the storage listener below).
    remember("shared-photo", { photo, at: Date.now() });
  } finally {
    changing = false;
    drawPanel();
  }
}

// Other tabs of this browser: a photo changed here, or a setting did.
window.addEventListener("storage", (e) => {
  if (e.key === "oriel.shared-photo" && e.newValue) {
    const shared = (JSON.parse(e.newValue) as { photo?: Photo } | null)?.photo;
    if (shared && !followsDesktop()) {
      if (!slotKey(settings.change, settings.bump)) tabPhoto = shared; // keep it, rather than going back to this tab's pick
      showPhoto(shared);
    }
  } else if (e.key === "oriel.shortcuts") {
    shortcuts = loadShortcuts();
    renderShortcuts();
    drawPanel();
    sync.pushExtensionSoon(); // changed in another tab; harmless if that tab already sent it
  } else if (e.key === "oriel.settings") {
    sync.pushExtensionSoon(); // e.g. from the toolbar popup, which has no account connection of its own
    const before = settings.mode;
    settings = loadSettings();
    styleBackground();
    renderLayout();
    drawPanel();
    if (settings.mode !== before) {
      refreshPhoto();
      void sync.syncWatcher();
    }
  }
});

let toastTimer: number | undefined;
function toast(message: string): void {
  const el = $("toast");
  el.textContent = message;
  el.classList.add("show");
  clearTimeout(toastTimer);
  toastTimer = window.setTimeout(() => el.classList.remove("show"), 3500);
}

// ---------------------------------------------------------------- account (logic: account-sync.ts)

chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "local" && changes.handoff?.newValue) void sync.consumeHandoff();
});

// ---------------------------------------------------------------- shortcuts

const shortcutsEl = $("shortcuts");
const scDialog = $("sc-dialog");
/** The shortcut being edited, or -1 while adding one. */
let editing = -1;

const icon = (path: string) =>
  `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${path}</svg>`;

/** Shown before "Show more". */
const collapsedCount = 6;

/** What the row shows: it is only rebuilt when this changes. */
const rowKey = () => JSON.stringify([shortcuts, shortcuts.length > collapsedCount && !!remember<boolean>("shortcuts-expanded")]);

/** What the row shows now: it is only rebuilt when this changes. */
let drawnShortcuts = "";

/** Stands in for a site whose icon can't be had (hidden while one is shown). */
const globe = `<span class="sc-globe">${icon('<circle cx="12" cy="12" r="10"/><path d="M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20M2 12h20"/>')}</span>`;

/** Under the clock: a round tile per link, then "Add shortcut" while there's room. */
function renderShortcuts(): void {
  shortcutsEl.hidden = !settings.shortcuts;
  if (!settings.shortcuts) {
    drawnShortcuts = "";
    return;
  }
  const expanded = shortcuts.length > collapsedCount && !!remember<boolean>("shortcuts-expanded");
  const key = rowKey();
  if (key === drawnShortcuts) return; // a setting changed, not the shortcuts: leave the row as it is
  drawnShortcuts = key;
  // More than collapsedCount: the rest (and Add) wait behind "Show more", like Chrome's own new tab.
  const shown = shortcuts.length > collapsedCount && !expanded ? shortcuts.slice(0, collapsedCount) : shortcuts;
  // data-key: the same tile across redraws, so Show more / Show less can move it smoothly (toggleMoreShortcuts).
  const tiles = shown.map((s, i) => {
    // Sites this browser's icon cache didn't know (see the load listener) go straight to the site's own icon.
    // Firefox has no icon cache: the site's own icon directly.
    const favicon = siteIcons.includes(s.url) ? siteIconUrl(s.url) : (faviconUrl(s.url) ?? siteIconUrl(s.url));
    return `<div class="sc" data-key="${i}" data-sc>
        <a class="sc-link" href="${escapeHtml(s.url)}" draggable="false">
          <span class="sc-ico">${favicon ? `<img src="${escapeHtml(favicon)}" alt="" />` : ""}${globe}</span>
          <span class="sc-title">${escapeHtml(s.title)}</span>
        </a>
        <button class="sc-edit" data-edit="${i}" aria-label="Edit ${escapeHtml(s.title)}">${icon('<circle cx="12" cy="5" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="12" cy="19" r="1"/>')}</button>
      </div>`;
  });
  if (shortcuts.length > collapsedCount) {
    tiles.push(
      expanded
        ? `<button class="sc sc-more" data-more data-key="more" aria-expanded="true"><span class="sc-ico">${icon('<path d="M4 14h6v6M20 10h-6V4M14 10l7-7M3 21l7-7"/>')}</span><span class="sc-title">Show less</span></button>`
        : `<button class="sc sc-more" data-more data-key="more" aria-expanded="false"><span class="sc-ico">${icon('<path d="M15 3h6v6M9 21H3v-6M21 3l-7 7M3 21l7-7"/>')}</span><span class="sc-title">Show more</span></button>`,
    );
  }
  if (shortcuts.length < maxShortcuts && (expanded || shortcuts.length <= collapsedCount)) {
    tiles.push(`<button class="sc sc-add" data-add data-key="add"><span class="sc-ico">${icon('<path d="M12 5v14M5 12h14"/>')}</span><span class="sc-title">Add shortcut</span></button>`);
  }
  shortcutsEl.innerHTML = tiles.join("");
  drawnAt = performance.now();
}

/** When the row was last drawn: an icon that arrives right after came from the browser's cache and needs no fade. */
let drawnAt = 0;

// ---- Drag a shortcut to reorder: the tile lifts and follows the pointer, the others glide aside, and on release it
// settles into its new place. Our own drag (pointer events), not the browser's, which only drags a faded picture.

/** Moves tiles in the page and lets every tile that moved (but the one held) glide from where it was (FLIP). */
function glide(change: () => void, held?: HTMLElement): void {
  const tiles = [...shortcutsEl.querySelectorAll<HTMLElement>(".sc")].filter((t) => t !== held);
  const before = new Map(tiles.map((t) => [t, t.getBoundingClientRect()]));
  change();
  for (const t of tiles) {
    const a = before.get(t)!;
    const b = t.getBoundingClientRect();
    const dx = a.left - b.left;
    const dy = a.top - b.top;
    if (dx || dy) t.animate([{ translate: `${dx}px ${dy}px` }, { translate: "0 0" }], { duration: 240, easing: "cubic-bezier(0.2, 0.8, 0.2, 1)" });
  }
}

const isShortcutTile = (el: Element | null): el is HTMLElement => !!el && el.matches(".sc[data-sc]");

let press: { tile: HTMLElement; id: number; x: number; y: number; dx: number; dy: number; dragging: boolean } | null = null;
/** Set when a drag ends, so the click the browser fires on release doesn't open the link. */
let justDragged = false;

/** Where the tile sits in the row, ignoring the offset it's drawn at while held. */
function homeOf(tile: HTMLElement): DOMRect {
  const offset = tile.style.translate;
  tile.style.translate = "";
  const box = tile.getBoundingClientRect();
  tile.style.translate = offset;
  return box;
}

shortcutsEl.addEventListener("pointerdown", (e) => {
  if (e.button !== 0 || (e.target as HTMLElement).closest(".sc-edit")) return;
  const tile = (e.target as HTMLElement).closest(".sc");
  if (!isShortcutTile(tile) || shortcutsEl.querySelectorAll(".sc[data-sc]").length < 2) return; // nothing to reorder
  const box = tile.getBoundingClientRect();
  press = { tile, id: e.pointerId, x: e.clientX, y: e.clientY, dx: e.clientX - box.left, dy: e.clientY - box.top, dragging: false };
});

shortcutsEl.addEventListener("pointermove", (e) => {
  if (!press || e.pointerId !== press.id) return;
  const { tile } = press;
  if (!press.dragging) {
    if (Math.hypot(e.clientX - press.x, e.clientY - press.y) < 6) return; // a click, so far
    press.dragging = true;
    tile.setPointerCapture(e.pointerId);
    tile.classList.add("held");
    shortcutsEl.classList.add("sorting");
  }
  // The tile under the pointer (the held one is skipped): move the held tile before or after it.
  const over = document
    .elementsFromPoint(e.clientX, e.clientY)
    .map((el) => el.closest(".sc"))
    .find((el) => isShortcutTile(el) && el !== tile) as HTMLElement | undefined;
  if (over) {
    const box = over.getBoundingClientRect();
    const target = e.clientX > box.left + box.width / 2 ? over.nextElementSibling : over;
    if (target !== tile && target !== tile.nextElementSibling) glide(() => shortcutsEl.insertBefore(tile, target), tile);
  }
  const home = homeOf(tile);
  tile.style.translate = `${e.clientX - press.dx - home.left}px ${e.clientY - press.dy - home.top}px`;
});

function endPress(e: PointerEvent): void {
  if (!press || e.pointerId !== press.id) return;
  const { tile, dragging } = press;
  press = null;
  if (!dragging) return;
  justDragged = true;
  setTimeout(() => (justDragged = false), 0);
  // Settle: from where it was let go into its place in the row.
  const from = tile.style.translate || "0px 0px";
  tile.style.translate = "";
  shortcutsEl.classList.remove("sorting");
  tile.classList.replace("held", "settling");
  tile.animate([{ translate: from, scale: "1.08" }, { translate: "0 0", scale: "1" }], { duration: 260, easing: "cubic-bezier(0.2, 0.8, 0.2, 1)" }).finished.finally(() => {
    tile.classList.remove("settling");
    // The order on screen is the new order; shortcuts hidden behind "Show more" keep their places after these.
    const order = [...shortcutsEl.querySelectorAll<HTMLElement>(".sc[data-sc]")].map((t) => Number(t.dataset.key));
    if (order.every((key, i) => key === i)) return;
    setShortcuts([...order.map((key) => shortcuts[key]), ...shortcuts.filter((_, i) => !order.includes(i))], true);
  });
}
shortcutsEl.addEventListener("pointerup", endPress);
shortcutsEl.addEventListener("pointercancel", endPress);
// The release after a drag isn't a click on the link.
shortcutsEl.addEventListener("click", (e) => justDragged && e.preventDefault(), true);

/**
 * Shortcuts whose icon comes from the site itself: the browser's icon cache only knows sites visited in this browser
 * (a fresh Edge has never opened Gmail) and hands back its generic page icon for the rest.
 */
let siteIcons: string[] = remember<string[]>("site-icons") ?? [];

/** A small fingerprint of an icon's pixels (icons from the browser's cache can be read, they're the extension's own). */
function iconPrint(img: HTMLImageElement): string {
  const canvas = document.createElement("canvas");
  canvas.width = canvas.height = 16;
  const ctx = canvas.getContext("2d", { willReadFrequently: true })!;
  ctx.drawImage(img, 0, 0, 16, 16);
  return canvas.toDataURL();
}

/**
 * A square picture (filled to its corners, like a profile photo) shows as a circle, as Chrome already hands some back;
 * a logo with clear corners (Gmail's M) keeps its shape. Only icons from the browser's cache can be read; a site's own
 * icon (another origin) stays as it is.
 */
function filledCorners(img: HTMLImageElement): boolean {
  try {
    const canvas = document.createElement("canvas");
    canvas.width = canvas.height = 16;
    const ctx = canvas.getContext("2d", { willReadFrequently: true })!;
    ctx.drawImage(img, 0, 0, 16, 16);
    return [[0, 0], [15, 0], [0, 15], [15, 15]].every(([x, y]) => ctx.getImageData(x, y, 1, 1).data[3] > 200);
  } catch {
    return false;
  }
}

/** What the browser's cache returns for a site it doesn't know: its generic page icon. */
let genericIcon: Promise<string> | null = null;
function genericPrint(): Promise<string> {
  return (genericIcon ??= new Promise((done) => {
    const img = new Image();
    img.onload = () => done(iconPrint(img));
    img.onerror = () => done("");
    img.src = faviconUrl("https://oriel-unknown-site.invalid/") ?? "";
  }));
}

// A favicon fades in once checked and loaded (one the browser already had, most tabs, is simply there). The browser's
// generic page icon is swapped for the site's own icon; an icon that fails to load is removed, showing a globe.
shortcutsEl.addEventListener(
  "load",
  (e) => {
    const img = e.target as HTMLImageElement;
    const quick = performance.now() - drawnAt < 80;
    const show = () => {
      if (quick) img.classList.add("instant");
      if (filledCorners(img)) img.classList.add("round");
      img.classList.add("loaded");
    };
    const url = img.closest(".sc")?.querySelector<HTMLAnchorElement>(".sc-link")?.getAttribute("href");
    if (!url || !img.src.includes("/_favicon/")) return show();
    void genericPrint().then((generic) => {
      if (!generic || iconPrint(img) !== generic) return show();
      siteIcons = [...siteIcons.filter((u) => u !== url), url];
      remember("site-icons", siteIcons);
      img.src = siteIconUrl(url) ?? ""; // loads again: this listener shows it, or "error" removes it
    });
  },
  true,
);
shortcutsEl.addEventListener("error", (e) => (e.target as HTMLElement).remove(), true);
shortcutsEl.addEventListener("click", (e) => {
  const target = e.target as HTMLElement;
  const edit = target.closest<HTMLElement>("[data-edit]");
  if (edit) {
    e.preventDefault();
    openShortcutEditor(Number(edit.dataset.edit));
  } else if (target.closest("[data-add]")) openShortcutEditor(-1);
  else if (target.closest("[data-more]")) void toggleMoreShortcuts();
});

const ease = "cubic-bezier(0.2, 0.8, 0.2, 1)";
let toggling = false;

/**
 * Show more / Show less (this browser only, not synced), animated: the tiles already there glide to their new places,
 * the row grows or shrinks smoothly (so the clock above moves with it instead of jumping), and the extra tiles fade in
 * one after another, sharpening from a soft blur. Closing fades the extra tiles out first, then the row closes.
 */
async function toggleMoreShortcuts(): Promise<void> {
  if (toggling) return;
  toggling = true;
  const opening = !remember<boolean>("shortcuts-expanded");
  const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
  try {
    if (!opening && !reduced) {
      const leaving = [...shortcutsEl.querySelectorAll<HTMLElement>("[data-key]")].filter(
        (el) => el.dataset.key === "add" || Number(el.dataset.key) >= collapsedCount,
      );
      await Promise.all(
        leaving.map((el, i) =>
          el.animate([{ opacity: 1, scale: 1, filter: "blur(0)" }, { opacity: 0, scale: 0.92, filter: "blur(6px)" }], {
            duration: 180,
            delay: (leaving.length - 1 - i) * 15,
            easing: "ease-in",
            fill: "forwards",
          }).finished,
        ),
      );
    }
    // FLIP: where everything is now, then redraw, then play each tile from its old place to its new one.
    const before = new Map([...shortcutsEl.querySelectorAll<HTMLElement>("[data-key]")].map((el) => [el.dataset.key!, el.getBoundingClientRect()]));
    const height = shortcutsEl.offsetHeight;
    remember("shortcuts-expanded", opening);
    renderShortcuts();
    if (reduced) return;
    shortcutsEl.animate([{ height: `${height}px` }, { height: `${shortcutsEl.offsetHeight}px` }], { duration: 420, easing: ease });
    let fresh = 0;
    for (const el of shortcutsEl.querySelectorAll<HTMLElement>("[data-key]")) {
      const old = before.get(el.dataset.key!);
      if (old) {
        const now = el.getBoundingClientRect();
        const dx = old.left - now.left;
        const dy = old.top - now.top;
        if (dx || dy) el.animate([{ translate: `${dx}px ${dy}px` }, { translate: "0 0" }], { duration: 420, easing: ease });
      } else {
        el.animate([{ opacity: 0, scale: 0.92, filter: "blur(6px)" }, { opacity: 1, scale: 1, filter: "blur(0)" }], {
          duration: 360,
          delay: 60 + fresh++ * 35,
          easing: ease,
          fill: "backwards",
        });
      }
    }
  } finally {
    toggling = false;
  }
}

function openShortcutEditor(index: number): void {
  editing = index;
  const s = shortcuts[index];
  scDialog.innerHTML = `
    <h2>${s ? "Edit shortcut" : "Add shortcut"}</h2>
    <form class="sc-form" novalidate>
      <input class="field" name="title" placeholder="Name" aria-label="Name" maxlength="40" autocomplete="off" value="${escapeHtml(s?.title ?? "")}" />
      <input class="field" name="url" placeholder="URL" aria-label="URL" maxlength="2000" autocomplete="off" spellcheck="false" value="${escapeHtml(s?.url ?? "")}" />
      <p class="sc-error" role="alert"></p>
      <div class="sc-actions">
        ${s ? `<button type="button" class="btn btn-danger" data-remove>Delete</button>` : ""}
        <span class="flex-1"></span>
        <button type="submit" class="btn btn-primary">${s ? "Save" : "Add"}</button>
      </div>
    </form>`;
  scDialog.classList.add("open");
  scDialog.removeAttribute("inert");
  backdrop.classList.add("open");
  document.body.classList.add("modal-open");
  // The name first, adding or editing, with the cursor at the end of it.
  const name = scDialog.querySelector<HTMLInputElement>('[name="title"]')!;
  name.focus();
  name.setSelectionRange(name.value.length, name.value.length);
}

function closeShortcutEditor(): void {
  scDialog.classList.remove("open");
  scDialog.setAttribute("inert", "");
}

/** `inPlace`: the tiles on screen already show this order (a drag just ended), so they're renumbered, not rebuilt. */
function setShortcuts(items: Shortcut[], inPlace = false): void {
  shortcuts = items;
  saveShortcuts(items);
  if (inPlace) {
    shortcutsEl.querySelectorAll<HTMLElement>(".sc[data-sc]").forEach((tile, i) => {
      tile.dataset.key = String(i);
      tile.querySelector<HTMLElement>("[data-edit]")!.dataset.edit = String(i);
    });
    drawnShortcuts = rowKey();
  }
  renderShortcuts();
  drawPanel();
  sync.pushExtensionSoon();
}

scDialog.addEventListener("submit", (e) => {
  e.preventDefault();
  const form = e.target as HTMLFormElement;
  const url = normalizeUrl((form.elements.namedItem("url") as HTMLInputElement).value);
  if (!url) {
    form.querySelector(".sc-error")!.textContent = "Enter a web address, like example.com";
    return;
  }
  const title = (form.elements.namedItem("title") as HTMLInputElement).value.trim() || titleFor(url);
  const items = [...shortcuts];
  if (editing >= 0) items[editing] = { title, url };
  else items.push({ title, url });
  setShortcuts(items);
  closePanel();
});
scDialog.addEventListener("click", (e) => {
  const target = e.target as HTMLElement;
  if (target.closest("[data-remove]")) {
    setShortcuts(shortcuts.filter((_, i) => i !== editing));
    closePanel();
  }
});

// Back to this tab: upload a change still waiting (made offline, or in the toolbar popup). Nothing is read here: the
// live doorbell already says when another browser changed something.
window.addEventListener("focus", () => void sync.syncExtension());

// ---------------------------------------------------------------- start

// Paint immediately from what's remembered; everything async only refines it.
styleBackground();
renderLayout();
scheduleClock();
refreshPhoto();

chrome.storage.local.get("handoff").then(({ handoff }) => {
  if (sync.profile || handoff) void sync.start().then(() => handoff && sync.consumeHandoff());
});
