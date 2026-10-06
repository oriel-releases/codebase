"use client";

import Image from "next/image";
import Link from "next/link";
import { useState } from "react";
import { routes } from "@/config/site";
import { AnchorButton } from "@/components/ui/Button";
import { navigation } from "@/lib/transitions";
import { appLink, isFresh, signInWithGoogle, type SignedIn } from "@/lib/sign-in";

type State =
  | { step: "idle"; error?: string }
  | { step: "working" }
  | { step: "done"; result: SignedIn; opened: boolean };

const buttonClass =
  "inline-flex items-center justify-center gap-3 rounded-full bg-ink px-6 py-3 text-sm font-medium text-paper transition-opacity hover:opacity-85 disabled:opacity-60";

/** Google sign-in, then a prompt to open Oriel so the app picks up the account. */
export function LoginCard() {
  const [state, setState] = useState<State>({ step: "idle" });

  async function signIn() {
    setState({ step: "working" });
    try {
      const result = await signInWithGoogle();
      // Open the app right away. If the browser blocks it, the "Open Oriel" button is the fallback.
      window.location.href = appLink(result);
      setState({ step: "done", result, opened: true });
    } catch (e) {
      const code = (e as { code?: string }).code;
      const dismissed = code === "auth/popup-closed-by-user" || code === "auth/cancelled-popup-request";
      setState({ step: "idle", error: dismissed ? undefined : "Couldn't sign you in. Please try again." });
    }
  }

  if (state.step === "done") {
    const { result } = state;
    // The link only works for an hour; after that, ask for a fresh sign-in.
    const expired = !isFresh(result);
    return (
      <div role="dialog" aria-labelledby="open-title" className="w-full rounded-3xl border border-line bg-surface p-8 text-center">
        <h2 id="open-title" className="text-xl font-semibold tracking-tight">
          {expired ? "That took a while" : "You're signed in"}
        </h2>
        <p className="mt-2 text-sm text-balance text-muted">
          {expired ? "Your sign-in expired. Sign in again to continue." : `Signed in as ${result.email}. Open Oriel to finish.`}
        </p>
        <div className="mt-6 flex flex-col items-center gap-3">
          {expired ? (
            <button onClick={signIn} className={buttonClass}>
              Sign in again
            </button>
          ) : (
            <AnchorButton href={appLink(result)} onClick={() => setState({ ...state, opened: true })}>
              Open Oriel
            </AnchorButton>
          )}
          {state.opened && !expired && (
            <p className="text-xs text-balance text-muted">
              Nothing happened?{" "}
              <Link href={routes.download} transitionTypes={[...navigation.forward]} className="underline underline-offset-4 hover:text-ink">
                Install Oriel
              </Link>{" "}
              first, then sign in here again.
            </p>
          )}
        </div>
      </div>
    );
  }

  const working = state.step === "working";
  return (
    <div className="flex w-full flex-col items-center gap-4">
      <button onClick={signIn} disabled={working} className={buttonClass}>
        <Image src="/google-g-logo.webp" alt="" width={18} height={18} />
        {working ? "Waiting for Google…" : "Continue with Google"}
      </button>
      {state.step === "idle" && state.error && (
        <p role="alert" className="text-sm text-red-600 dark:text-red-400">
          {state.error}
        </p>
      )}
    </div>
  );
}
