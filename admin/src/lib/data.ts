import {
  collection,
  collectionGroup,
  doc,
  getCountFromServer,
  getDoc,
  getDocs,
  limit,
  orderBy,
  query,
  type DocumentData,
  type Timestamp,
  where,
} from "firebase/firestore";
import { cached } from "@/lib/cache";
import { auth, db } from "@/lib/firebase";

/**
 * Everything the admin site reads, straight from Firestore (Spark plan: 50,000 reads a day). Every read goes through
 * `cached` (lib/cache.ts): kept for 10 minutes in memory and localStorage, so moving between pages or reloading costs
 * nothing; Refresh (`refresh = true`) reads again.
 */

export type Platform = "windows" | "linux" | "android" | "macos" | "extension" | "web";

export type Session = {
  id: string;
  uid: string;
  platform: string;
  deviceName: string;
  appVersion: string;
  createdAt: Date | null;
  lastSeenAt: Date | null;
};

export type Account = {
  uid: string;
  email: string;
  displayName: string;
  photoURL: string;
  createdAt: Date | null;
  /** "YYYY-MM-DD" when the person set it on the website's /me, else "". */
  birthday: string;
  sessions: Session[];
  /** The latest lastSeenAt of any of its sessions. */
  lastSeenAt: Date | null;
};

export type Overview = {
  accounts: Account[];
  sessions: Session[];
  versions: Record<string, string>;
  shares: number;
  codes: number;
  loadedAt: Date;
};

export const toDate = (value: unknown): Date | null =>
  value && typeof (value as Timestamp).toDate === "function" ? (value as Timestamp).toDate() : null;

const str = (value: unknown) => (typeof value === "string" ? value : "");

function toSession(uid: string, id: string, d: DocumentData): Session {
  return {
    id,
    uid,
    platform: str(d.platform),
    deviceName: str(d.deviceName),
    appVersion: str(d.appVersion),
    createdAt: toDate(d.createdAt),
    lastSeenAt: toDate(d.lastSeenAt),
  };
}

/** Every account with its sessions, the published app versions and a few counts. */
export const overview = (refresh = false) => cached("overview", loadOverview, refresh);

async function loadOverview(): Promise<Overview> {
  const store = db();
  const [users, sessions, shares, codes, versions] = await Promise.all([
    getDocs(collection(store, "users")),
    getDocs(collectionGroup(store, "sessions")),
    getCountFromServer(collection(store, "shares")),
    getCountFromServer(collection(store, "codes")),
    Promise.all(
      ["windows", "linux", "android", "macos"].map(async (app) => {
        const snap = await getDoc(doc(store, "application", app));
        return [app, str(snap.data()?.version)] as const;
      }),
    ),
  ]);

  const allSessions = sessions.docs.map((s) => toSession(s.ref.parent.parent?.id ?? "", s.id, s.data()));
  const byUser = new Map<string, Session[]>();
  for (const s of allSessions) byUser.set(s.uid, [...(byUser.get(s.uid) ?? []), s]);

  const accounts = users.docs.map((u): Account => {
    const d = u.data();
    const own = (byUser.get(u.id) ?? []).sort((a, b) => time(b.lastSeenAt) - time(a.lastSeenAt));
    return {
      uid: u.id,
      email: str(d.email),
      displayName: str(d.displayName),
      photoURL: str(d.photoURL),
      createdAt: toDate(d.createdAt),
      birthday: /^\d{4}-\d{2}-\d{2}$/.test(str(d.birthday)) ? str(d.birthday) : "",
      sessions: own,
      lastSeenAt: own[0]?.lastSeenAt ?? null,
    };
  });
  accounts.sort((a, b) => time(b.createdAt) - time(a.createdAt));

  return {
    accounts,
    sessions: allSessions,
    versions: Object.fromEntries(versions),
    shares: shares.data().count,
    codes: codes.data().count,
    loadedAt: new Date(),
  };
}

export const time = (d: Date | null) => d?.getTime() ?? 0;

/** A Firestore document as plain data: Timestamps become Dates (so it survives the cache), everything else as stored. */
export type Raw = Record<string, unknown>;
export type Doc = { id: string; path: string; data: Raw };

function plain(value: unknown): unknown {
  const date = toDate(value);
  if (date) return date;
  if (Array.isArray(value)) return value.map(plain);
  if (value && typeof value === "object") return Object.fromEntries(Object.entries(value).map(([k, v]) => [k, plain(v)]));
  return value;
}

const asDoc = (snap: { id: string; ref: { path: string }; data: () => DocumentData | undefined }): Doc => ({
  id: snap.id,
  path: snap.ref.path,
  data: plain(snap.data() ?? {}) as Raw,
});

/** What the account page always shows: the profile, its devices, its wallpaper and schedule. About 8 reads. */
export type AccountBasics = {
  profile: Doc | null;
  sessions: Doc[];
  current: Doc | null;
  settings: Doc | null;
};

export const accountBasics = (uid: string, refresh = false) => cached(`basics:${uid}`, () => loadBasics(uid), refresh);

async function loadBasics(uid: string): Promise<AccountBasics> {
  const store = db();
  const [profile, sessions, current, settings] = await Promise.all([
    getDoc(doc(store, "users", uid)),
    getDocs(collection(store, "users", uid, "sessions")),
    getDoc(doc(store, "users", uid, "sync", "current")),
    getDoc(doc(store, "users", uid, "sync", "settings")),
  ]);
  return {
    profile: profile.exists() ? asDoc(profile) : null,
    sessions: sessions.docs.map(asDoc).sort((a, b) => time(toDate(b.data.lastSeenAt)) - time(toDate(a.data.lastSeenAt))),
    current: current.exists() ? asDoc(current) : null,
    settings: settings.exists() ? asDoc(settings) : null,
  };
}

// The rest is read only when its section on the account page is opened, each part cached on its own.

/** The browser extension's settings and shortcuts (users/{uid}/sync/extension). One read. */
export const accountExtension = (uid: string, refresh = false) =>
  cached(`ext:${uid}`, async () => {
    const snap = await getDoc(doc(db(), "users", uid, "sync", "extension"));
    return snap.exists() ? asDoc(snap) : null;
  }, refresh);

/** Every favorite, starred or not (un-starring keeps the document), newest change first. One read each. */
export const accountFavorites = (uid: string, refresh = false) =>
  cached(`fav:${uid}`, async () => (await getDocs(query(collection(db(), "users", uid, "favorites"), orderBy("updatedAt", "desc")))).docs.map(asDoc), refresh);

export type AccountSocial = {
  followers: Doc[];
  /** The accounts this one follows: their uid and this account's follower document there. */
  following: { owner: string; doc: Doc }[];
  shares: Doc[];
  codes: Doc[];
};

/** Followers, who it follows, its shares and follow code. */
export const accountSocial = (uid: string, refresh = false) =>
  cached(`social:${uid}`, async (): Promise<AccountSocial> => {
    const store = db();
    const [followers, allFollowers, shares, codes] = await Promise.all([
      getDocs(collection(store, "users", uid, "followers")),
      // Who this account follows: its uid is the document id under the friend's followers. Small: one per follow.
      getDocs(collectionGroup(store, "followers")),
      getDocs(query(collection(store, "shares"), where("uid", "==", uid))),
      getDocs(query(collection(store, "codes"), where("uid", "==", uid))),
    ]);
    return {
      followers: followers.docs.map(asDoc),
      following: allFollowers.docs.filter((f) => f.id === uid).map((f) => ({ owner: f.ref.parent.parent?.id ?? "", doc: asDoc(f) })),
      shares: shares.docs.map(asDoc).sort((a, b) => time(toDate(b.data.createdAt)) - time(toDate(a.data.createdAt))),
      codes: codes.docs.map(asDoc),
    };
  }, refresh);

export type AccountLive = { slot: Doc | null; pulse: Raw | null; pulseError?: string };

/** The Realtime Database doorbell and the last claimed scheduled change. */
export const accountLive = (uid: string, refresh = false) =>
  cached(`live:${uid}`, async (): Promise<AccountLive> => {
    const [slot, pulse] = await Promise.all([getDoc(doc(db(), "users", uid, "sync", "slot")), readPulse(uid)]);
    return { slot: slot.exists() ? asDoc(slot) : null, ...pulse };
  }, refresh);

const realtimeUrl = "https://oriel-gcp-default-rtdb.firebaseio.com";

/** pulse/{uid} over REST with the admin's ID token (database.rules.json lets the admin read it). */
async function readPulse(uid: string): Promise<{ pulse: Raw | null; pulseError?: string }> {
  try {
    const token = await auth().currentUser?.getIdToken();
    const r = await fetch(`${realtimeUrl}/pulse/${uid}.json?auth=${token}`);
    if (r.status === 401) return { pulse: null, pulseError: "The Realtime Database rules don't let the admin read it yet (deploy database.rules.json)." };
    if (!r.ok) return { pulse: null, pulseError: `Realtime Database answered ${r.status}.` };
    return { pulse: (await r.json()) as Raw | null };
  } catch {
    return { pulse: null, pulseError: "Couldn't reach the Realtime Database." };
  }
}

export type Share = {
  id: string;
  uid: string;
  from: string;
  picture: string;
  photoId: string;
  photoPath: string;
  photographer: string;
  createdAt: Date | null;
};

export type Code = { code: string; uid: string; name: string; picture: string; photoPath: string; createdAt: Date | null };

export const shares = (refresh = false) => cached("shares", loadShares, refresh);

async function loadShares(): Promise<Share[]> {
  const snap = await getDocs(query(collection(db(), "shares"), orderBy("createdAt", "desc"), limit(200)));
  return snap.docs.map((s) => {
    const d = s.data();
    return {
      id: s.id,
      uid: str(d.uid),
      from: str(d.from),
      picture: str(d.picture),
      photoId: str(d.photoId),
      photoPath: str(d.photoPath),
      photographer: str(d.photographer),
      createdAt: toDate(d.createdAt),
    };
  });
}

export const codes = (refresh = false) => cached("codes", loadCodes, refresh);

async function loadCodes(): Promise<Code[]> {
  const snap = await getDocs(query(collection(db(), "codes"), orderBy("createdAt", "desc"), limit(200)));
  return snap.docs.map((s) => {
    const d = s.data();
    return { code: s.id, uid: str(d.uid), name: str(d.name), picture: str(d.picture), photoPath: str(d.photoPath), createdAt: toDate(d.createdAt) };
  });
}

/** A small preview of a synced image: Unsplash crops on its CDN; a Pollinations picture is used as it is. */
export function thumb(imageUrl: string, width = 480): string {
  if (!imageUrl.startsWith("https://images.unsplash.com/")) return imageUrl;
  const sep = imageUrl.includes("?") ? "&" : "?";
  return `${imageUrl}${sep}w=${width}&q=70&auto=format&fit=crop`;
}

/** A share or follow code keeps only the image path ("photo-1234..."). */
export const photoFromPath = (path: string, width = 480) => thumb(`https://images.unsplash.com/${path}`, width);
