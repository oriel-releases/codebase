import "../newtab/style.css";
import { renderPanel, type PanelActions, type PanelState } from "../newtab/panel";
import type { DesktopCurrent, Profile } from "../lib/account";
import { loadSettings, remember, saveSettings, syncedSettings, type Settings } from "../lib/settings";
import { siteUrl } from "../lib/config";
import { loadAccount } from "../lib/load-account";
import { imageUrl, pick, slotKey, type Photo } from "../lib/photo";
import { readableAccent } from "../lib/accent";
import { protectImages } from "../lib/protect";
import { faviconUrl, loadShortcuts, maxShortcuts, normalizeUrl, sameLink, saveShortcuts, titleFor } from "../lib/shortcuts";

protectImages();

/**
 * The toolbar icon's popup: the same settings dialog as the new tab, drawn under the icon. Settings are saved to the
 * same storage, so open new tabs pick the change up through their `storage` listener.
 */
const panel = document.getElementById("panel")!;
let settings = loadSettings();
let profile: Profile | null = remember<Profile>("profile");
let signingOut = false;
let watching = false;
/** The page the popup was opened on (a website: the new tab never shows this popup), or null. */
let here: { title: string; url: string } | "checking" | null = "checking";

// The photo the new tab last showed, blurred and dimmed behind the dialog exactly as on the new tab.
let last = remember<{ photo: Photo; accent: string }>("shown");
let changing = false;

function paintBackground(): void {
  if (!last) return;
  const bg = document.getElementById("bg") as HTMLImageElement;
  bg.src = imageUrl(last.photo, 200, 140, 50); // blurred anyway: a small one loads at once and costs nothing to draw
  bg.hidden = false;
  // The new tab's accent, or else the photo's own color, so the popup is never plain white.
  const accent = readableAccent(last.accent || last.photo.color);
  if (accent) document.documentElement.style.setProperty("--accent", accent);
}
// After the page has loaded: the browser shows the popup only then, and shouldn't wait for this download.
if (document.readyState === "complete") paintBackground();
else window.addEventListener("load", paintBackground, { once: true });

/**
 * Change now / Wallpaper of the day, from the popup. Following the account: published to every device (the open new
 * tabs hear the doorbell). Otherwise: pinned to this time slot and handed to the open new tabs of this browser.
 */
async function changePhoto(chosen?: Photo): Promise<void> {
  if (changing) return;
  changing = true;
  draw();
  try {
    settings = loadSettings();
    let photo = chosen;
    if (settings.mode === "desktop" && profile) {
      photo ??= pick(settings.topics, "tab", 0);
      await (await watchAccount()).publishWallpaper(photo);
    } else {
      if (!photo) {
        settings = { ...settings, bump: settings.bump + 1 };
        saveSettings(settings);
        photo = pick(settings.topics, settings.change, settings.bump);
      }
      const key = slotKey(settings.change, settings.bump);
      if (key) remember("slot", { key, photo });
      remember("shared-photo", { photo, at: Date.now() });
    }
    last = { photo, accent: "" };
    remember("shown", last);
    paintBackground();
  } catch {
    // Offline: the photo stays as it was.
  } finally {
    changing = false;
    draw();
  }
}
document.getElementById("scrim")!.style.backgroundColor = `rgb(0 0 0 / ${settings.dim / 100})`;

// Firebase is only loaded when the person actually signs in or out.
const account = loadAccount;

/** Sign-in and sign-out finish in Firebase; the cached profile follows, and the dialog with it. */
async function watchAccount(): Promise<typeof import("../lib/account")> {
  const lib = await account();
  if (!watching) {
    watching = true;
    lib.watchUser((user) => {
      profile = user;
      if (!user && settings.mode === "desktop") change({ mode: "own" });
      else draw();
    });
  }
  return lib;
}

let uploadTimer: number | undefined;

/**
 * Sends this browser's settings and shortcuts to the account right away (the upload rings the other browsers), instead
 * of waiting for a new tab to do it. Only something changed here is sent; if the popup closes first, the change stays
 * marked and the next new tab sends it.
 */
async function upload(): Promise<void> {
  clearTimeout(uploadTimer);
  const dirty = remember<number>("ext-dirty");
  if (!profile || !dirty) return;
  try {
    await (await watchAccount()).publishExtensionSync(syncedSettings(loadSettings()), loadShortcuts());
    if (remember<number>("ext-dirty") === dirty) remember("ext-dirty", null);
  } catch {
    // Offline: a new tab sends it later.
  }
}

function change(patch: Partial<Settings>): void {
  settings = { ...loadSettings(), ...patch };
  saveSettings(settings);
  // A burst of changes (a slider, several switches) is one upload.
  clearTimeout(uploadTimer);
  uploadTimer = window.setTimeout(() => void upload(), 600);
  draw();
}

const actions: PanelActions = {
  change,
  preview() {}, // the photo isn't on screen here
  connect() {
    window.open(`${siteUrl()}/login?for=extension`, "_blank");
    window.close();
  },
  async signInEmail(email, password) {
    await (await watchAccount()).signInWithEmail(email, password);
    remember("linked-before", null);
  },
  async resetPassword(email) {
    await (await watchAccount()).resetPassword(email);
  },
  openApp() {},
  changeNow: () => void changePhoto(),
  setPhoto: (photo) => void changePhoto(photo),
  addHere() {
    if (!here || here === "checking") return;
    const items = loadShortcuts();
    const page = here;
    if (items.length >= maxShortcuts || items.some((s) => sameLink(s.url, page.url))) return;
    // Saved here first (the new tabs read it from this browser's storage), then sent to the account.
    saveShortcuts([...items, { title: page.title, url: page.url }]);
    draw();
    void upload();
  },
  async disconnect() {
    signingOut = true;
    draw();
    try {
      await (await watchAccount()).signOutAccount();
    } finally {
      signingOut = false;
      draw();
    }
  },
};

function draw(): void {
  const items = loadShortcuts();
  const page = here;
  const current =
    page === "checking"
      ? page
      : page && {
          ...page,
          favicon: faviconUrl(page.url),
          added: items.some((s) => sameLink(s.url, page.url)),
          full: items.length >= maxShortcuts,
        };
  const state: PanelState = { settings, profile, busy: false, signingOut, desktop: remember<DesktopCurrent>("current"), banner: last ? imageUrl(last.photo, 560, 224, 45) : null,
    photo: last?.photo ?? null,
    changing,
    here: current,
  };
  renderPanel(panel, state, actions);
}

document.addEventListener("oriel:close-panel", () => window.close());
document.addEventListener("oriel:redraw-panel", draw);
draw();

// The tab you clicked the icon on ("activeTab" lets the popup see its address).
// Until it answers, the card says "Checking…"; a page that can't be a shortcut (chrome://, a PDF viewer) drops it.
void chrome.tabs.query({ active: true, currentWindow: true }).then(([tab]) => {
  const url = tab?.url && /^https?:/.test(tab.url) ? normalizeUrl(tab.url) : null;
  here = url ? { title: (tab.title?.trim() || titleFor(url)).slice(0, 40), url } : null;
  draw();
});
