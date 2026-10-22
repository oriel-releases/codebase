"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import { ageOn, birthdayOf, daysIn, isBirthday, MAX_AGE, MIN_AGE } from "@/lib/birthday";

/** Where the year starts when no birthday is saved yet: 20 years ago. */
const DEFAULT_AGE = 20;
const months = Array.from({ length: 12 }, (_, i) => new Date(2000, i, 1).toLocaleDateString("en", { month: "long" }));

type Props = {
  open: boolean;
  /** "YYYY-MM-DD" (users/{uid}.birthday), or null. */
  birthday: string | null;
  /** Saves the birthday, or removes it (null). Throws when it couldn't. */
  onSave: (birthday: string | null) => Promise<void>;
  onClose: () => void;
};

type Parts = { day: number; month: number; year: number };

function partsOf(birthday: string | null): Parts {
  if (birthday && isBirthday(birthday)) {
    const [year, month, day] = birthday.split("-").map(Number);
    return { day: day!, month: month!, year: year! };
  }
  return { day: 0, month: 0, year: new Date().getFullYear() - DEFAULT_AGE };
}

/** Your birthday in its own window, like the page's other dialogs: day, month and year, and the age they make. */
export function BirthdayDialog({ open, birthday, onSave, onClose }: Props) {
  const ref = useRef<HTMLDialogElement>(null);
  const [parts, setParts] = useState<Parts>(() => partsOf(birthday));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();

  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    if (open && !dialog.open) dialog.showModal();
    if (!open && dialog.open) dialog.close();
  }, [open]);

  // Each opening starts from the saved birthday.
  const [wasOpen, setWasOpen] = useState(open);
  if (open !== wasOpen) {
    setWasOpen(open);
    if (open) {
      setParts(partsOf(birthday));
      setError(undefined);
    }
  }

  const thisYear = new Date().getFullYear();
  const years = Array.from({ length: MAX_AGE - MIN_AGE + 1 }, (_, i) => thisYear - MIN_AGE - i);
  const days = Array.from({ length: parts.month ? daysIn(parts.year, parts.month) : 31 }, (_, i) => i + 1);
  const chosen = parts.day && parts.month ? birthdayOf(parts.year, parts.month, parts.day) : null;
  const age = chosen && isBirthday(chosen) ? ageOn(chosen) : null;

  // A shorter month (or a non-leap February) pulls a too-late day back to its last one.
  const set = (patch: Partial<Parts>) =>
    setParts((p) => {
      const next = { ...p, ...patch };
      if (next.month && next.day > daysIn(next.year, next.month)) next.day = daysIn(next.year, next.month);
      return next;
    });

  const save = async (next: string | null) => {
    setBusy(true);
    setError(undefined);
    try {
      await onSave(next);
      onClose();
    } catch {
      setError("Couldn't save. Try again.");
    } finally {
      setBusy(false);
    }
  };

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (!chosen || !isBirthday(chosen)) return setError("Pick the day and the month.");
    const a = ageOn(chosen);
    if (a < MIN_AGE || a > MAX_AGE) return setError(`That makes you ${a}. Check the year.`);
    void save(chosen);
  };

  const select =
    "h-12 w-full appearance-none rounded-2xl bg-surface px-3 text-center text-base font-medium outline-none focus-visible:ring-2 focus-visible:ring-accent";

  return (
    <dialog
      ref={ref}
      aria-labelledby="birthday-title"
      onCancel={(e) => {
        e.preventDefault();
        if (!busy) onClose();
      }}
      onClick={(e) => {
        if (e.target === e.currentTarget && !busy) onClose();
      }}
      className="confirm m-auto w-[min(24rem,calc(100vw-2rem))] rounded-[2rem] bg-paper p-0 text-ink shadow-2xl ring-1 ring-line"
    >
      <form onSubmit={submit} className="p-7 text-center">
        <h2 id="birthday-title" className="text-xl font-semibold tracking-tight">
          Your birthday
        </h2>

        <div className="mt-6 grid grid-cols-[1fr_1.6fr_1.3fr] gap-2">
          <select aria-label="Day" value={parts.day} onChange={(e) => set({ day: Number(e.target.value) })} className={select}>
            <option value={0} disabled>
              Day
            </option>
            {days.map((d) => (
              <option key={d} value={d}>
                {d}
              </option>
            ))}
          </select>
          <select aria-label="Month" value={parts.month} onChange={(e) => set({ month: Number(e.target.value) })} className={select}>
            <option value={0} disabled>
              Month
            </option>
            {months.map((m, i) => (
              <option key={m} value={i + 1}>
                {m}
              </option>
            ))}
          </select>
          <select aria-label="Year" value={parts.year} onChange={(e) => set({ year: Number(e.target.value) })} className={select}>
            {years.map((y) => (
              <option key={y} value={y}>
                {y}
              </option>
            ))}
          </select>
        </div>

        <p className="mt-4 h-6 text-base font-medium tabular-nums" aria-live="polite">
          {age !== null && age >= 0 ? `You're ${age}` : ""}
        </p>
        <p role="alert" className="h-5 text-sm text-red-600 dark:text-red-400">
          {error}
        </p>

        <div className="mt-3 grid grid-cols-2 gap-3">
          <button type="button" onClick={onClose} disabled={busy} className="rounded-full bg-surface px-5 py-3 text-sm font-medium disabled:opacity-60">
            Cancel
          </button>
          <button type="submit" disabled={busy} className="rounded-full bg-ink px-5 py-3 text-sm font-medium text-paper disabled:opacity-60">
            {busy ? "Saving…" : "Save"}
          </button>
        </div>
        {birthday && (
          <button type="button" onClick={() => void save(null)} disabled={busy} className="mt-4 text-sm text-muted underline-offset-4 hover:text-ink hover:underline">
            Remove my birthday
          </button>
        )}
      </form>
    </dialog>
  );
}
