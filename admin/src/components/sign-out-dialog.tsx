"use client";

import { LogOut } from "lucide-react";
import { useState, type ReactNode } from "react";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogMedia,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import { signOut } from "@/lib/firebase";

/** Asks before signing out; `children` is the button that opens it. */
export function SignOutDialog({ email, children }: { email?: string | null; children: ReactNode }) {
  const [busy, setBusy] = useState(false);
  return (
    <AlertDialog>
      <AlertDialogTrigger asChild>{children}</AlertDialogTrigger>
      <AlertDialogContent size="sm">
        <AlertDialogHeader>
          <AlertDialogMedia>
            <LogOut />
          </AlertDialogMedia>
          <AlertDialogTitle>Sign out of Oriel Admin?</AlertDialogTitle>
          <AlertDialogDescription>
            {email ? <>{email} will be signed out here. </> : null}
            Everything read on this browser is forgotten too.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            disabled={busy}
            onClick={() => {
              setBusy(true);
              void signOut().finally(() => setBusy(false));
            }}
          >
            Sign out
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
