"use client";

import { getAuth, onAuthStateChanged, signOut, type User } from "firebase/auth";
import { collection, doc, getDoc, onSnapshot, writeBatch } from "firebase/firestore";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { firebaseApp, firebaseAuth, firestore } from "@/lib/firebase";
import { endSession, ensureWebSession, forgetWebSession, localSessionId, toSessionRow, type SessionRow } from "@/lib/sessions";
import {
  addPassword,
  changePassword,
  confirmIdentity,
  connectGoogle,
  disconnect,
  methodError,
  methodsOf,
  needsRecentLogin,
  sendVerification,
  type Methods,
} from "@/lib/sign-in-methods";
import { MeContent, Spinner, type Asking, type Wallpaper as WallpaperData } from "@/components/me/MeContent";
import type { MethodActions, Outcome } from "@/components/me/SignInMethods";

/** Loads the signed-in account's data and wires the buttons. Signed-out visitors are sent to /login and come back here. */
export function MeView() {
  const router = useRouter();
  // Always start at the top: on a reload the browser would otherwise slide back to wherever you had scrolled (smoothly, because the site scrolls smoothly).
  useEffect(() => {
    const previous = history.scrollRestoration;
    history.scrollRestoration = "manual";
    window.scrollTo({ top: 0, behavior: "instant" });
    return () => {
      history.scrollRestoration = previous;
    };
  }, []);
  const [user, setUser] = useState<User | null | undefined>(undefined);
  // How the account signs in. Kept as state: the Firebase user object changes in place when a method is added or removed.
  const [methods, setMethods] = useState<Methods | null>(null);
  const [memberSince, setMemberSince] = useState<Date | null>(null);
  const [wallpaper, setWallpaper] = useState<WallpaperData | null | undefined>(undefined);
  const [sessions, setSessions] = useState<SessionRow[] | null>(null);
  const [currentId, setCurrentId] = useState<string | null>(null);
  const [asking, setAsking] = useState<Asking>(null);
  const [busy, setBusy] = useState(false);
  // The spinner stays a moment after the account arrives so it can leave with an animation instead of vanishing.
  const [spinnerGone, setSpinnerGone] = useState(false);
  const ready = Boolean(user) && wallpaper !== undefined && sessions !== null;
  useEffect(() => {
    if (!ready) return;
    const timer = setTimeout(() => setSpinnerGone(true), 400);
    return () => clearTimeout(timer);
  }, [ready]);

  useEffect(() => {
    let off = () => {};
    let cancelled = false;
    void firebaseAuth().then((auth) => {
      if (cancelled) return;
      off = onAuthStateChanged(auth, (next) => {
        setUser(next);
        setMethods(next ? methodsOf(next) : null);
        if (!next) router.replace("/login?next=me");
      });
    });
    return () => {
      cancelled = true;
      off();
    };
  }, [router]);

  const uid = user?.uid;
  useEffect(() => {
    if (!uid) return;
    const db = firestore();
    const stops: (() => void)[] = [];
    let cancelled = false;

    void (async () => {
      const id = await ensureWebSession(uid);
      if (cancelled) return;
      setCurrentId(id);

      const profile = await getDoc(doc(db, "users", uid)).catch(() => null);
      if (!cancelled) setMemberSince(profile?.data()?.createdAt?.toDate?.() ?? null);

      stops.push(
        onSnapshot(doc(db, "users", uid, "sync", "current"), (snap) => {
          const d = snap.data();
          setWallpaper(
            d && typeof d.imageUrl === "string"
              ? {
                  imageUrl: d.imageUrl,
                  photographer: String(d.photographer ?? ""),
                  photographerUrl: String(d.photographerUrl ?? ""),
                  pageUrl: String(d.pageUrl ?? ""),
                  description: String(d.description ?? ""),
                  source: String(d.source ?? ""),
                  updatedAt: d.updatedAt?.toDate?.() ?? null,
                }
              : null,
          );
        }, () => setWallpaper(null)),
      );

      stops.push(
        onSnapshot(collection(db, "users", uid, "sessions"), (snap) => {
          const rows = snap.docs.map((d) => toSessionRow(d.id, d.data()));
          rows.sort((a, b) => (b.lastSeenAt?.getTime() ?? 0) - (a.lastSeenAt?.getTime() ?? 0));
          setSessions(rows);
          // This browser's session was ended from somewhere else: sign out here too.
          if (!snap.metadata.fromCache && !rows.some((r) => r.id === id)) {
            forgetWebSession();
            void signOut(auth());
          }
        }),
      );
    })();

    return () => {
      cancelled = true;
      stops.forEach((stop) => stop());
    };
  }, [uid]);

  async function end(id: string) {
    if (!uid) return;
    setBusy(true);
    try {
      await endSession(uid, id);
      if (id === (currentId ?? localSessionId())) {
        forgetWebSession();
        await signOut(auth());
      }
    } finally {
      setAsking(null);
      setBusy(false);
    }
  }

  async function endOthers() {
    if (!uid || !sessions) return;
    setBusy(true);
    try {
      const batch = writeBatch(firestore());
      for (const s of sessions) if (s.id !== currentId) batch.delete(doc(firestore(), "users", uid, "sessions", s.id));
      await batch.commit();
    } finally {
      setAsking(null);
      setBusy(false);
    }
  }

  async function signOutHere() {
    if (!uid) return;
    setBusy(true);
    const id = currentId ?? localSessionId();
    try {
      if (id) await endSession(uid, id).catch(() => {});
    } finally {
      forgetWebSession();
      await signOut(auth());
    }
  }

  /**
   * Runs a change to how the account signs in and reports it to the card in plain terms. Firebase may want a fresh
   * sign-in first: then the card asks the person to prove it's them (with Google if the account has it) and retries.
   */
  async function attempt(change: (user: User) => Promise<void>): Promise<Outcome> {
    const current = auth().currentUser;
    if (!current) return { ok: false, error: "You're signed out. Sign in again." };
    try {
      // No await before this: connecting Google opens a window, which browsers only allow straight after a click.
      await change(current);
      await current.reload().catch(() => {});
      setMethods(methodsOf(current));
      return { ok: true };
    } catch (e) {
      if (needsRecentLogin(e)) return { ok: false, confirm: methodsOf(current).google ? "google" : "password" };
      return { ok: false, error: methodError(e) };
    }
  }

  const methodActions: MethodActions = {
    addPassword: (password) => attempt((u) => addPassword(u, password)),
    changePassword: (current, next) => attempt((u) => changePassword(u, current, next)),
    connectGoogle: () => attempt(connectGoogle),
    disconnect: (method) => attempt((u) => disconnect(u, method)),
    confirm: (password) => attempt((u) => confirmIdentity(u, password)),
    sendVerification: () => attempt(sendVerification),
  };

  // Nothing but the spinner shows until the account, wallpaper and sessions have all arrived; then the page appears
  // whole (a soft fade, nothing moves) while the spinner fades out in place.
  const spinner = spinnerGone ? null : <Spinner leaving={ready} />;
  return (
    <div className="relative min-h-[60svh]">
      {user && ready && methods && (
        <div className="me-enter">
          <MeContent
            profile={{ name: user.displayName ?? "", email: user.email ?? "", photo: user.photoURL ?? "", memberSince }}
            methods={methods}
            methodActions={methodActions}
            wallpaper={wallpaper}
            sessions={sessions}
            currentId={currentId}
            asking={asking}
            busy={busy}
            onAsk={setAsking}
            onEnd={end}
            onEndOthers={endOthers}
            onSignOut={signOutHere}
          />
        </div>
      )}
      {spinner}
    </div>
  );
}

/** The Firebase auth instance, for signing out once the page is already running. */
const auth = () => getAuth(firebaseApp());
