"use client";

import { Activity, CalendarCheck, Layers, Link2, MonitorSmartphone, Share2, UserPlus, Users } from "lucide-react";
import Link from "next/link";
import type { ReactNode } from "react";
import { BarList } from "@/components/bar-list";
import { ErrorNote, RefreshButton, Stat } from "@/components/bits";
import { DailyBars } from "@/components/daily-bars";
import { GrowthLine } from "@/components/growth-line";
import { PlatformIcon } from "@/components/platform-icon";
import { PageHeader } from "@/components/shell";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { overview, type Overview, type Session } from "@/lib/data";
import { ageFrom, compareVersions, DAY, platformNames, platformOrder, within } from "@/lib/format";
import { useLoad } from "@/lib/use-load";
import { useRemembered } from "@/lib/use-remembered";

const ranges = [7, 30, 90] as const;
type Range = (typeof ranges)[number];

export default function OverviewPage() {
  const { data, error, loading, reload } = useLoad(overview);
  const [range, setRange] = useRemembered<Range>("overview.range", 30);

  return (
    <>
      <PageHeader
        title="Overview"
        actions={
          <div className="flex items-center gap-2">
            <Select value={String(range)} onValueChange={(v) => setRange(Number(v) as Range)}>
              <SelectTrigger size="sm" aria-label="Range">
                <SelectValue />
              </SelectTrigger>
              <SelectContent align="end">
                {ranges.map((r) => (
                  <SelectItem key={r} value={String(r)}>
                    {r} days
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <RefreshButton loading={loading} onClick={reload} />
          </div>
        }
      />
      {error && <ErrorNote message={error} />}
      {data ? <Dashboard data={data} range={range} /> : !error && <Loading />}
    </>
  );
}

function Dashboard({ data, range }: { data: Overview; range: Range }) {
  const { accounts, sessions } = data;
  const activeIn = (ms: number) => accounts.filter((a) => within(a.lastSeenAt, ms)).length;
  const newIn = (ms: number) => accounts.filter((a) => within(a.createdAt, ms)).length;
  const multi = accounts.filter((a) => new Set(a.sessions.map((s) => s.platform)).size >= 2).length;
  const pct = (n: number) => (accounts.length ? `${Math.round((n / accounts.length) * 100)}% of accounts` : "");
  const newDevices = sessions.filter((s) => within(s.createdAt, range * DAY)).length;

  return (
    <div className="space-y-8">
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Stat icon={Users} label="Accounts" value={accounts.length} hint={`${activeIn(30 * DAY)} seen in 30 days`} />
        <Stat icon={UserPlus} label="New this week" value={newIn(7 * DAY)} hint={`${newIn(30 * DAY)} in 30 days`} />
        <Stat icon={Activity} label="Active today" value={activeIn(DAY)} hint={pct(activeIn(DAY))} />
        <Stat icon={CalendarCheck} label="Active this week" value={activeIn(7 * DAY)} hint={pct(activeIn(7 * DAY))} />
        <Stat
          icon={MonitorSmartphone}
          label="Signed-in devices"
          value={sessions.length}
          hint={accounts.length ? `${(sessions.length / accounts.length).toFixed(1)} per account` : undefined}
        />
        <Stat icon={Layers} label="On 2+ platforms" value={multi} hint={pct(multi)} />
        <Stat icon={Share2} label="Shares" value={data.shares} hint="wallpapers sent by link" />
        <Stat icon={Link2} label="Follow codes" value={data.codes} hint="accounts that can be followed" />
      </div>

      <div className="grid gap-3 lg:grid-cols-[3fr_2fr]">
        <ChartCard title="Accounts over time">
          <GrowthLine dates={accounts.map((a) => a.createdAt)} unit={["account", "accounts"]} />
        </ChartCard>
        <ChartCard title="Last seen">
          <BarList rows={lastSeenRows(data)} total={accounts.length} />
        </ChartCard>
      </div>

      <div className="grid gap-3 lg:grid-cols-2">
        <ChartCard title="New accounts">
          <DailyBars dates={accounts.map((a) => a.createdAt)} days={range} unit={["new account", "new accounts"]} />
        </ChartCard>
        <ChartCard title="New devices">
          <DailyBars dates={sessions.map((s) => s.createdAt)} days={range} unit={["new device", "new devices"]} />
        </ChartCard>
      </div>

      <div className="grid gap-3 lg:grid-cols-3">
        <PlatformMix sessions={sessions} />
        <ChartCard title="Devices per account">
          <BarList rows={devicesPerAccountRows(data)} total={accounts.length} />
        </ChartCard>
        <ChartCard title="Ages">
          <BarList rows={ageRows(data)} total={accounts.filter((a) => a.birthday).length} />
        </ChartCard>
      </div>

      <section>
        <h2 className="mb-1 text-lg font-semibold tracking-tight">App versions</h2>
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          {platformOrder
            .filter((p) => p !== "web")
            .map((p) => (
              <VersionCard key={p} platform={p} sessions={sessions.filter((s) => s.platform === p)} published={data.versions[p]} />
            ))}
        </div>
      </section>
    </div>
  );
}

function ChartCard({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>{title}</CardTitle>
      </CardHeader>
      <CardContent className="pt-4">{children}</CardContent>
    </Card>
  );
}

/** Accounts by how recently any of their devices checked in. */
function lastSeenRows({ accounts }: Overview) {
  const now = Date.now();
  const buckets: [string, string, (ago: number) => boolean][] = [
    ["today", "Today", (ago) => ago <= DAY],
    ["week", "This week", (ago) => ago > DAY && ago <= 7 * DAY],
    ["month", "This month", (ago) => ago > 7 * DAY && ago <= 30 * DAY],
    ["older", "Longer ago", (ago) => ago > 30 * DAY],
  ];
  const rows = buckets.map(([key, label, test]) => ({
    key,
    label,
    value: accounts.filter((a) => a.lastSeenAt && test(now - a.lastSeenAt.getTime())).length,
  }));
  const never = accounts.filter((a) => !a.lastSeenAt).length;
  return never ? [...rows, { key: "never", label: "Never", value: never }] : rows;
}

/** Accounts with a birthday, by age group; the share is of those that set one. */
function ageRows({ accounts }: Overview) {
  const ages = accounts.filter((a) => a.birthday).map((a) => ageFrom(a.birthday));
  const groups: [string, number, number][] = [
    ["Under 18", 0, 17],
    ["18–24", 18, 24],
    ["25–34", 25, 34],
    ["35–44", 35, 44],
    ["45–54", 45, 54],
    ["55 or older", 55, 200],
  ];
  return groups.map(([label, from, to]) => ({ key: label, label, value: ages.filter((n) => n >= from && n <= to).length }));
}

/** Accounts by their number of signed-in devices: none, 1, 2, 3, 4 or more. */
function devicesPerAccountRows({ accounts }: Overview) {
  const label = (n: number) => (n === 0 ? "None" : n >= 4 ? "4 or more" : `${n} device${n === 1 ? "" : "s"}`);
  return [0, 1, 2, 3, 4]
    .map((n) => ({
      key: String(n),
      label: label(n),
      value: accounts.filter((a) => (n === 4 ? a.sessions.length >= 4 : a.sessions.length === n)).length,
    }))
    .filter((r) => r.key !== "0" || r.value > 0);
}

function PlatformMix({ sessions }: { sessions: Session[] }) {
  const rows = platformOrder
    .map((p) => {
      const own = sessions.filter((s) => s.platform === p);
      return {
        key: p,
        label: (
          <>
            <PlatformIcon platform={p} />
            {platformNames[p]}
          </>
        ),
        value: own.length,
        part: own.filter((s) => within(s.lastSeenAt, 7 * DAY)).length,
      };
    })
    .sort((a, b) => b.value - a.value);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Devices by platform</CardTitle>
        <CardDescription className="flex flex-wrap items-center gap-3">
          <span className="flex items-center gap-1.5">
            <span className="size-2.5 rounded-sm bg-foreground" /> seen this week
          </span>
          <span className="flex items-center gap-1.5">
            <span className="size-2.5 rounded-sm bg-foreground/20" /> all signed in
          </span>
        </CardDescription>
      </CardHeader>
      <CardContent className="pt-4">
        <BarList rows={rows} />
      </CardContent>
    </Card>
  );
}

/** One platform: how many devices run the latest release, and every version in use (old ones stand out). */
function VersionCard({ platform, sessions, published }: { platform: string; sessions: Session[]; published?: string }) {
  const versions = new Map<string, number>();
  for (const s of sessions) versions.set(s.appVersion || "?", (versions.get(s.appVersion || "?") ?? 0) + 1);
  const sorted = [...versions].sort((a, b) => compareVersions(b[0], a[0]));
  const outdated = (v: string) => !!published && v !== "?" && compareVersions(v, published) < 0;
  const current = published ? sessions.filter((s) => s.appVersion && compareVersions(s.appVersion, published) >= 0).length : 0;
  const share = sessions.length ? current / sessions.length : 0;

  return (
    <Card className="gap-4">
      <CardHeader className="flex flex-row items-center justify-between">
        <CardTitle className="flex items-center gap-2">
          <span className="grid size-8 place-items-center rounded-full bg-secondary">
            <PlatformIcon platform={platform} />
          </span>
          {platformNames[platform]}
        </CardTitle>
        {published ? <Badge variant="outline">Latest {published}</Badge> : <Badge variant="outline">Store</Badge>}
      </CardHeader>
      <CardContent className="space-y-4">
        {sessions.length === 0 ? (
          <p className="text-sm text-muted-foreground">No devices yet.</p>
        ) : (
          <>
            {published && (
              <div>
                <div className="mb-1.5 flex justify-between text-sm">
                  <span className="text-muted-foreground">On the latest</span>
                  <span className="tabular-nums">
                    {current} of {sessions.length} · {Math.round(share * 100)}%
                  </span>
                </div>
                <div className="h-2 rounded-full bg-secondary">
                  <div className="h-full rounded-full bg-foreground" style={{ width: `${share * 100}%` }} />
                </div>
              </div>
            )}
            <div className="flex flex-wrap gap-1.5">
              {sorted.map(([v, n]) => (
                <Badge key={v} asChild variant={outdated(v) ? "destructive" : "secondary"} className="font-normal tabular-nums hover:opacity-80">
                  <Link href={`/users?platform=${platform}&version=${encodeURIComponent(v)}`} title={`Accounts on ${platformNames[platform]} ${v}`}>
                    {v} · {n}
                  </Link>
                </Badge>
              ))}
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}

function Loading() {
  return (
    <div className="space-y-8">
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {Array.from({ length: 8 }, (_, i) => (
          <Skeleton key={i} className="h-28 rounded-xl" />
        ))}
      </div>
      {Array.from({ length: 3 }, (_, i) => (
        <div key={i} className="grid gap-3 lg:grid-cols-2">
          <Skeleton className="h-72 rounded-xl" />
          <Skeleton className="h-72 rounded-xl" />
        </div>
      ))}
    </div>
  );
}
