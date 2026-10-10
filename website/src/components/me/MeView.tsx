"use client";

import { getAuth, onAuthStateChanged, signOut, type User } from "firebase/auth";
import { collection, doc, getDoc, onSnapshot, writeBatch } from "firebase/firestore";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { firebaseApp, firebaseAuth, firestore } from "@/lib/firebase";
import { endSession, ensureWebSession, forgetWebSession, localSessionId, toSessionRow, type SessionRow } from "@/lib/sessions";
import { MeContent, Spinner, type Asking, type Wallpaper as WallpaperData } from "@/components/me/MeContent";

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

  // Nothing but the spinner shows until the account, wallpaper and sessions have all arrived; then the page appears
  // whole (a soft fade, nothing moves) while the spinner fades out in place.
  const spinner = spinnerGone ? null : <Spinner leaving={ready} />;
  return (
    <div className="relative min-h-[60svh]">
      {user && ready && (
        <div className="me-enter">
          <MeContent
            profile={{ name: user.displayName ?? "", email: user.email ?? "", photo: user.photoURL ?? "", memberSince }}
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
