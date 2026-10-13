"use client";

import { onAuthStateChanged, type User } from "firebase/auth";
import { useEffect, useState } from "react";
import { firebaseAuth } from "@/lib/firebase";

/** Who is signed in on this browser: undefined while Firebase is still checking, null when nobody is. */
export function useUser(): User | null | undefined {
  const [user, setUser] = useState<User | null | undefined>(undefined);
  useEffect(() => {
    let off = () => {};
    let cancelled = false;
    void firebaseAuth().then((auth) => {
      if (!cancelled) off = onAuthStateChanged(auth, setUser);
    });
    return () => {
      cancelled = true;
      off();
    };
  }, []);
  return user;
}

/**
 * Where /login sends someone back to after signing in: "/me" for the old "next=me", or a path on this site. Anything
 * else (another site, "//evil.example") is ignored.
 */
export function returnPath(next: string | null): string | null {
  if (next === "me") return "/me";
  if (!next || !next.startsWith("/") || next.startsWith("//") || next.includes("\\")) return null;
  return next;
}
