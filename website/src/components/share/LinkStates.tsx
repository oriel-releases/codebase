"use client";

import Link from "next/link";
import type { ReactNode } from "react";
import { routes, site } from "@/config/site";
import { ButtonLink } from "@/components/ui/Button";
import { Logo } from "@/components/ui/Logo";
import { Frame, linkClass, photoMaxHeight } from "@/components/wallpaper/WallpaperStage";

/** The card in place of a photo: the Oriel mark (or a picture) and a line. */
export function Placeholder({ mark, children }: { mark?: ReactNode; children?: ReactNode }) {
  return (
    <div className={`grid aspect-[3/2] w-full place-items-center rounded-[2rem] bg-surface ${photoMaxHeight}`}>
      <div className="flex flex-col items-center gap-5 px-6 text-center">
        {mark ?? <Logo size={96} className="size-16 opacity-80 sm:size-20" />}
        {children}
      </div>
    </div>
  );
}

/** Signed out: what's behind the link is only shown to signed-in people. Signing in comes back here. */
export function SignInFirst({ what }: { what: string }) {
  const next = typeof window === "undefined" ? "" : window.location.pathname;
  return (
    <Frame
      photo={
        <Placeholder>
          <h1 className="text-xl font-semibold tracking-tight text-balance sm:text-2xl">Sign in to see {what}</h1>
        </Placeholder>
      }
      action={
        <ButtonLink href={`${routes.login}?next=${encodeURIComponent(next)}`} className="w-full py-3.5 text-base">
          Sign in
        </ButtonLink>
      }
      note={<>It&apos;s free, with Google or your email. You come right back here.</>}
    />
  );
}

/** A link that leads nowhere: broken, turned off, or the visitor's own. */
export function LinkProblem({ title, note }: { title: string; note: ReactNode }) {
  return <Frame photo={<Placeholder />} action={<span className="text-lg font-medium text-ink">{title}</span>} note={note} />;
}

export const askAgain = (
  <>
    Ask your friend to send it again, or&nbsp;
    <Link href={routes.home} className={linkClass}>
      see what {site.name} is
    </Link>
    .
  </>
);
