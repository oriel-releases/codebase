"use client";

import { useCallback, useEffect, useState } from "react";

/**
 * useState that this browser remembers (localStorage), for the page's own choices: a filter, a sort, a search. Going
 * into an account and back, or reloading, finds them as they were. The pages only render after sign-in (AdminGate),
 * so reading storage on the first render can't disagree with the static HTML.
 */
export function useRemembered<T>(key: string, initial: T): [T, (value: T) => void] {
  const storageKey = `oriel-admin:pref:${key}`;
  const [value, setValue] = useState<T>(() => {
    try {
      const saved = localStorage.getItem(storageKey);
      return saved === null ? initial : (JSON.parse(saved) as T);
    } catch {
      return initial;
    }
  });
  const remember = useCallback(
    (next: T) => {
      setValue(next);
      try {
        localStorage.setItem(storageKey, JSON.stringify(next));
      } catch {
        // Storage blocked: the choice still holds until the tab closes.
      }
    },
    [storageKey],
  );
  return [value, remember];
}

/**
 * Brings a list back to where it was scrolled when the person left it (into an account and back). `ready` is when
 * the rows are on the page; before that there's nothing to scroll to.
 */
export function useScrollMemory(key: string, ready: boolean) {
  const storageKey = `oriel-admin:scroll:${key}`;
  useEffect(() => {
    if (!ready) return;
    try {
      const y = Number(sessionStorage.getItem(storageKey));
      if (y) requestAnimationFrame(() => window.scrollTo({ top: y }));
    } catch {
      // Storage blocked: start at the top.
    }
    const save = () => {
      try {
        sessionStorage.setItem(storageKey, String(window.scrollY));
      } catch {}
    };
    window.addEventListener("scroll", save, { passive: true });
    return () => window.removeEventListener("scroll", save);
  }, [ready, storageKey]);
}
