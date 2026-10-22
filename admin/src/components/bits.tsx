"use client";

import { RefreshCw, TriangleAlert, type LucideIcon } from "lucide-react";
import type { ReactNode } from "react";
import { PlatformIcon } from "@/components/platform-icon";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { platformNames } from "@/lib/format";
import { cn } from "@/lib/utils";

export function RefreshButton({ loading, onClick }: { loading: boolean; onClick: () => void }) {
  return (
    <Button variant="secondary" size="sm" onClick={onClick} disabled={loading}>
      <RefreshCw className={cn(loading && "animate-spin")} /> Refresh
    </Button>
  );
}

export function ErrorNote({ message }: { message: string }) {
  return (
    <div className="mb-6 flex items-start gap-3 rounded-xl border border-destructive/30 bg-destructive/5 p-4 text-sm text-destructive">
      <TriangleAlert className="mt-0.5 size-4 shrink-0" />
      <p>{message}</p>
    </div>
  );
}

export function Stat({ label, value, hint, icon: Icon }: { label: string; value: ReactNode; hint?: ReactNode; icon?: LucideIcon }) {
  return (
    <Card className="gap-0 py-5">
      <CardContent className="px-5">
        <div className="flex items-center justify-between gap-2">
          <p className="text-sm text-muted-foreground">{label}</p>
          {Icon && <Icon className="size-4 text-muted-foreground" />}
        </div>
        <p className="mt-2 text-3xl font-semibold tracking-tight tabular-nums">{value}</p>
        {hint && <p className="mt-1 text-xs text-muted-foreground">{hint}</p>}
      </CardContent>
    </Card>
  );
}

export function PlatformBadge({ platform }: { platform: string }) {
  return (
    <Badge variant="secondary" className="gap-1.5 font-normal">
      <PlatformIcon platform={platform} className="size-3!" />
      {platformNames[platform] ?? (platform || "Unknown")}
    </Badge>
  );
}

export function Empty({ children }: { children: ReactNode }) {
  return <p className="py-10 text-center text-sm text-muted-foreground">{children}</p>;
}
