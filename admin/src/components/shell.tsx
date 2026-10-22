"use client";

import { Gauge, Link2, LogOut, Share2, Users } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import type { ReactNode } from "react";
import { Logo } from "@/components/logo";
import { SignOutDialog } from "@/components/sign-out-dialog";
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { initials } from "@/lib/format";
import { useUser } from "@/lib/use-user";
import { cn } from "@/lib/utils";

const nav = [
  { href: "/", label: "Overview", icon: Gauge },
  { href: "/users", label: "Users", icon: Users },
  { href: "/shares", label: "Shares", icon: Share2 },
  { href: "/codes", label: "Follow codes", icon: Link2 },
];

/** A sidebar on wide screens, a top bar with the same links on phones. */
export function Shell({ children }: { children: ReactNode }) {
  const path = usePathname();
  const user = useUser();
  const active = (href: string) => (href === "/" ? path === "/" : path.startsWith(href) || (href === "/users" && path.startsWith("/user")));

  return (
    <div className="min-h-svh md:grid md:grid-cols-[232px_1fr]">
      <aside className="sticky top-0 z-20 flex flex-col border-b bg-background/85 backdrop-blur md:h-svh md:border-r md:border-b-0">
        <div className="flex items-center gap-2.5 px-4 py-3 md:px-5 md:py-5">
          <Logo size={24} />
          <span className="font-semibold tracking-tight">Oriel Admin</span>
          {user && (
            <SignOutDialog email={user.email}>
              <button className="ml-auto rounded-full md:hidden" aria-label="Sign out">
                <Avatar className="size-8">
                  <AvatarImage src={user.photoURL ?? undefined} alt="" />
                  <AvatarFallback>{initials(user.displayName ?? "", user.email ?? "")}</AvatarFallback>
                </Avatar>
              </button>
            </SignOutDialog>
          )}
        </div>
        <nav className="flex gap-1 overflow-x-auto px-3 pb-3 md:flex-col md:pb-0">
          {nav.map(({ href, label, icon: Icon }) => (
            <Link
              key={href}
              href={href}
              className={cn(
                "flex shrink-0 items-center gap-2.5 rounded-full px-3 py-2 text-sm transition-colors",
                active(href) ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:bg-secondary hover:text-foreground",
              )}
            >
              <Icon className="size-4" />
              {label}
            </Link>
          ))}
        </nav>
        {user && (
          <div className="mt-auto hidden items-center gap-3 border-t p-4 md:flex">
            <Avatar className="size-8">
              <AvatarImage src={user.photoURL ?? undefined} alt="" />
              <AvatarFallback>{initials(user.displayName ?? "", user.email ?? "")}</AvatarFallback>
            </Avatar>
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-medium">{user.displayName}</p>
              <p className="truncate text-xs text-muted-foreground">{user.email}</p>
            </div>
            <SignOutDialog email={user.email}>
              <Button variant="ghost" size="icon" aria-label="Sign out">
                <LogOut />
              </Button>
            </SignOutDialog>
          </div>
        )}
      </aside>
      <main className="min-w-0 px-4 py-6 md:px-10 md:py-10">{children}</main>
    </div>
  );
}

export function PageHeader({ title, description, actions }: { title: string; description?: ReactNode; actions?: ReactNode }) {
  return (
    <div className="mb-8 flex flex-wrap items-end justify-between gap-4">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight md:text-3xl">{title}</h1>
        {description && <p className="mt-1 text-sm text-muted-foreground">{description}</p>}
      </div>
      {actions}
    </div>
  );
}
