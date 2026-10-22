"use client";

import Link from "next/link";
import { Empty, ErrorNote, RefreshButton } from "@/components/bits";
import { PageHeader } from "@/components/shell";
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar";
import { Card } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { photoFromPath, shares } from "@/lib/data";
import { timeAgo } from "@/lib/format";
import { useLoad } from "@/lib/use-load";

export default function SharesPage() {
  const { data, error, loading, reload } = useLoad(shares);
  return (
    <>
      <PageHeader title="Shares" description="Wallpapers sent by link, newest first (up to 200)" actions={<RefreshButton loading={loading} onClick={reload} />} />
      {error && <ErrorNote message={error} />}
      {data?.length === 0 && <Empty>No shares yet.</Empty>}
      <div className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-5">
        {!data && !error && Array.from({ length: 10 }, (_, i) => <Skeleton key={i} className="aspect-[4/3] rounded-xl" />)}
        {data?.map((s) => (
          <Card key={s.id} className="gap-0 overflow-hidden py-0">
            <a href={`https://oriel.akbarjon.uz/share/${s.id}`} target="_blank" rel="noreferrer">
              <img src={photoFromPath(s.photoPath)} alt="" loading="lazy" className="aspect-[4/3] w-full bg-secondary object-cover" />
            </a>
            <Link href={`/user?uid=${s.uid}`} className="flex items-center gap-2 p-3 hover:bg-secondary">
              <Avatar className="size-6">
                <AvatarImage src={s.picture || undefined} alt="" referrerPolicy="no-referrer" />
                <AvatarFallback className="text-[10px]">{s.from[0] ?? "?"}</AvatarFallback>
              </Avatar>
              <span className="min-w-0 flex-1 truncate text-sm">{s.from || "Someone"}</span>
              <span className="shrink-0 text-xs text-muted-foreground">{timeAgo(s.createdAt)}</span>
            </Link>
          </Card>
        ))}
      </div>
    </>
  );
}
