"use client";

import { ChevronDown, LoaderCircle, type LucideIcon } from "lucide-react";
import { useEffect, useState, type ReactNode } from "react";
import { Card } from "@/components/ui/card";
import { cn } from "@/lib/utils";

type Props<T> = {
  title: string;
  icon: LucideIcon;
  /** A word or two beside the title while it's closed. */
  hint?: string;
  /** Reads the section's data; `refresh` skips the cache. Not called until the section is opened. */
  load: (refresh: boolean) => Promise<T>;
  /** Goes up when the page's Refresh is pressed: an open section reads again. */
  round: number;
  children: (data: T) => ReactNode;
};

/** A closed card that reads its data from Firestore only once it's opened (and again on Refresh while open). */
export function LazySection<T>({ title, icon: Icon, hint, load, round, children }: Props<T>) {
  const [open, setOpen] = useState(false);
  const [state, setState] = useState<{ data?: T; error?: string; loading: boolean; round: number }>({ loading: false, round: -1 });

  // Starting a read when opening (or on Refresh) is done in the handler and in render, not in an effect body.
  const wanted = open && (state.round === -1 || state.round !== round);
  if (wanted && !state.loading) setState((s) => ({ ...s, loading: true, error: undefined, round }));

  useEffect(() => {
    if (!state.loading) return;
    let live = true;
    load(state.round > 0 && state.data !== undefined)
      .then((data) => live && setState((s) => ({ ...s, data, loading: false })))
      .catch((e: unknown) => live && setState((s) => ({ ...s, loading: false, error: (e as Error).message || "Couldn't read it." })));
    return () => {
      live = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state.loading, state.round]);

  return (
    <Card className="gap-0 py-0">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        className="flex w-full items-center gap-3 px-5 py-4 text-left hover:bg-secondary/50"
      >
        <Icon className="size-4 text-muted-foreground" />
        <span className="font-medium">{title}</span>
        {hint && !open && <span className="text-sm text-muted-foreground">{hint}</span>}
        {state.loading && <LoaderCircle className="size-4 animate-spin text-muted-foreground" />}
        <ChevronDown className={cn("ml-auto size-4 text-muted-foreground transition-transform", open && "rotate-180")} />
      </button>
      {open && (
        <div className="border-t px-5 py-4">
          {state.error ? (
            <p className="text-sm text-destructive">{state.error}</p>
          ) : state.data !== undefined ? (
            children(state.data)
          ) : (
            <p className="text-sm text-muted-foreground">Reading…</p>
          )}
        </div>
      )}
    </Card>
  );
}
