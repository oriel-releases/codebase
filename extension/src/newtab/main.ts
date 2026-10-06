import "./style.css";
import { imageUrl, pick, placeholderUrl, slotKey, type Photo } from "../lib/photo";
import { remember, loadSettings, saveSettings } from "../lib/settings";
import { siteUrl } from "../lib/config";
import { randomQuote } from "../data/quotes";
import { applyTone, forget, measure } from "../lib/tone";
import type { DesktopCurrent, Profile } from "../lib/account";
import { openPage, renderPanel, resetQuestions, type PanelActions } from "./panel";

// The Firebase SDK is only loaded when there's an account to talk to.
type Account = typeof import("../lib/account");

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
const bg = $("bg");
const lo = $<HTMLImageElement>("lo");
const scrim = $("scrim");
const panel = $("panel");
const backdrop = $("backdrop");
const infoBtn = $("info-btn");
const info = $("info");

let settings = loadSettings();
let profile: Profile | null = remember<Profile>("profile");
let desktop: DesktopCurrent | null = remember<DesktopCurrent>("current");
let busy = false;
let signingOut = false;
let account: Account | null = null;
let stopWatching: (() => void) | null = null;
let shown: Photo | null = null;
let paintToken = 0;
/** The photo layer on screen right now (a new photo fades in over it, then it is removed). */
let layer: HTMLImageElement | null = null;
/** From this much blur on, the photo is soft anyway, so the blurred placeholder is skipped. */
const softEnough = 6;

/** Desktop mode needs an account; without one the extension always picks for itself. */
const followsDesktop = () => settings.mode === "desktop" && !!profile;

const escapeHtml = (s: string) => s.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]!);

// ---------------------------------------------------------------- photo

function currentPhoto(): Photo {
  if (followsDesktop() && desktop) return desktop.photo;
  // One photo per slot: changing topics later doesn't swap the photo you are looking at; it applies from the next slot.
  const key = slotKey(settings.change, settings.bump);
  if (!key) return pick(settings.topics, settings.change, settings.bump);
  const kept = remember<{ key: string; photo: Photo }>("slot");
  if (kept?.key === key) return kept.photo;
  const photo = pick(settings.topics, settings.change, settings.bump);
  remember("slot", { key, photo });
  return photo;
}

function showPhoto(photo: Photo): void {
  const same = shown?.id === photo.id && shown.url === photo.url;
  shown = photo;
  renderCredit();
  renderSync();
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
  if (profile?.photo) {
    avatar.src = profile.photo;
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
  $("clock").textContent = settings.clock
    ? now.toLocaleTimeString([], { hour: settings.hour12 ? "numeric" : "2-digit", minute: "2-digit", hour12: settings.hour12 }).replace(/\s?[AP]M$/i, "")
    : "";

  const first = (profile?.name || "").split(" ")[0];
  const h = now.getHours();
  const part = h < 5 ? "Good night" : h < 12 ? "Good morning" : h < 18 ? "Good afternoon" : "Good evening";
  $("greeting").textContent = settings.greeting ? (first ? `${part}, ${first}.` : `${part}.`) : "";
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
  $("search").style.display = settings.search ? "" : "none";
  infoBtn.hidden = !settings.photoInfo || !shown?.photographer;
  if (!settings.photoInfo) closeInfo();
  $("shuffle").hidden = followsDesktop();
  renderClock();
  renderQuote();
  renderSync();
}

// Search: the browser's own default engine where the extension API allows it.
$("search").addEventListener("submit", (e) => {
  e.preventDefault();
  const input = $<HTMLInputElement>("q");
  const text = input.value.trim();
  if (!text) return;
  const looksLikeAddress = !/\s/.test(text) && /^(https?:\/\/|[\w-]+(\.[\w-]+)+(\/|$))/i.test(text);
  if (looksLikeAddress) {
    location.href = /^https?:\/\//i.test(text) ? text : `https://${text}`;
    return;
  }
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

const actions: PanelActions = {
  change(patch) {
    const modeChanged = patch.mode !== undefined && patch.mode !== settings.mode;
    settings = { ...settings, ...patch };
    saveSettings(settings);
    renderLayout();
    styleBackground();
    // Topics and frequency apply to the next photo; only switching source changes what is on screen now.
    if (modeChanged) {
      refreshPhoto();
      void syncWatcher();
    }
    drawPanel();
  },
  connect() {
    window.open(`${siteUrl()}/login?for=extension`, "_blank");
    busy = true;
    drawPanel();
    toast("Finish signing in on the Oriel tab");
    // If the sign-in tab is closed without finishing, make the button usable again.
    window.setTimeout(() => {
      if (!profile && busy) {
        busy = false;
        drawPanel();
      }
    }, 3 * 60_000);
  },
  openApp() {
    toast("Opening Oriel…");
  },
  async disconnect() {
    // The button shows "Signing out…" (disabled) until it is done; onAuthStateChanged then redraws the page as signed out.
    signingOut = true;
    drawPanel();
    try {
      await (await loadAccount()).signOutAccount();
    } catch {
      toast("Couldn't sign out. Please try again.");
    } finally {
      signingOut = false;
      drawPanel();
    }
  },
};

function drawPanel(): void {
  renderPanel(panel, { settings, profile, busy, signingOut, desktop, banner: shown ? imageUrl(shown, 560, 224, 45) : null }, actions);
}

$("open-panel").addEventListener("click", openPanel);
// Your profile picture opens settings on the Account page.
const openAccount = () => {
  openPage("account");
  openPanel();
};
$("avatar").addEventListener("click", openAccount);
$("avatar").addEventListener("keydown", (e) => {
  if (e.key === "Enter" || e.key === " ") {
    e.preventDefault();
    openAccount();
  }
});
backdrop.addEventListener("click", closePanel);
document.addEventListener("oriel:close-panel", closePanel);
$("shuffle").addEventListener("click", (e) => {
  const button = e.currentTarget as HTMLElement;
  button.classList.remove("spin");
  void button.offsetWidth; // restart the animation
  button.classList.add("spin");
  settings = { ...settings, bump: settings.bump + 1 };
  saveSettings(settings);
  const photo = currentPhoto();
  showPhoto(photo);
  // Tell the other open tabs, so they all switch to this photo (see the storage listener below).
  remember("shared-photo", { photo, at: Date.now() });
});

// Other tabs of this browser: a photo changed here, or a setting did.
window.addEventListener("storage", (e) => {
  if (e.key === "oriel.shared-photo" && e.newValue) {
    const shared = (JSON.parse(e.newValue) as { photo?: Photo } | null)?.photo;
    if (shared && !followsDesktop()) showPhoto(shared);
  } else if (e.key === "oriel.settings") {
    const before = settings.mode;
    settings = loadSettings();
    styleBackground();
    renderLayout();
    drawPanel();
    if (settings.mode !== before) {
      refreshPhoto();
      void syncWatcher();
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

// ---------------------------------------------------------------- account

async function loadAccount(): Promise<Account> {
  return (account ??= await import("../lib/account"));
}

/** Follows the desktop's wallpaper only while the account is linked and the mode asks for it. */
async function syncWatcher(): Promise<void> {
  stopWatching?.();
  stopWatching = null;
  if (!profile || settings.mode !== "desktop") return;
  const acct = await loadAccount();
  stopWatching = acct.watchDesktopWallpaper(
    profile.uid,
    (current) => {
      desktop = current;
      remember("current", current);
      drawPanel();
      renderLayout();
      refreshPhoto();
    },
    () => toast("Couldn't read your desktop wallpaper. Showing a photo from here for now."),
  );
}

let started: Promise<void> | null = null;
const startAccount = () => (started ??= watchAccount());

async function watchAccount(): Promise<void> {
  const acct = await loadAccount();
  acct.watchUser((user) => {
    const wasLinked = !!profile;
    profile = user;
    busy = false;
    if (!user) {
      desktop = null;
      remember("current", null);
      if (settings.mode === "desktop") settings = { ...settings, mode: "own" };
      saveSettings(settings);
    } else {
      void acct.verifySession(user);
      // First link: follow the desktop by default, which is why the person connected.
      if (!wasLinked && !remember("linked-before")) {
        settings = { ...settings, mode: "desktop" };
        saveSettings(settings);
        remember("linked-before", true);
        toast(`Connected as ${user.name || user.email}`);
      }
    }
    renderLayout();
    refreshPhoto();
    drawPanel();
    void syncWatcher();
  });
}

interface Handoff {
  token: string;
  at: number;
}

/** The website's sign-in page leaves a Google credential in extension storage (see content/bridge.ts). */
async function consumeHandoff(): Promise<void> {
  // Several new tabs can be open; only one may use the credential.
  await navigator.locks.request("oriel-handoff", async () => {
    const { handoff } = (await chrome.storage.local.get("handoff")) as { handoff?: Handoff };
    if (!handoff) return;
    await chrome.storage.local.remove("handoff");
    if (Date.now() - handoff.at > 50 * 60_000) return; // Google credentials last an hour
    busy = true;
    drawPanel();
    try {
      await startAccount();
      const acct = await loadAccount();
      await acct.signInWithGoogleToken(handoff.token);
      remember("linked-before", null); // a fresh connect follows the desktop again
      // watchUser (above) reacts to the new user and finishes the job.
    } catch {
      busy = false;
      drawPanel();
      toast("Couldn't connect your account. Please try again.");
    }
  });
}

chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "local" && changes.handoff?.newValue) void consumeHandoff();
});

// ---------------------------------------------------------------- start

// Paint immediately from what's remembered; everything async only refines it.
styleBackground();
renderLayout();
scheduleClock();
refreshPhoto();

chrome.storage.local.get("handoff").then(({ handoff }) => {
  if (profile || handoff) void startAccount().then(() => handoff && consumeHandoff());
});
