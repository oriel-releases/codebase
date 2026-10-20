import "./style.css";
import { imageUrl, pick, placeholderUrl, slotKey, withPoolAccent, type Photo } from "../lib/photo";
import { remember, loadSettings, saveSettings, defaults, type Settings } from "../lib/settings";
import { siteUrl } from "../lib/config";
import { escapeHtml } from "../lib/html";
import { randomQuote } from "../data/quotes";
import { applyTone, forget, measure } from "../lib/tone";
import { accentFor } from "../lib/accent";
import { greetingText, timeText } from "../lib/clock";
import { parseSearch } from "../lib/search";
import { openPage, renderPanel, resetQuestions, type PanelActions } from "./panel";
import { createAccountSync } from "./account-sync";
import { createShortcutsRow } from "./shortcuts-row";
import { showTime } from "./clock-view";
import { protectImages } from "../lib/protect";

protectImages();

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
const bg = $("bg");
const lo = $<HTMLImageElement>("lo");
const scrim = $("scrim");
const panel = $("panel");
const backdrop = $("backdrop");
const infoBtn = $("info-btn");
const info = $("info");

let settings = loadSettings();
/** The links under the clock (UI: shortcuts-row.ts). */
const shortcuts = createShortcutsRow($("shortcuts"), $("sc-dialog"), {
  visible: () => settings.shortcuts,
  edited() {
    drawPanel();
    sync.pushExtensionSoon();
  },
  modalOpened() {
    backdrop.classList.add("open");
    document.body.classList.add("modal-open");
  },
  close: () => closePanel(),
  drawn: () => applyTone(settings.dim / 100),
});
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
    shortcuts.reload(); // signing out erases them
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
    if (JSON.stringify(next) === JSON.stringify(settings) && JSON.stringify(items) === JSON.stringify(shortcuts.items)) return;
    settings = next;
    saveSettings(settings, true);
    shortcuts.replace(items);
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

/** A vivid color from the wallpaper tints the page's controls (see accentFor). */
function applyAccent(photo: Photo): void {
  const accent = accentFor(withPoolAccent(photo), followsDesktop(), sync.desktop?.photo.id);
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

/** The time the clock last showed: a later one rolls forward, an earlier one (the computer's clock set back) rolls back. */
let clockAt = 0;

function renderClock(): void {
  const now = new Date();
  showTime($("clock"), settings.clock ? timeText(now, settings.hour12) : "", now.getTime() >= clockAt, settings.clockAnimation);
  clockAt = now.getTime();
  $("greeting").textContent = settings.greeting ? greetingText(now, sync.profile?.name ?? "") : "";
}

let clockTimer = 0;

/** Draws the clock now and again right at the next minute. */
function scheduleClock(): void {
  renderClock();
  clearTimeout(clockTimer);
  clockTimer = window.setTimeout(scheduleClock, 60_000 - (Date.now() % 60_000) + 5);
}
// The browser slows timers in tabs out of sight (up to a minute late): back on screen, the clock catches up at once.
document.addEventListener("visibilitychange", () => document.visibilityState === "visible" && scheduleClock());

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
  shortcuts.render();
  renderClock();
  renderQuote();
  renderSync();
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
  shortcuts.closeEditor();
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
    shortcuts.reload();
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
