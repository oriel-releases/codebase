"use client";

import { useRouter } from "next/navigation";
import { Empty, ErrorNote, RefreshButton } from "@/components/bits";
import { PageHeader } from "@/components/shell";
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar";
import { Card } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { codes, photoFromPath } from "@/lib/data";
import { shortDate } from "@/lib/format";
import { useLoad } from "@/lib/use-load";

export default function CodesPage() {
  const { data, error, loading, reload } = useLoad(codes);
  const router = useRouter();
  return (
    <>
      <PageHeader title="Follow codes" description="Each account's code and the wallpaper it shows now (up to 200)" actions={<RefreshButton loading={loading} onClick={reload} />} />
      {error && <ErrorNote message={error} />}
      <Card className="py-0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="pl-4">Owner</TableHead>
              <TableHead>Code</TableHead>
              <TableHead>Wallpaper</TableHead>
              <TableHead className="pr-4">Created</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {!data &&
              !error &&
              Array.from({ length: 6 }, (_, i) => (
                <TableRow key={i}>
                  <TableCell colSpan={4} className="px-4">
                    <Skeleton className="h-10" />
                  </TableCell>
                </TableRow>
              ))}
            {data?.map((c) => (
              <TableRow key={c.code} className="cursor-pointer" onClick={() => router.push(`/user?uid=${c.uid}`)}>
                <TableCell className="pl-4">
                  <div className="flex items-center gap-3">
                    <Avatar className="size-8">
                      <AvatarImage src={c.picture || undefined} alt="" referrerPolicy="no-referrer" />
                      <AvatarFallback className="text-xs">{c.name[0] ?? "?"}</AvatarFallback>
                    </Avatar>
                    {c.name || "—"}
                  </div>
                </TableCell>
                <TableCell className="font-mono">{c.code}</TableCell>
                <TableCell>
                  {c.photoPath ? <img src={photoFromPath(c.photoPath, 160)} alt="" loading="lazy" className="h-10 w-16 rounded-md bg-secondary object-cover" /> : "—"}
                </TableCell>
                <TableCell className="pr-4 text-muted-foreground">{shortDate(c.createdAt)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        {data?.length === 0 && <Empty>No follow codes yet.</Empty>}
      </Card>
    </>
  );
}
