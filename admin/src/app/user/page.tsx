"use client";

import { ArrowLeft, Bell, ChevronRight, Copy, Database, ExternalLink, FolderOpen, Flame, Lock, Puzzle, Star, Users } from "lucide-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useMemo, useState, type ReactNode } from "react";
import { Empty, ErrorNote, PlatformBadge, RefreshButton } from "@/components/bits";
import { FieldList, Value } from "@/components/fields";
import { LazySection } from "@/components/lazy-section";
import { SiteIcon } from "@/components/site-icon";
import { PageHeader } from "@/components/shell";
import { UserCell } from "@/components/user-cell";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";
import {
  accountBasics,
  accountExtension,
  accountFavorites,
  accountLive,
  accountSocial,
  overview,
  photoFromPath,
  thumb,
  toDate,
  type Account,
  type AccountBasics,
  type AccountSocial,
  type Doc,
  type Raw,
} from "@/lib/data";
import { ageFrom, birthdayText, dateTime, shortDate, timeAgo } from "@/lib/format";
import { useLoad } from "@/lib/use-load";
import { useRemembered } from "@/lib/use-remembered";

export default function UserPage() {
  // A static export has no page per account: the uid rides in the query (?uid=...).
  return (
    <Suspense fallback={<UserSkeleton />}>
      <UserView />
    </Suspense>
  );
}

const s = (v: unknown) => (typeof v === "string" ? v : "");
const date = (v: unknown) => (v instanceof Date ? v : toDate(v));

function UserView() {
  const router = useRouter();
  const uid = useSearchParams().get("uid") ?? "";
  const accounts = useLoad(overview);
  const basics = useLoad((refresh) => accountBasics(uid, refresh), [uid]);
  // Refresh reads this page's basics again, and every section that is open.
  const [round, setRound] = useState(0);
  const account = accounts.data?.accounts.find((a) => a.uid === uid);
  const people = useMemo(() => new Map(accounts.data?.accounts.map((a) => [a.uid, a]) ?? []), [accounts.data]);
  const b = basics.data;
  // Session ids (the `device` of a slot, a ring, a sync write) shown as the device's name.
  const names = useMemo(
    () => Object.fromEntries((b?.sessions ?? []).map((x) => [x.id, s(x.data.deviceName) || s(x.data.platform)])),
    [b],
  );

  return (
    <>
      <Button asChild variant="ghost" size="sm" className="mb-4 -ml-2">
        <Link
          href="/users"
          onClick={(e) => {
            // Back keeps the list's filters (they live in its URL); a page opened directly goes to the plain list.
            if (window.history.length > 1 && document.referrer.startsWith(window.location.origin)) {
              e.preventDefault();
              router.back();
            }
          }}
        >
          <ArrowLeft /> Users
        </Link>
      </Button>
      <PageHeader
        title={account ? account.displayName || account.email : "Account"}
        actions={
          <div className="flex items-center gap-2">
            <Button asChild variant="secondary" size="sm">
              <a href={`https://console.firebase.google.com/project/oriel-gcp/firestore/databases/-default-/data/~2Fusers~2F${uid}`} target="_blank" rel="noreferrer">
                <Flame /> Firebase
              </a>
            </Button>
            <RefreshButton
              loading={basics.loading}
              onClick={() => {
                basics.reload();
                setRound((r) => r + 1);
              }}
            />
          </div>
        }
      />
      {(accounts.error ?? basics.error) && <ErrorNote message={(accounts.error ?? basics.error)!} />}
      {accounts.data && !account && <Empty>No account with this uid.</Empty>}
      {!accounts.data && !accounts.error && <UserSkeleton header={false} />}
      {account && (
        <div className="space-y-4">
          <Profile account={account} basics={b} />
          {!b ? (
            <DevicesSkeleton />
          ) : (
            <div className="grid gap-4 xl:grid-cols-[1fr_380px]">
              <Devices sessions={b.sessions} />
              <div className="min-w-0 space-y-4">
                <Wallpaper doc={b.current} />
                <Schedule doc={b.settings} />
              </div>
            </div>
          )}

          <LazySection title="Browser extension" icon={Puzzle} load={(r) => accountExtension(uid, r)} round={round}>
            {(doc) => <Extension doc={doc} names={names} />}
          </LazySection>
          <LazySection title="Favorites" icon={Star} load={(r) => accountFavorites(uid, r)} round={round}>
            {(docs) => <Favorites docs={docs} />}
          </LazySection>
          <LazySection title="Friends & sharing" icon={Users} load={(r) => accountSocial(uid, r)} round={round}>
            {(social) => <Social social={social} people={people} />}
          </LazySection>
          <LazySection title="Live sync" icon={Bell} load={(r) => accountLive(uid, r)} round={round}>
            {(live) => (
              <div className="grid gap-6 md:grid-cols-2">
                <div>
                  <h3 className="mb-1 text-sm font-medium">Doorbell</h3>
                  {live.pulseError ? (
                    <p className="text-sm text-destructive">{live.pulseError}</p>
                  ) : live.pulse ? (
                    <FieldList data={live.pulse} names={names} />
                  ) : (
                    <p className="text-sm text-muted-foreground">Never rang.</p>
                  )}
                </div>
                <div>
                  <h3 className="mb-1 text-sm font-medium">Last claimed change</h3>
                  {live.slot ? <FieldList data={live.slot.data} names={names} /> : <p className="text-sm text-muted-foreground">None.</p>}
                </div>
              </div>
            )}
          </LazySection>
          <LazySection
            title="All data"
            icon={Database}
            load={async (r) => {
              const [ext, fav, social, live] = await Promise.all([accountExtension(uid, r), accountFavorites(uid, r), accountSocial(uid, r), accountLive(uid, r)]);
              const docs = [b?.profile, b?.current, b?.settings, ext, live.slot, ...(b?.sessions ?? []), ...social.followers, ...social.following.map((f) => f.doc), ...social.codes, ...social.shares, ...fav];
              return { docs: docs.filter((d): d is Doc => !!d), pulse: live.pulse };
            }}
            round={round}
          >
            {(all) => <AllData docs={all.docs} pulse={all.pulse} names={names} />}
          </LazySection>
        </div>
      )}
    </>
  );
}

/** The account page's shape while it loads: profile card, devices beside wallpaper and schedule, then the folded sections. */
function UserSkeleton({ header = true }: { header?: boolean }) {
  return (
    <div className="space-y-4">
      {header && (
        <>
          <Skeleton className="mb-4 h-8 w-24" />
          <div className="mb-6 space-y-2">
            <Skeleton className="h-8 w-56" />
            <Skeleton className="h-4 w-40" />
          </div>
        </>
      )}
      <Skeleton className="h-24 rounded-xl" />
      <DevicesSkeleton />
      {Array.from({ length: 5 }, (_, i) => (
        <Skeleton key={i} className="h-16 rounded-xl" />
      ))}
    </div>
  );
}

function DevicesSkeleton() {
  return (
    <div className="grid gap-4 xl:grid-cols-[1fr_380px]">
      <Skeleton className="h-64 rounded-xl" />
      <div className="space-y-4">
        <Skeleton className="aspect-video w-full rounded-xl" />
        <Skeleton className="h-28 rounded-xl" />
      </div>
    </div>
  );
}

function Profile({ account, basics }: { account: Account; basics?: AccountBasics }) {
  const birthday = s(basics?.profile?.data.birthday);
  return (
    <Card>
      <CardContent className="flex flex-wrap items-center gap-x-10 gap-y-4">
        <UserCell account={account} size="size-12" />
        <Fact label="Joined">{shortDate(account.createdAt)}</Fact>
        <Fact label="Last seen">{timeAgo(account.lastSeenAt)}</Fact>
        {birthday && (
          <Fact label="Age">
            {ageFrom(birthday)} <span className="text-muted-foreground">· {birthdayText(birthday)}</span>
          </Fact>
        )}
        <button
          className="ml-auto flex items-center gap-1.5 font-mono text-xs text-muted-foreground hover:text-foreground"
          onClick={() => void navigator.clipboard.writeText(account.uid)}
          title="Copy the uid"
        >
          {account.uid} <Copy className="size-3" />
        </button>
      </CardContent>
    </Card>
  );
}

function Devices({ sessions }: { sessions: Doc[] }) {
  return (
    <Card className="gap-2 self-start pb-0">
      <CardHeader>
        <CardTitle>Devices · {sessions.length}</CardTitle>
      </CardHeader>
      {sessions.length === 0 ? (
        <Empty>No signed-in devices.</Empty>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="pl-6">Platform</TableHead>
              <TableHead>Device</TableHead>
              <TableHead>Version</TableHead>
              <TableHead className="pr-6">Last seen</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {sessions.map((x) => (
              <TableRow key={x.id}>
                <TableCell className="pl-6">
                  <PlatformBadge platform={s(x.data.platform)} />
                </TableCell>
                <TableCell className="max-w-56 truncate">{s(x.data.deviceName) || "—"}</TableCell>
                <TableCell className="tabular-nums">{s(x.data.appVersion) || "—"}</TableCell>
                <TableCell className="pr-6 text-muted-foreground">{timeAgo(date(x.data.lastSeenAt))}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </Card>
  );
}

function Wallpaper({ doc }: { doc: Doc | null }) {
  const c = doc?.data;
  if (!c) {
    return (
      <Card>
        <CardContent>
          <Empty>No wallpaper synced yet.</Empty>
        </CardContent>
      </Card>
    );
  }
  return (
    <Card className="gap-3 overflow-hidden pt-0">
      <img src={thumb(s(c.imageUrl), 760)} alt={s(c.description)} className="aspect-video w-full object-cover" style={{ background: s(c.color) || undefined }} />
      <CardContent className="flex items-center gap-2">
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-medium">{s(c.photographer) || "AI picture"}</p>
          <p className="flex items-center gap-1.5 text-xs text-muted-foreground">
            <PlatformBadge platform={s(c.source)} /> {timeAgo(date(c.updatedAt))}
          </p>
        </div>
        {s(c.pageUrl) && (
          <Button asChild variant="ghost" size="icon">
            <a href={s(c.pageUrl)} target="_blank" rel="noreferrer" aria-label="Open on Unsplash">
              <ExternalLink />
            </a>
          </Button>
        )}
      </CardContent>
    </Card>
  );
}

function Schedule({ doc }: { doc: Doc | null }) {
  const st = doc?.data;
  if (!st) return null;
  const interests = Array.isArray(st.interests) ? (st.interests as { query?: string; enabled?: boolean }[]).filter((i) => i.enabled !== false) : [];
  const interval = Number(st.intervalMinutes ?? 0);
  return (
    <Card className="gap-3">
      <CardHeader className="flex flex-row items-center justify-between">
        <CardTitle>Schedule</CardTitle>
        {st.paused === true && (
          <Badge variant="secondary">
            <Lock /> Locked
          </Badge>
        )}
      </CardHeader>
      <CardContent className="space-y-3">
        <p className="text-sm">
          {s(st.dailyTime) || "—"}
          {interval ? `, then every ${interval % 60 ? `${interval} min` : `${interval / 60} h`}` : ", once a day"}
          <span className="text-muted-foreground"> · {s(st.timeZone)}</span>
        </p>
        {interests.length > 0 && (
          <div className="flex flex-wrap gap-1.5">
            {interests.map((i, n) => (
              <Badge key={n} variant="secondary">
                {i.query}
              </Badge>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
}

/** The browser extension's own settings and new tab shortcuts (users/{uid}/sync/extension). */
function Extension({ doc, names }: { doc: Doc | null; names: Record<string, string> }) {
  if (!doc) return <p className="text-sm text-muted-foreground">The extension never synced for this account.</p>;
  // Links are {title, url}; folders (extension 1.0.5+) are {title, items: [{title, url}]}.
  const shortcuts = Array.isArray(doc.data.shortcuts) ? (doc.data.shortcuts as ShortcutDoc[]) : [];
  const links = shortcuts.filter((x) => !Array.isArray(x.items));
  const folders = shortcuts.filter((x) => Array.isArray(x.items));
  return (
    <div className="space-y-4">
      <p className="text-xs text-muted-foreground">
        Synced {timeAgo(date(doc.data.updatedAt))} from <Value value={doc.data.device} names={names} />
      </p>
      {shortcuts.length === 0 ? (
        <p className="text-sm text-muted-foreground">No shortcuts.</p>
      ) : (
        <div className="space-y-3">
          <p className="text-sm text-muted-foreground">
            {shortcuts.length} in the row{folders.length ? ` · ${folders.length} ${folders.length === 1 ? "folder" : "folders"} holding ${folders.reduce((n, f) => n + f.items!.length, 0)}` : ""}
          </p>
          {links.length > 0 && (
            <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
              {links.map((x, i) => (
                <ShortcutLink key={i} title={x.title} url={x.url} />
              ))}
            </div>
          )}
          {folders.map((f, i) => (
            <div key={i} className="rounded-xl border bg-secondary/30 p-3">
              <p className="mb-2 flex items-center gap-2 text-sm font-medium">
                <FolderOpen className="size-4 text-muted-foreground" />
                {f.title || "Folder"}
                <span className="font-normal text-muted-foreground">· {f.items!.length}</span>
              </p>
              <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
                {f.items!.map((x, k) => (
                  <ShortcutLink key={k} title={x.title} url={x.url} />
                ))}
              </div>
            </div>
          ))}
        </div>
      )}
      <Folded label="Settings">
        <FieldList data={(doc.data.settings ?? {}) as Raw} names={names} />
      </Folded>
    </div>
  );
}

type ShortcutDoc = { title?: string; url?: string; items?: { title?: string; url?: string }[] };

function ShortcutLink({ title, url }: { title?: string; url?: string }) {
  return (
    <a href={url} target="_blank" rel="noreferrer" className="flex min-w-0 items-center gap-3 rounded-lg border bg-card px-3 py-2 hover:bg-secondary">
      <SiteIcon url={url} label={title || host(url)} />
      <span className="min-w-0">
        <span className="block truncate text-sm">{title || host(url)}</span>
        <span className="block truncate text-xs text-muted-foreground">{host(url)}</span>
      </span>
    </a>
  );
}

function host(url?: string) {
  try {
    return url ? new URL(url).hostname.replace(/^www\./, "") : "";
  } catch {
    return url ?? "";
  }
}

function Favorites({ docs }: { docs: Doc[] }) {
  const [show, setShow] = useRemembered<"starred" | "all">("user.favorites", "starred");
  const starred = docs.filter((f) => f.data.favorite === true);
  const rows = show === "all" ? docs : starred;
  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between gap-3">
        <p className="text-sm text-muted-foreground">
          {starred.length} starred{docs.length > starred.length ? `, ${docs.length - starred.length} un-starred` : ""}
        </p>
        {docs.length > starred.length && (
          <ToggleGroup type="single" value={show} onValueChange={(v) => v && setShow(v as "starred" | "all")} variant="outline" size="sm">
            <ToggleGroupItem value="starred">Starred</ToggleGroupItem>
            <ToggleGroupItem value="all">All</ToggleGroupItem>
          </ToggleGroup>
        )}
      </div>
      {rows.length === 0 ? (
        <p className="text-sm text-muted-foreground">No favorites.</p>
      ) : (
        <div className="grid grid-cols-3 gap-2 sm:grid-cols-5 lg:grid-cols-8">
          {rows.map((f) => (
            <a key={f.id} href={s(f.data.pageUrl) || s(f.data.imageUrl)} target="_blank" rel="noreferrer" title={`${s(f.data.photographer) || f.id} · ${dateTime(date(f.data.updatedAt))}`}>
              <img
                src={thumb(s(f.data.imageUrl), 320)}
                alt=""
                loading="lazy"
                className={`aspect-[4/3] w-full rounded-lg bg-secondary object-cover ${f.data.favorite === true ? "" : "opacity-40 grayscale"}`}
              />
            </a>
          ))}
        </div>
      )}
    </div>
  );
}

function Social({ social, people }: { social: AccountSocial; people: Map<string, Account> }) {
  const person = (uid: string, when: Date | null) => {
    const a = people.get(uid);
    return (
      <Link key={uid} href={`/user?uid=${uid}`} className="flex items-center gap-3 rounded-lg px-2 py-1.5 hover:bg-secondary">
        <div className="min-w-0 flex-1">{a ? <UserCell account={a} /> : <span className="font-mono text-xs">{uid}</span>}</div>
        <span className="shrink-0 text-xs text-muted-foreground">{shortDate(when)}</span>
      </Link>
    );
  };
  const none = <p className="px-2 text-sm text-muted-foreground">None.</p>;
  return (
    <div className="grid gap-6 md:grid-cols-2 xl:grid-cols-4">
      <Column title={`Followers · ${social.followers.length}`}>{social.followers.length ? social.followers.map((f) => person(f.id, date(f.data.createdAt))) : none}</Column>
      <Column title={`Following · ${social.following.length}`}>{social.following.length ? social.following.map((f) => person(f.owner, date(f.doc.data.createdAt))) : none}</Column>
      <Column title="Follow code">
        {social.codes.length
          ? social.codes.map((c) => (
              <a key={c.id} href={`https://oriel.akbarjon.uz/follow/${c.id}`} target="_blank" rel="noreferrer" className="block px-2 font-mono text-sm hover:underline">
                {c.id}
              </a>
            ))
          : none}
      </Column>
      <Column title={`Shares · ${social.shares.length}`}>
        {social.shares.length ? (
          <div className="grid grid-cols-3 gap-2 px-2">
            {social.shares.map((x) => (
              <a key={x.id} href={`https://oriel.akbarjon.uz/share/${x.id}`} target="_blank" rel="noreferrer" title={shortDate(date(x.data.createdAt))}>
                <img src={photoFromPath(s(x.data.photoPath), 200)} alt="" loading="lazy" className="aspect-[4/3] w-full rounded-md bg-secondary object-cover" />
              </a>
            ))}
          </div>
        ) : (
          none
        )}
      </Column>
    </div>
  );
}

function Column({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="min-w-0">
      <h3 className="mb-2 px-2 text-sm font-medium">{title}</h3>
      <div className="-mx-0">{children}</div>
    </div>
  );
}

/** Every document stored for the account, every field, folded one by one. */
function AllData({ docs, pulse, names }: { docs: Doc[]; pulse: Raw | null; names: Record<string, string> }) {
  return (
    <div className="-mx-2 divide-y">
      {docs.map((d) => (
        <Folded key={d.path} label={d.path} note={`${Object.keys(d.data).length} fields`} mono>
          <FieldList data={d.data} names={names} />
        </Folded>
      ))}
      {pulse && (
        <Folded label="realtime database: pulse/{uid}" mono>
          <FieldList data={pulse} names={names} />
        </Folded>
      )}
    </div>
  );
}

function Folded({ label, note, mono, children }: { label: string; note?: string; mono?: boolean; children: ReactNode }) {
  return (
    <details className="group px-2">
      <summary className="flex cursor-pointer list-none items-center gap-2 py-2 text-sm">
        <ChevronRight className="size-4 shrink-0 text-muted-foreground transition-transform group-open:rotate-90" />
        <span className={mono ? "truncate font-mono text-xs" : "truncate"}>{label}</span>
        {note && <span className="ml-auto shrink-0 text-xs text-muted-foreground">{note}</span>}
      </summary>
      <div className="pb-3 pl-6">{children}</div>
    </details>
  );
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="min-w-0">
      <p className="text-xs text-muted-foreground">{label}</p>
      <div className="mt-0.5 truncate text-sm">{children}</div>
    </div>
  );
}
