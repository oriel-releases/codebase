"use client";

import { useSyncExternalStore } from "react";
import { routes } from "@/config/site";
import { ButtonLink } from "@/components/ui/Button";
import { looksSignedIn, subscribeSignedIn } from "@/lib/sessions";
import { navigation } from "@/lib/transitions";

/** An "Account" button in the header, only for browsers that are signed in (decided without loading Firebase). */
export function AccountLink() {
  const signedIn = useSyncExternalStore(subscribeSignedIn, looksSignedIn, () => false);
  if (!signedIn) return null;
  return (
    <ButtonLink href={routes.me} transitionTypes={[...navigation.forward]} variant="secondary" size="sm">
      Account
    </ButtonLink>
  );
}
