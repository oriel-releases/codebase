import type { ReactNode } from "react";

export type BarRow = { key: string; label: ReactNode; value: number; /** A darker part of the bar (e.g. "seen this week"). */ part?: number };

/**
 * Horizontal bars on one scale, ink on paper, each with its value and share written out (so nothing relies on the
 * bar's length alone). A `part` draws the darker inner bar; without it the whole bar is ink.
 */
export function BarList({ rows, total, labelWidth = "7.5rem" }: { rows: BarRow[]; total?: number; labelWidth?: string }) {
  const max = Math.max(1, ...rows.map((r) => r.value));
  const sum = total ?? rows.reduce((s, r) => s + r.value, 0);
  return (
    <div className="space-y-3.5">
      {rows.map((r) => (
        <div key={r.key} className="grid items-center gap-3 text-sm" style={{ gridTemplateColumns: `${labelWidth} 1fr 5.5rem` }}>
          <span className="flex min-w-0 items-center gap-2 truncate">{r.label}</span>
          <div className="relative h-2.5 rounded-full bg-secondary">
            {r.part === undefined ? (
              <div className="absolute inset-y-0 left-0 rounded-full bg-foreground" style={{ width: `${(r.value / max) * 100}%` }} />
            ) : (
              <>
                <div className="absolute inset-y-0 left-0 rounded-full bg-foreground/20" style={{ width: `${(r.value / max) * 100}%` }} />
                <div className="absolute inset-y-0 left-0 rounded-full bg-foreground" style={{ width: `${(r.part / max) * 100}%` }} />
              </>
            )}
          </div>
          <span className="text-right tabular-nums">
            {r.part !== undefined ? (
              <>
                {r.part}
                <span className="text-muted-foreground"> / {r.value}</span>
              </>
            ) : (
              <>
                {r.value}
                <span className="text-muted-foreground"> · {sum ? Math.round((r.value / sum) * 100) : 0}%</span>
              </>
            )}
          </span>
        </div>
      ))}
    </div>
  );
}
