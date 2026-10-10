import { remember, type Settings } from "../lib/settings";
import type { DesktopCurrent, Profile } from "../lib/account";

/** The Firebase SDK is only loaded when there's an account to talk to. */
type Account = typeof import("../lib/account");

interface Handoff {
  token: string;
  at: number;
}

/** What the account logic needs from the page. It never touches the DOM itself. */
export interface AccountSyncHost {
  settings(): Settings;
  /** Merge, save and remember new settings. */
  update(patch: Partial<Settings>): void;
  /** The linked account or the desktop wallpaper changed: redraw. */
  changed(): void;
  /** Only the "busy" (signing in) flag changed: the panel needs a redraw. */
  busyChanged(): void;
  toast(message: string): void;
}

/**
 * Everything about the Oriel account, apart from how it looks: who is linked, what the desktop wallpaper is, signing in
 * from the website's handoff, and following the desktop's `sync/current` document while the mode asks for it.
 */
export function createAccountSync(host: AccountSyncHost) {
  let profile: Profile | null = remember<Profile>("profile");
  let desktop: DesktopCurrent | null = remember<DesktopCurrent>("current");
  let busy = false;
  let account: Account | null = null;
  let stopWatching: (() => void) | null = null;
  let started: Promise<void> | null = null;

  const load = async (): Promise<Account> => (account ??= await import("../lib/account"));

  function setBusy(value: boolean): void {
    busy = value;
    host.busyChanged();
  }

  /** Follows the desktop's wallpaper only while the account is linked and the mode asks for it. */
  async function syncWatcher(): Promise<void> {
    stopWatching?.();
    stopWatching = null;
    if (!profile || host.settings().mode !== "desktop") return;
    const acct = await load();
    stopWatching = acct.watchDesktopWallpaper(
      profile.uid,
      (current) => {
        desktop = current;
        remember("current", current);
        host.changed();
      },
      () => host.toast("Couldn't read your desktop wallpaper. Showing a photo from here for now."),
    );
  }

  async function watch(): Promise<void> {
    const acct = await load();
    acct.watchUser((user) => {
      const wasLinked = !!profile;
      profile = user;
      busy = false;
      if (!user) {
        desktop = null;
        remember("current", null);
        if (host.settings().mode === "desktop") host.update({ mode: "own" });
      } else {
        void acct.verifySession(user);
        // First link: follow the desktop by default, which is why the person connected.
        if (!wasLinked && !remember("linked-before")) {
          host.update({ mode: "desktop" });
          remember("linked-before", true);
          host.toast(`Connected as ${user.name || user.email}`);
        }
      }
      host.changed();
      void syncWatcher();
    });
  }

  /** Starts watching the signed-in user (once). */
  const start = (): Promise<void> => (started ??= watch());

  /** The website's sign-in page leaves a Google credential in extension storage (see content/bridge.ts). */
  async function consumeHandoff(): Promise<void> {
    // Several new tabs can be open; only one may use the credential.
    await navigator.locks.request("oriel-handoff", async () => {
      const { handoff } = (await chrome.storage.local.get("handoff")) as { handoff?: Handoff };
      if (!handoff) return;
      await chrome.storage.local.remove("handoff");
      if (Date.now() - handoff.at > 50 * 60_000) return; // Google credentials last an hour
      setBusy(true);
      try {
        await start();
        const acct = await load();
        await acct.signInWithGoogleToken(handoff.token);
        remember("linked-before", null); // a fresh connect follows the desktop again
        // watchUser (above) reacts to the new user and finishes the job.
      } catch {
        setBusy(false);
        host.toast("Couldn't connect your account. Please try again.");
      }
    });
  }

  return {
    get profile() {
      return profile;
    },
    get desktop() {
      return desktop;
    },
    get busy() {
      return busy;
    },
    setBusy,
    start,
    syncWatcher,
    consumeHandoff,
    async signOut(): Promise<void> {
      await (await load()).signOutAccount();
    },
  };
}

export type AccountSync = ReturnType<typeof createAccountSync>;
