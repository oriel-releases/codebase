"use client";

import { Search } from "lucide-react";
import { useRouter } from "next/navigation";
import { useMemo } from "react";
import { Empty, ErrorNote, PlatformBadge, RefreshButton } from "@/components/bits";
import { PageHeader } from "@/components/shell";
import { UserCell } from "@/components/user-cell";
import { Card } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";
import { overview, time } from "@/lib/data";
import { ageFrom, platformNames, platformOrder, shortDate, timeAgo } from "@/lib/format";
import { useLoad } from "@/lib/use-load";
import { useRemembered, useScrollMemory } from "@/lib/use-remembered";

type Sort = "joined" | "seen";

export default function UsersPage() {
  const { data, error, loading, reload } = useLoad(overview);
  const router = useRouter();
  // Remembered, so coming back from an account (or reloading) keeps the list as it was.
  const [search, setSearch] = useRemembered("users.search", "");
  const [platform, setPlatform] = useRemembered("users.platform", "all");
  const [sort, setSort] = useRemembered<Sort>("users.sort", "joined");
  useScrollMemory("users", !!data);

  const rows = useMemo(() => {
    const q = search.trim().toLowerCase();
    return (data?.accounts ?? [])
      .filter((a) => !q || a.email.toLowerCase().includes(q) || a.displayName.toLowerCase().includes(q) || a.uid.includes(search.trim()))
      .filter((a) => platform === "all" || a.sessions.some((s) => s.platform === platform))
      .sort((a, b) => (sort === "joined" ? time(b.createdAt) - time(a.createdAt) : time(b.lastSeenAt) - time(a.lastSeenAt)));
  }, [data, search, platform, sort]);

  return (
    <>
      <PageHeader
        title="Users"
        description={data ? `${rows.length} of ${data.accounts.length} accounts` : "Every account"}
        actions={<RefreshButton loading={loading} onClick={reload} />}
      />
      {error && <ErrorNote message={error} />}

      <div className="mb-4 flex flex-wrap items-center gap-3">
        <div className="relative w-full sm:w-72">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Name, email or uid" className="rounded-full pl-9" />
        </div>
        <ToggleGroup type="single" value={platform} onValueChange={(v) => setPlatform(v || "all")} variant="outline" size="sm" className="flex-wrap">
          <ToggleGroupItem value="all">All</ToggleGroupItem>
          {platformOrder.map((p) => (
            <ToggleGroupItem key={p} value={p}>
              {platformNames[p]}
            </ToggleGroupItem>
          ))}
        </ToggleGroup>
        <ToggleGroup type="single" value={sort} onValueChange={(v) => v && setSort(v as Sort)} variant="outline" size="sm" className="sm:ml-auto">
          <ToggleGroupItem value="joined">Newest</ToggleGroupItem>
          <ToggleGroupItem value="seen">Last seen</ToggleGroupItem>
        </ToggleGroup>
      </div>

      <Card className="py-0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="pl-4">Account</TableHead>
              <TableHead>Devices</TableHead>
              <TableHead>Age</TableHead>
              <TableHead>Joined</TableHead>
              <TableHead className="pr-4">Last seen</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {!data &&
              Array.from({ length: 8 }, (_, i) => (
                <TableRow key={i}>
                  <TableCell colSpan={5} className="px-4">
                    <Skeleton className="h-8" />
                  </TableCell>
                </TableRow>
              ))}
            {rows.map((a) => (
              <TableRow key={a.uid} className="cursor-pointer" onClick={() => router.push(`/user?uid=${a.uid}`)}>
                <TableCell className="max-w-72 pl-4">
                  <UserCell account={a} />
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
                <TableCell className="pr-4 text-muted-foreground">{timeAgo(a.lastSeenAt)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        {data && rows.length === 0 && <Empty>Nobody matches.</Empty>}
      </Card>
    </>
  );
}
