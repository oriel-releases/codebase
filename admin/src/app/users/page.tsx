"use client";

import { ArrowDown, ArrowUp, ArrowUpDown, Search } from "lucide-react";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useEffect, useMemo, type ReactNode } from "react";
import { Empty, ErrorNote, PlatformBadge, RefreshButton } from "@/components/bits";
import { PageHeader } from "@/components/shell";
import { PlatformIcon } from "@/components/platform-icon";
import { UserCell } from "@/components/user-cell";
import { Card } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { overview, time, type Account } from "@/lib/data";
import { ageFrom, compareVersions, DAY, platformNames, platformOrder, shortDate, timeAgo, within } from "@/lib/format";
import { useLoad } from "@/lib/use-load";
import { useUser } from "@/lib/use-user";
import { useRemembered, useScrollMemory } from "@/lib/use-remembered";

// The signed-in admin's own row: a warm champagne wash that fades to the right, with the text and badges kept dark and crisp on it.
const mine =
  "bg-gradient-to-r from-[#f3d98b]/35 via-[#f3d98b]/10 to-transparent hover:from-[#f3d98b]/45 hover:via-[#f3d98b]/15 dark:from-[#e0b94d]/20 dark:via-[#e0b94d]/6 " +
  "[&_.text-muted-foreground]:text-foreground/70 [&_[data-slot=badge]]:border-foreground/15 [&_[data-slot=badge]]:bg-background [&_[data-slot=badge]]:text-foreground";

type Field = "seen" | "changed" | "joined" | "age";
type Sort = Field | `${Field}-old`;
type Activity = "any" | "day" | "week" | "month" | "older";

const activityOptions: [Activity, string][] = [
  ["any", "Any time"],
  ["day", "Today"],
  ["week", "7 days"],
  ["month", "30 days"],
  ["older", "Older"],
];

export default function UsersPage() {
  // useSearchParams needs a boundary in a static export.
  return (
    <Suspense>
      <Users />
    </Suspense>
  );
}

function Users() {
  const { data, error, loading, reload } = useLoad(overview);
  const router = useRouter();
  const params = useSearchParams();
  // The signed-in admin's own account gets a highlighted row.
  const me = useUser()?.uid;
  // Remembered, so coming back from an account (or reloading) keeps the list as it was.
  const [search, setSearch] = useRemembered("users.search", "");
  const [platform, setPlatformOnly] = useRemembered("users.platform", "all");
  const [version, setVersion] = useRemembered("users.version", "");
  const [activity, setActivity] = useRemembered<Activity>("users.activity", "any");
  const [sort, setSort] = useRemembered<Sort>("users.sort2", "seen");
  const setPlatform = (v: string) => {
    setPlatformOnly(v);
    setVersion("");
  };

  // A link such as /users?platform=windows&version=1.1.0 (the overview's version badges) sets the filters, the rest cleared.
  const linkedPlatform = params.get("platform");
  const linkedVersion = params.get("version");
  useEffect(() => {
    if (!linkedPlatform) return;
    setPlatformOnly(linkedPlatform);
    setVersion(linkedVersion ?? "");
    setSearch("");
    setActivity("any");
    router.replace("/users", { scroll: false });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [linkedPlatform, linkedVersion]);
  useScrollMemory("users", !!data);

  // The time the list is sorted by (and the "within" filter looks at): last seen or last changed.
  const byChange = sort.startsWith("changed");
  const stamp = (a: { lastSeenAt: Date | null; changedAt: Date | null; createdAt: Date | null }) => (sort.startsWith("joined") ? a.createdAt : byChange ? a.changedAt : a.lastSeenAt);

  // Search and the time window; the version list and the rows both start from the accounts that pass them.
  const passes = (a: Account) => {
    const q = search.trim().toLowerCase();
    if (q && !(a.email.toLowerCase().includes(q) || a.displayName.toLowerCase().includes(q) || a.uid.includes(search.trim()))) return false;
    const at = stamp(a);
    if (activity === "any") return true;
    if (activity === "older") return !at || !within(at, 30 * DAY);
    return within(at, activity === "day" ? DAY : activity === "week" ? 7 * DAY : 30 * DAY);
  };

  // Only versions that some listed account runs on the chosen platform.
  const versions = useMemo(() => {
    if (platform === "all" || !data) return [];
    const found = new Set<string>();
    for (const a of data.accounts) if (passes(a)) for (const s of a.sessions) if (s.platform === platform) found.add(s.appVersion || "?");
    return [...found].sort((a, b) => compareVersions(b, a));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data, platform, search, activity, sort]);
  const activeVersion = platform !== "all" && versions.includes(version) ? version : "";

  const rows = useMemo(() => {
    return (data?.accounts ?? [])
      .filter(passes)
      .filter((a) => platform === "all" || a.sessions.some((s) => s.platform === platform && (!activeVersion || (s.appVersion || "?") === activeVersion)))
      .sort((a, b) => {
        // Accounts without that value (a time, or a birthday for age) go last either way.
        const age = sort.startsWith("age");
        const x = age ? (a.birthday ? ageFrom(a.birthday) + 1 : 0) : time(stamp(a));
        const y = age ? (b.birthday ? ageFrom(b.birthday) + 1 : 0) : time(stamp(b));
        if (!x || !y) return (x ? 0 : 1) - (y ? 0 : 1);
        return sort.endsWith("-old") ? x - y : y - x;
      })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data, search, platform, activeVersion, activity, sort]);
  const filtered = search.trim() !== "" || platform !== "all" || activity !== "any";

  return (
    <>
      <PageHeader
        title="Users"
        actions={<RefreshButton loading={loading} onClick={reload} />}
      />
      {error && <ErrorNote message={error} />}

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <div className="relative w-full sm:w-72">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Name, email or uid" className="rounded-full pl-9" />
        </div>
        <Pick
          label="Platform"
          value={platform}
          onChange={setPlatform}
          options={[["all", "All platforms"], ...platformOrder.map((p): [string, string, ReactNode] => [p, platformNames[p]!, <PlatformIcon key={p} platform={p} />])]}
        />
        {versions.length > 0 && (
          <Pick label="App version" value={activeVersion} onChange={setVersion} options={[["", "Any version"], ...versions.map((v): [string, string] => [v, `Version ${v}`])]} />
        )}
        <Pick
          label="Within"
          value={activity}
          onChange={(v) => setActivity(v as Activity)}
          options={activityOptions}
        />
        {(filtered || activeVersion) && (
          <button
            className="text-sm text-muted-foreground underline-offset-4 hover:text-foreground hover:underline"
            onClick={() => {
              setSearch("");
              setPlatform("all");
              setActivity("any");
            }}
          >
            Clear filters
          </button>
        )}
      </div>

      <Card className="py-0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="pl-4">Account</TableHead>
              <TableHead>Devices</TableHead>
              <SortHead label="Age" field="age" sort={sort} onSort={setSort} />
              <SortHead label="Joined" field="joined" sort={sort} onSort={setSort} />
              <SortHead label="Last seen" field="seen" sort={sort} onSort={setSort} />
              <SortHead label="Last changed" field="changed" sort={sort} onSort={setSort} className="pr-4" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {!data &&
              Array.from({ length: 8 }, (_, i) => (
                <TableRow key={i}>
                  <TableCell colSpan={6} className="px-4">
                    <Skeleton className="h-8" />
                  </TableCell>
                </TableRow>
              ))}
            {rows.map((a) => (
              <TableRow
                key={a.uid}
                className={`cursor-pointer ${a.uid === me ? mine : ""}`}
                onClick={() => router.push(`/user?uid=${a.uid}`)}>
                <TableCell className="max-w-72 pl-4">
                  <UserCell account={a} gold={a.uid === me} />
                </TableCell>
                <TableCell>
                  <div className="flex flex-wrap gap-1">
                    {[...new Set(a.sessions.map((s) => s.platform))].map((p) => (
                      <PlatformBadge key={p} platform={p} />
                    ))}
                  </div>
                </TableCell>
                <TableCell className="tabular-nums">{a.birthday ? ageFrom(a.birthday) : <span className="text-muted-foreground">—</span>}</TableCell>
                <TableCell className="text-muted-foreground">{shortDate(a.createdAt)}</TableCell>
                <TableCell className="text-muted-foreground">{timeAgo(a.lastSeenAt)}</TableCell>
                <TableCell className="pr-4 text-muted-foreground">{timeAgo(a.changedAt)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        {data && rows.length === 0 && <Empty>Nobody matches.</Empty>}
      </Card>
    </>
  );
}

/** A rounded dropdown; Radix items can't have an empty value, so "" travels as a placeholder. */
function Pick({ label, value, onChange, options }: { label: string; value: string; onChange: (value: string) => void; options: [string, string, ReactNode?][] }) {
  const blank = "__blank";
  return (
    <Select value={value || blank} onValueChange={(v) => onChange(v === blank ? "" : v)}>
      <SelectTrigger aria-label={label} title={label}>
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {options.map(([v, text, icon]) => (
          <SelectItem key={v} value={v || blank}>
            {icon}
            {text}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

/** A column header that sorts by its time: first click newest first, again for oldest first. */
function SortHead({ label, field, sort, onSort, className }: { label: string; field: Field; sort: Sort; onSort: (sort: Sort) => void; className?: string }) {
  const active = sort === field || sort === `${field}-old`;
  const oldest = sort === `${field}-old`;
  return (
    <TableHead className={className}>
      <button
        className={`inline-flex items-center gap-1 hover:text-foreground ${active ? "text-foreground" : ""}`}
        onClick={() => onSort(active && !oldest ? (`${field}-old` as Sort) : field)}
        title={field === "age" ? (active && !oldest ? "Youngest first" : "Oldest first") : active && !oldest ? "Show the oldest first" : "Show the newest first"}
      >
        {label}
        {active ? oldest ? <ArrowUp className="size-3.5" /> : <ArrowDown className="size-3.5" /> : <ArrowUpDown className="size-3.5 opacity-40" />}
      </button>
    </TableHead>
  );
}
