import { lacksSettings, remember, syncedSettings, type Settings } from "../lib/settings";
import { loadAccount } from "../lib/load-account";
import { loadShortcuts, type Shortcut } from "../lib/shortcuts";
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
  /** Settings and shortcuts arrived from the account (another browser changed them): take them over and redraw. */
  applySynced(settings: Partial<Settings>, shortcuts: Shortcut[]): void;
  /** The account's birthday was read and differs from before: redraw the clock and the panel. */
  birthdayChanged(): void;
}

/** The account's birthday as last read, for one account. */
interface KnownBirthday {
  uid: string;
  birthday: string | null;
  at: number;
  /** The doorbell's birthdayAt this read follows (the website rings it on each change); a newer ring reads it again. */
  version?: number;
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
  let known = remember<KnownBirthday>("account-birthday");
  let readingBirthday = false;
  /** A birthday ring that came during a read: read once more after it, for that version. */
  let ringDuringRead: number | undefined;

  const showsAge = () => host.settings().clock && host.settings().clockShows === "age";

  /**
   * Reads the account's birthday for the Age clock, only while that's shown. A change on the website rings the
   * doorbell (onBirthday below), which reads it at once; besides that, a known birthday is read again after an hour
   * and a missing one after 20 seconds.
   */
  async function refreshBirthday(force = false, version = known?.uid === profile?.uid ? known?.version : undefined): Promise<void> {
    if (!profile) return;
    if (readingBirthday) {
      if (force) ringDuringRead = version;
      return;
    }
    const fresh = known?.uid === profile.uid && Date.now() - known.at < (known.birthday ? 3_600_000 : 20_000);
    if (fresh && !force) return;
    readingBirthday = true;
    try {
      const birthday = await (await load()).readBirthday();
      const changed = known?.uid !== profile.uid || known.birthday !== birthday;
      known = remember<KnownBirthday>("account-birthday", { uid: profile.uid, birthday, at: Date.now(), version });
      if (changed) host.birthdayChanged();
    } catch {
      // Offline: the last one read stays, and the next tab tries again.
    } finally {
      readingBirthday = false;
      if (ringDuringRead !== undefined) {
        const next = ringDuringRead;
        ringDuringRead = undefined;
        void refreshBirthday(true, next);
      }
    }
  }

  /** The doorbell's birthdayAt: newer than the read this browser has, so the birthday was set or removed on the website. */
  function onBirthday(at: number | undefined): void {
    if (!profile || at === undefined) return;
    if (known?.uid === profile.uid && known.version !== undefined && known.version >= at) return;
    if (showsAge()) return void refreshBirthday(true, at);
    // Not on screen: forget the read, so the Age clock reads it when it's shown next.
    if (known?.uid === profile.uid) known = remember<KnownBirthday>("account-birthday", { ...known, at: 0, version: at });
  }

  const load = async (): Promise<Account> => (account ??= await loadAccount());

  function setBusy(value: boolean): void {
    busy = value;
    host.busyChanged();
  }

  /**
   * The live doorbell connection, open while an account is linked: it follows the account's wallpaper while the mode
   * asks for it, and the extension's settings and shortcuts always. Called again when the mode changes.
   */
  async function syncWatcher(): Promise<void> {
    stopWatching?.();
    stopWatching = null;
    if (!profile) return;
    const acct = await load();
    stopWatching = acct.watchAccount(profile.uid, {
      followsWallpaper: () => host.settings().mode === "desktop",
      knownPhotoId: () => desktop?.photo.id,
      onCurrent(current) {
        desktop = current;
        remember("current", current);
        host.changed();
      },
      onError: () => host.toast("Couldn't read your desktop wallpaper. Showing a photo from here for now."),
      onBirthday,
      onExtension(at, mine) {
        const have = remember<number>("ext-version");
        if (mine) {
          // Our own upload's ring: this browser already has that version.
          if (at !== undefined && at > (have ?? 0)) remember("ext-version", at);
          return;
        }
        // Read the account's copy only when it changed since this browser last had it (or was never read here).
        if (at === undefined ? have === null : at > (have ?? 0)) void syncExtension({ read: true, version: at });
        else void syncExtension(); // nothing new there; upload a change made here, if any
      },
    });
  }

  let extensionBusy = false;
  let queued: { adopt?: boolean; read?: boolean; version?: number } | null = null;
  let pushTimer: number | undefined;

  /**
   * Keeps the extension's settings and shortcuts one and the same in every browser of the account
   * (users/{uid}/sync/extension), without reading Firestore on every new tab: this browser keeps them in its own storage
   * with the version it has ("ext-version", the doorbell's extensionAt). Changed here: upload them (the upload rings the
   * other browsers). `read`: the doorbell says the account's copy is newer, so read it once and take it over.
   * `adopt` is the first sign-in: like the apps, the account's copy wins over this browser's.
   */
  async function syncExtension(options: { adopt?: boolean; read?: boolean; version?: number } = {}): Promise<void> {
    if (!profile) return;
    if (extensionBusy) {
      queued = options; // a ring during an upload or read: handle it right after
      return;
    }
    extensionBusy = true;
    try {
      const acct = await load();
      const upload = async () => {
        const dirty = remember<number>("ext-dirty");
        await acct.publishExtensionSync(syncedSettings(host.settings()), loadShortcuts());
        if (remember<number>("ext-dirty") === dirty) remember("ext-dirty", null); // unless it changed again meanwhile
      };
      // A change made here is the newest there is (last write wins), unless this is the first sign-in.
      if (remember("ext-dirty") && !options.adopt) return await upload();
      if (!options.read && !options.adopt) return;
      const remote = await acct.readExtensionSync();
      if (!remote) return await upload(); // nothing kept yet: this browser's become the account's
      if (remember("ext-dirty") && !options.adopt) return await upload(); // changed here while reading: this is newer
      remember("ext-dirty", null);
      // The doorbell's time (the server's clock, never this computer's). Without one (first sign-in), 0: the doorbell's
      // first event then confirms it with one more read at most.
      remember("ext-version", options.version ?? 0);
      host.applySynced(remote.settings, remote.shortcuts);
      // Written by an older version: fill in the settings it didn't know (new ones keep this browser's value).
      if (lacksSettings(remote.settings)) await upload();
    } catch {
      // Offline or signed out meanwhile: a change stays marked and goes up next time.
    } finally {
      extensionBusy = false;
      if (queued) {
        const next = queued;
        queued = null;
        void syncExtension(next);
      }
    }
  }

  /** A setting or shortcut changed here: upload shortly, so a burst of changes is one write. */
  function pushExtensionSoon(): void {
    if (!profile) return;
    clearTimeout(pushTimer);
    pushTimer = window.setTimeout(() => void syncExtension(), 1500);
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
        void syncExtension({ adopt: !wasLinked });
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

  /** Email and password, typed in the panel. Throws the reason (in words) when it doesn't work. */
  async function signInWithEmail(email: string, password: string): Promise<void> {
    setBusy(true);
    try {
      await start();
      await (await load()).signInWithEmail(email, password);
      remember("linked-before", null); // a fresh connect follows the desktop again
      // watchUser (above) reacts to the new user and finishes the job.
    } catch (e) {
      setBusy(false);
      throw e;
    }
  }

  return {
    signInWithEmail,
    resetPassword: async (email: string) => (await load()).resetPassword(email),
    get profile() {
      return profile;
    },
    get desktop() {
      return desktop;
    },
    get busy() {
      return busy;
    },
    /** The signed-in account's birthday as last read (undefined: never read yet for this account). */
    get birthday(): string | null | undefined {
      return profile && known?.uid === profile.uid ? known.birthday : undefined;
    },
    refreshBirthday,
    setBusy,
    start,
    syncWatcher,
    consumeHandoff,
    syncExtension,
    pushExtensionSoon,
    /** Shows this photo on every device of the account (see account.ts publishWallpaper). */
    async publish(photo: import("../lib/photo").Photo): Promise<void> {
      await (await load()).publishWallpaper(photo);
    },
    async signOut(): Promise<void> {
      await (await load()).signOutAccount();
      known = remember<KnownBirthday>("account-birthday", null); // the account's birthday leaves with it
    },
  };
}

export type AccountSync = ReturnType<typeof createAccountSync>;
