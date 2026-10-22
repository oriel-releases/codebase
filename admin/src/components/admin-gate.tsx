"use client";

import { LoaderCircle, LogOut, ShieldX } from "lucide-react";
import { useState, type ReactNode } from "react";
import { Logo } from "@/components/logo";
import { Button } from "@/components/ui/button";
import { adminEmail, signIn, signOut } from "@/lib/firebase";
import { useUser } from "@/lib/use-user";

/**
 * Shows the admin site only to the admin's Google sign-in. Anyone can load this code, so this is just the door's
 * sign: Firestore's rules (isAdmin) refuse every read for any other account.
 */
export function AdminGate({ children }: { children: ReactNode }) {
  const user = useUser();
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);

  if (user === undefined) {
    return (
      <Centered>
        <LoaderCircle className="size-6 animate-spin text-muted-foreground" />
      </Centered>
    );
  }

  const isAdmin =
    !!user && user.email === adminEmail && user.emailVerified && user.providerData.some((p) => p.providerId === "google.com");
  if (isAdmin) return children;

  if (user) {
    return (
      <Centered>
        <ShieldX className="size-10 text-muted-foreground" />
        <h1 className="mt-6 text-2xl font-semibold tracking-tight">Not for this account</h1>
        <p className="mt-2 text-sm text-muted-foreground">{user.email} can&apos;t open Oriel Admin.</p>
        <Button variant="secondary" className="mt-8" onClick={() => void signOut()}>
          <LogOut /> Sign out
        </Button>
      </Centered>
    );
  }

  const start = async () => {
    setBusy(true);
    setError(undefined);
    try {
      await signIn();
    } catch (e) {
      const code = (e as { code?: string }).code;
      if (code !== "auth/popup-closed-by-user" && code !== "auth/cancelled-popup-request")
        setError(code === "auth/unauthorized-domain" ? "Add this domain to Firebase Auth's authorized domains." : "Couldn't sign in. Try again.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Centered>
      <Logo size={56} />
      <h1 className="mt-8 text-3xl font-semibold tracking-tight">Oriel Admin</h1>
      <p className="mt-2 text-sm text-muted-foreground">Sign in with the owner&apos;s Google account.</p>
      <Button size="lg" className="mt-8" onClick={() => void start()} disabled={busy}>
        {busy ? <LoaderCircle className="animate-spin" /> : <img src="/google-g-logo.webp" alt="" className="size-4" />}
        Continue with Google
      </Button>
      {error && <p className="mt-4 text-sm text-destructive">{error}</p>}
    </Centered>
  );
}

function Centered({ children }: { children: ReactNode }) {
  return <div className="flex min-h-svh flex-col items-center justify-center px-4 text-center">{children}</div>;
}
