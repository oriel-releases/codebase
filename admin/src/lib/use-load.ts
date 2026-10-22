"use client";

import { useCallback, useEffect, useState } from "react";

export type Load<T> = { data?: T; error?: string; loading: boolean; reload: () => void };

/** Runs `load` on mount (and on reload); `refresh` is true for a reload, so cached reads can be skipped. */
export function useLoad<T>(load: (refresh: boolean) => Promise<T>, deps: unknown[] = []): Load<T> {
  const [state, setState] = useState<{ data?: T; error?: string; loading: boolean }>({ loading: true });
  const [round, setRound] = useState(0);

  useEffect(() => {
    let live = true;
    load(round > 0)
      .then((data) => live && setState({ data, loading: false }))
      .catch((e: unknown) => live && setState((s) => ({ ...s, loading: false, error: message(e) })));
    return () => {
      live = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [round, ...deps]);

  const reload = useCallback(() => {
    setState((s) => ({ ...s, loading: true, error: undefined }));
    setRound((r) => r + 1);
  }, []);
  return { ...state, reload };
}

function message(e: unknown): string {
  const code = (e as { code?: string }).code;
  if (code === "permission-denied") return "Firestore said no: the rules don't let this account read it. Deploy website/firestore.rules.";
  if (code === "failed-precondition") return "Firestore needs an index for this. The browser console has a link that creates it.";
  return (e as Error).message || "Something went wrong.";
}
