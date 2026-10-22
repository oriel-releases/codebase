"use client";

import { useState } from "react";
import { DAY } from "@/lib/format";
import { cn } from "@/lib/utils";

type Props = {
  /** One date per event (a sign-up); counted per local day. */
  dates: (Date | null)[];
  days?: number;
  /** "new account" / "new accounts". */
  unit: [string, string];
};

/**
 * One bar per day for the last `days` days, ink on paper like the rest of the site. A single series, so no legend:
 * the card's title names it. Hovering (or focusing) a day shows its count; the busiest day's value is labelled.
 */
export function DailyBars({ dates, days = 30, unit }: Props) {
  const [hover, setHover] = useState<number | null>(null);
  const today = startOfDay(new Date());
  const counts = Array.from({ length: days }, () => 0);
  for (const d of dates) {
    if (!d) continue;
    const ago = Math.round((today - startOfDay(d)) / DAY);
    if (ago >= 0 && ago < days) counts[days - 1 - ago]!++;
  }
  const max = Math.max(1, ...counts);
  const peak = counts.indexOf(Math.max(...counts));
  const dayOf = (i: number) => new Date(today - (days - 1 - i) * DAY);
  const label = (i: number) => dayOf(i).toLocaleDateString("en", { day: "numeric", month: "short" });
  const words = (n: number) => `${n} ${n === 1 ? unit[0] : unit[1]}`;
  const shown = hover ?? null;

  return (
    <div>
      <div className="relative flex h-40 items-end gap-0.5 border-b" onMouseLeave={() => setHover(null)} role="list">
        {counts.map((n, i) => (
          <div
            key={i}
            role="listitem"
            tabIndex={0}
            aria-label={`${label(i)}: ${words(n)}`}
            onMouseEnter={() => setHover(i)}
            onFocus={() => setHover(i)}
            onBlur={() => setHover(null)}
            className="group relative flex h-full flex-1 items-end outline-none"
          >
            <div
              className={cn(
                "w-full rounded-t-[4px] transition-opacity",
                n ? "bg-foreground" : "h-0.5 rounded-none bg-border",
                shown !== null && shown !== i && "opacity-35",
              )}
              style={n ? { height: `${(n / max) * 100}%` } : undefined}
            />
            {i === peak && n > 0 && shown === null && (
              <span className="absolute left-1/2 -translate-x-1/2 text-[11px] text-muted-foreground tabular-nums" style={{ bottom: `calc(${(n / max) * 100}% + 4px)` }}>
                {n}
              </span>
            )}
          </div>
        ))}
        {shown !== null && (
          <div
            className="pointer-events-none absolute -top-2 z-10 -translate-x-1/2 -translate-y-full rounded-lg border bg-popover px-2.5 py-1.5 text-xs whitespace-nowrap shadow-sm"
            style={{ left: `${((shown + 0.5) / days) * 100}%` }}
          >
            <p className="text-muted-foreground">{label(shown)}</p>
            <p className="font-medium tabular-nums">{words(counts[shown]!)}</p>
          </div>
        )}
      </div>
      <div className="mt-2 flex justify-between text-[11px] text-muted-foreground">
        <span>{label(0)}</span>
        <span>{label(Math.floor(days / 2))}</span>
        <span>Today</span>
      </div>
    </div>
  );
}

function startOfDay(d: Date): number {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
}
