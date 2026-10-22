"use client";

import { useRef, useState } from "react";
import { DAY } from "@/lib/format";

/**
 * A running total over time (accounts since the first one), one point per day: a 2px ink line over a faint area.
 * One series, so no legend. Hovering shows a crosshair with that day's total and how many were added that day.
 */
export function GrowthLine({ dates, unit }: { dates: (Date | null)[]; unit: [string, string] }) {
  const box = useRef<HTMLDivElement>(null);
  const [hover, setHover] = useState<number | null>(null);

  const days = dates.filter((d): d is Date => !!d).map(startOfDay).sort((a, b) => a - b);
  if (days.length === 0) return <p className="py-16 text-center text-sm text-muted-foreground">Nothing yet.</p>;

  const first = days[0]!;
  const today = startOfDay(new Date());
  const n = Math.max(2, Math.round((today - first) / DAY) + 1);
  const added = Array.from({ length: n }, () => 0);
  for (const d of days) added[Math.min(n - 1, Math.round((d - first) / DAY))]!++;
  const totals: number[] = [];
  added.reduce((sum, a, i) => (totals[i] = sum + a), 0);
  const max = niceMax(totals[n - 1]!);

  const x = (i: number) => (i / (n - 1)) * 100;
  const y = (v: number) => 100 - (v / max) * 100;
  const line = totals.map((v, i) => `${i ? "L" : "M"}${x(i)},${y(v)}`).join("");
  const area = `${line}L100,100L0,100Z`;
  const label = (i: number) => new Date(first + i * DAY).toLocaleDateString("en", { day: "numeric", month: "short", year: "numeric" });
  const words = (v: number) => `${v} ${v === 1 ? unit[0] : unit[1]}`;

  const onMove = (clientX: number) => {
    const r = box.current?.getBoundingClientRect();
    if (!r) return;
    setHover(Math.max(0, Math.min(n - 1, Math.round(((clientX - r.left) / r.width) * (n - 1)))));
  };

  return (
    <div className="flex gap-3">
      <div className="flex h-48 flex-col justify-between text-right text-[11px] text-muted-foreground tabular-nums">
        <span>{max}</span>
        <span>{max / 2}</span>
        <span>0</span>
      </div>
      <div className="min-w-0 flex-1">
        <div
          ref={box}
          className="relative h-48 touch-none"
          onPointerMove={(e) => onMove(e.clientX)}
          onPointerDown={(e) => onMove(e.clientX)}
          onPointerLeave={() => setHover(null)}
          role="img"
          aria-label={`${words(totals[n - 1]!)} in all, from ${label(0)} to today`}
        >
          {[0, 50].map((t) => (
            <div key={t} className="absolute inset-x-0 border-t border-dashed border-border" style={{ top: `${t}%` }} />
          ))}
          <div className="absolute inset-x-0 bottom-0 border-t" />
          <svg viewBox="0 0 100 100" preserveAspectRatio="none" className="absolute inset-0 size-full overflow-visible">
            <path d={area} className="fill-foreground/[0.07]" />
            <path d={line} fill="none" className="stroke-foreground" strokeWidth={2} vectorEffect="non-scaling-stroke" strokeLinejoin="round" />
          </svg>
          {hover !== null && (
            <>
              <div className="pointer-events-none absolute inset-y-0 w-px bg-foreground/30" style={{ left: `${x(hover)}%` }} />
              <div
                className="pointer-events-none absolute size-2.5 -translate-x-1/2 translate-y-1/2 rounded-full bg-foreground ring-2 ring-card"
                style={{ left: `${x(hover)}%`, bottom: `${100 - y(totals[hover]!)}%` }}
              />
              <div
                className="pointer-events-none absolute top-0 z-10 rounded-lg border bg-popover px-2.5 py-1.5 text-xs whitespace-nowrap shadow-sm"
                style={x(hover) > 60 ? { right: `${100 - x(hover)}%`, marginRight: 10 } : { left: `${x(hover)}%`, marginLeft: 10 }}
              >
                <p className="text-muted-foreground">{label(hover)}</p>
                <p className="font-medium tabular-nums">{words(totals[hover]!)}</p>
                {added[hover]! > 0 && <p className="text-muted-foreground tabular-nums">+{added[hover]} that day</p>}
              </div>
            </>
          )}
        </div>
        <div className="mt-2 flex justify-between text-[11px] text-muted-foreground">
          <span>{label(0)}</span>
          <span>Today</span>
        </div>
      </div>
    </div>
  );
}

function startOfDay(d: Date): number {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
}

/** A round top for the scale (2, 4, 6, 8, 10, 20, 40…) at or above `v`, so the halfway label is whole too. */
function niceMax(v: number): number {
  const p = 10 ** Math.floor(Math.log10(Math.max(1, v)));
  return [2, 4, 6, 8, 10].map((m) => m * p).find((m) => m >= v) ?? 20 * p;
}
