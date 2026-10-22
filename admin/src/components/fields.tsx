import type { ReactNode } from "react";
import { Badge } from "@/components/ui/badge";
import { dateTime, timeAgo } from "@/lib/format";

/**
 * Every field of a document as it is stored, nothing left out: dates in words, links clickable, pictures shown,
 * lists and maps nested. `names` turns ids (a device's session id) into something readable.
 */
export function FieldList({ data, names = {} }: { data: Record<string, unknown>; names?: Record<string, string> }) {
  const keys = Object.keys(data).sort();
  if (keys.length === 0) return <p className="text-sm text-muted-foreground">Empty.</p>;
  return (
    <dl className="divide-y text-sm">
      {keys.map((k) => (
        <div key={k} className="grid gap-1 py-2 sm:grid-cols-[11rem_1fr] sm:gap-4">
          <dt className="font-mono text-xs text-muted-foreground sm:pt-0.5">{k}</dt>
          <dd className="min-w-0 break-words">
            <Value value={data[k]} field={k} names={names} />
          </dd>
        </div>
      ))}
    </dl>
  );
}

export function Value({ value, field = "", names = {} }: { value: unknown; field?: string; names?: Record<string, string> }): ReactNode {
  if (value === null || value === undefined) return <span className="text-muted-foreground">null</span>;
  if (value instanceof Date) return <When date={value} />;
  if (typeof value === "boolean") return <Badge variant={value ? "default" : "outline"}>{value ? "true" : "false"}</Badge>;
  if (typeof value === "number") {
    // Realtime Database times are milliseconds (at, settingsAt…).
    if (/(At|^at)$/.test(field) && value > 1e12) return <When date={new Date(value)} />;
    return <span className="tabular-nums">{value}</span>;
  }
  if (typeof value === "string") {
    if (names[value]) return <span title={value}>{names[value]}</span>;
    if (/^#[0-9a-f]{6}$/i.test(value))
      return (
        <span className="inline-flex items-center gap-1.5 font-mono">
          <span className="size-3 rounded-full ring-1 ring-border" style={{ background: value }} /> {value}
        </span>
      );
    if (/^https?:\/\//.test(value)) {
      const picture = /^https:\/\/(images\.unsplash\.com|media\.pollinations\.ai|lh3\.googleusercontent\.com)\//.test(value);
      return (
        <a href={value} target="_blank" rel="noreferrer" className="inline-flex max-w-full items-center gap-2 underline-offset-2 hover:underline">
          {picture && <img src={value.includes("unsplash") ? `${value}${value.includes("?") ? "&" : "?"}w=120&q=60` : value} alt="" className="h-8 w-12 shrink-0 rounded object-cover" referrerPolicy="no-referrer" />}
          <span className="truncate">{value}</span>
        </a>
      );
    }
    return value === "" ? <span className="text-muted-foreground">{'""'}</span> : <span className="whitespace-pre-wrap">{value}</span>;
  }
  if (Array.isArray(value)) {
    if (value.length === 0) return <span className="text-muted-foreground">[ ]</span>;
    return (
      <ol className="space-y-1.5">
        {value.map((v, i) => (
          <li key={i} className="flex gap-2">
            <span className="w-5 shrink-0 text-right font-mono text-xs text-muted-foreground">{i}</span>
            <div className="min-w-0 flex-1">
              {v && typeof v === "object" && !(v instanceof Date) ? <Nested data={v as Record<string, unknown>} names={names} /> : <Value value={v} names={names} />}
            </div>
          </li>
        ))}
      </ol>
    );
  }
  if (typeof value === "object") return <Nested data={value as Record<string, unknown>} names={names} />;
  return String(value);
}

function Nested({ data, names }: { data: Record<string, unknown>; names: Record<string, string> }) {
  return (
    <div className="rounded-lg border bg-secondary/40 px-3">
      <FieldList data={data} names={names} />
    </div>
  );
}

function When({ date }: { date: Date }) {
  return (
    <span title={date.toISOString()}>
      {dateTime(date)} <span className="text-muted-foreground">· {timeAgo(date)}</span>
    </span>
  );
}
