"use client";

import Image from "next/image";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { onAuthStateChanged } from "firebase/auth";
import { useEffect, useState, useSyncExternalStore, type ReactNode } from "react";
import { routes } from "@/config/site";
import { AnchorButton } from "@/components/ui/Button";
import { SupportHint } from "@/components/ui/SupportCard";
import { firebaseAuth } from "@/lib/firebase";
import { looksSignedIn, subscribeSignedIn } from "@/lib/sessions";
import { navigation } from "@/lib/transitions";
import { forgetShare } from "@/lib/pending-share";
import { appLink, handToExtension, isFresh, signInWithGoogle, type SignedIn } from "@/lib/sign-in";

type State =
  | { step: "idle"; error?: string }
  | { step: "working" }
  | { step: "done"; result: SignedIn; link: string; opened: boolean }
  | { step: "extension"; email: string }
  | { step: "extension-failed" };

export type Account = { name: string; email: string; photo: string };

const noSubscribe = () => () => {};

/**
 * The sign-in area before anything is signed in: always the same height, whichever of its states is showing (nothing
 * yet, checking, the account row, the Google button). Switching between them only fades, so the page never jumps.
 */
function LoginSlot({ kind, children }: { kind: string; children?: ReactNode }) {
  return (
    <div className="min-h-[6.75rem] w-full">
      <div key={kind} className="login-fade flex w-full flex-col items-center">
        {children}
      </div>
    </div>
  );
}

const buttonClass =
  "inline-flex items-center justify-center gap-3 rounded-full bg-ink px-6 py-3 text-sm font-medium text-paper transition-opacity  disabled:opacity-60";

/** Google sign-in, then a prompt to open Oriel so the app picks up the account. */
export function LoginCard() {
  const router = useRouter();
  const [state, setState] = useState<State>({ step: "idle" });
  const hydrated = useSyncExternalStore(noSubscribe, () => true, () => false);

  // Is somebody already signed in on this browser? Decided cheaply first (no Firebase), then confirmed.
  const maybeSignedIn = useSyncExternalStore(subscribeSignedIn, looksSignedIn, () => false);
  const [account, setAccount] = useState<Account | null | undefined>(undefined);
  useEffect(() => {
    if (!maybeSignedIn) return;
    let off = () => {};
    let cancelled = false;
    void firebaseAuth().then((auth) => {
      if (cancelled) return;
      off = onAuthStateChanged(auth, (user) =>
        setAccount(user ? { name: user.displayName ?? "", email: user.email ?? "", photo: user.photoURL ?? "" } : null),
      );
    });
    return () => {
      cancelled = true;
      off();
    };
  }, [maybeSignedIn]);
  const known = maybeSignedIn ? account : null; // undefined: still checking

  // Coming from the account page while already signed in: nothing to ask, go straight back.
  const toAccountPage = typeof window !== "undefined" && new URLSearchParams(window.location.search).get("next") === "me";
  useEffect(() => {
    if (known && toAccountPage) router.replace("/me");
  }, [known, toAccountPage, router]);

  // A tab opened by the extension can close itself once the hand-over worked.
  useEffect(() => {
    if (state.step !== "extension") return;
    const timer = setTimeout(() => window.close(), 2500);
    return () => clearTimeout(timer);
  }, [state.step]);

  async function signIn(options?: { hint?: string; chooser?: boolean }) {
    setState({ step: "working" });
    try {
      const result = await signInWithGoogle(options);
      // /login?next=me comes from the account page: go back there instead of handing the account to an app.
      if (new URLSearchParams(window.location.search).get("next") === "me") {
        router.push("/me");
        return;
      }
      // /login?for=extension is opened by the browser extension: the credential goes to it instead of the desktop app.
      if (new URLSearchParams(window.location.search).get("for") === "extension") {
        const handed = await handToExtension(result);
        setState(handed ? { step: "extension", email: result.email } : { step: "extension-failed" });
        return;
      }
      // Open the app right away. If the browser blocks it, the "Open Oriel" button is the fallback. The link is made
      // once: a shared wallpaper waiting in this browser goes along with it and is then forgotten.
      const link = appLink(result);
      forgetShare();
      window.location.href = link;
      setState({ step: "done", result, link, opened: true });
    } catch (e) {
      const code = (e as { code?: string }).code;
      const dismissed = code === "auth/popup-closed-by-user" || code === "auth/cancelled-popup-request";
      setState({ step: "idle", error: dismissed ? undefined : "Couldn't sign you in. Please try again." });
    }
  }

  if (state.step === "extension") {
    return (
      <StatusCard tone="success" title="Extension connected" detail={state.email}>
        <p className="text-sm text-balance text-muted">You can close this tab and go back to your new tab page.</p>
      </StatusCard>
    );
  }

  if (state.step === "extension-failed") {
    return (
      <StatusCard tone="error" title="Couldn't reach the extension">
        <p className="text-sm text-balance text-muted">Make sure the Oriel extension is installed and turned on in this browser, then start again from its settings.</p>
        <button onClick={() => setState({ step: "idle" })} className={`${buttonClass} mt-6 w-full`}>
          Try again
        </button>
      </StatusCard>
    );
  }

  if (state.step === "done") {
    const { result } = state;
    // The link only works for an hour; after that, ask for a fresh sign-in.
    const expired = !isFresh(result);
    return (
      <StatusCard tone={expired ? "warning" : "success"} title={expired ? "Sign-in expired" : "You're signed in"} detail={result.email}>
        {expired ? (
          <>
            <p className="text-sm text-balance text-muted">That took a while. Sign in again to continue.</p>
            <button onClick={() => signIn()} className={`${buttonClass} mt-6 w-full`}>
              Sign in again
            </button>
          </>
        ) : (
          <>
            <AnchorButton href={state.link} onClick={() => setState({ ...state, opened: true })} className="mt-6 w-full">
              Open Oriel
              <svg viewBox="0 0 24 24" width={16} height={16} fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden>
                <path d="M5 12h14M13 6l6 6-6 6" />
              </svg>
            </AnchorButton>
            <p className={`mt-4 text-xs text-balance text-muted transition-opacity duration-500 ${state.opened ? "opacity-100" : "opacity-0"}`}>
              Nothing happened?{" "}
              <Link href={routes.download} transitionTypes={[...navigation.forward]} className="grow text-ink">
                Install Oriel
              </Link>{" "}
              first, then sign in here again.
            </p>
          </>
        )}
      </StatusCard>
    );
  }

  // Until the page is running in the browser it can't know whether someone is signed in here, so it shows nothing (same
  // height, no jump) instead of flashing the Google button and the help link at people who are.
  if (!hydrated) return <LoginSlot kind="blank" />;

  const working = state.step === "working";
  const error =
    state.step === "idle" && state.error ? (
      <p role="alert" className="text-sm text-red-600 dark:text-red-400">
        {state.error}
      </p>
    ) : null;

  // Still checking, or already on its way back to the account page: a quiet placeholder the size of the account row.
  if (known === undefined || (known && toAccountPage)) {
    return (
      <LoginSlot kind="checking">
        <div aria-busy="true" className="flex h-[3.75rem] w-full items-center justify-center rounded-full border border-line bg-surface">
          <span aria-hidden className="size-5 animate-spin rounded-full border-2 border-ink/15 border-t-accent motion-reduce:animate-none" />
          <span className="sr-only">Checking your account</span>
        </div>
      </LoginSlot>
    );
  }

  // An account is already signed in here: show it (one tap to continue as it), instead of the Google button.
  if (known) {
    return (
      <LoginSlot kind="account">
        <KnownAccount account={known} working={working} error={error} onContinue={() => signIn({ hint: known.email })} onOther={() => signIn({ chooser: true })} />
      </LoginSlot>
    );
  }

  return (
    <LoginSlot kind="google">
      <div className="flex w-full flex-col items-center gap-4">
      <button onClick={() => signIn()} disabled={working} className={buttonClass}>
        <Image src="/google-g-logo.webp" alt="" width={18} height={18} />
        {working ? "Waiting for Google…" : "Continue with Google"}
      </button>
      {error}
      {/* Help is for people who are not in yet: it is not shown once an account is signed in. */}
      <div className="mt-4">
        <SupportHint>Trouble signing in?</SupportHint>
      </div>
      </div>
    </LoginSlot>
  );
}

/** The account that is already signed in on this browser, as a single row to continue with. */
export function KnownAccount({
  account,
  working,
  error,
  onContinue,
  onOther,
}: {
  account: Account;
  working: boolean;
  error: ReactNode;
  onContinue: () => void;
  onOther: () => void;
}) {
  const initial = (account.name || account.email).trim().charAt(0).toUpperCase();
  return (
    <div className="flex w-full flex-col items-center gap-4">
      <button
        type="button"
        onClick={onContinue}
        disabled={working}
        className="group flex w-full items-center gap-3.5 rounded-full border border-line bg-surface p-2.5 pr-5 text-left transition-colors focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-accent disabled:opacity-70"
      >
        {account.photo ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img src={account.photo} alt="" referrerPolicy="no-referrer" className="size-11 shrink-0 rounded-full object-cover" />
        ) : (
          <span className="grid size-11 shrink-0 place-items-center rounded-full bg-ink/10 font-semibold">{initial}</span>
        )}
        <span className="min-w-0 flex-1">
          <span className="block truncate text-sm font-medium">{account.name || account.email}</span>
          {account.name && <span className="block truncate text-xs text-muted">{account.email}</span>}
        </span>
        {working ? (
          <span aria-hidden className="size-5 shrink-0 animate-spin rounded-full border-2 border-ink/15 border-t-accent motion-reduce:animate-none" />
        ) : (
          <svg viewBox="0 0 24 24" width={18} height={18} fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden className="shrink-0 text-muted transition-transform ">
            <path d="M5 12h14M13 6l6 6-6 6" />
          </svg>
        )}
      </button>
      <button type="button" onClick={onOther} disabled={working} className="text-sm text-muted underline underline-offset-4 hover:text-ink disabled:opacity-60">
        Use a different account
      </button>
      {error}
    </div>
  );
}

type Tone = "success" | "warning" | "error";

const tones: Record<Tone, { badge: string; glow: string }> = {
  success: { badge: "bg-emerald-500/15 text-emerald-600 dark:text-emerald-400", glow: "bg-emerald-500/25" },
  warning: { badge: "bg-amber-500/15 text-amber-600 dark:text-amber-400", glow: "bg-amber-500/25" },
  error: { badge: "bg-red-500/15 text-red-600 dark:text-red-400", glow: "bg-red-500/25" },
};

/**
 * The result screens after signing in: a status mark that pops in (a success check draws itself), the title, who it is,
 * and what to do next. One shape for success, "expired" and failure so they read as the same family.
 */
export function StatusCard({ tone, title, detail, children }: { tone: Tone; title: string; detail?: string; children: ReactNode }) {
  const t = tones[tone];
  return (
    <div role={tone === "error" ? "alert" : "status"} className="relative w-full overflow-hidden rounded-[2rem] bg-surface p-7 text-center sm:p-8">
      {/* A soft glow behind the mark. */}
      <div aria-hidden className={`pointer-events-none absolute -top-16 left-1/2 size-44 -translate-x-1/2 rounded-full blur-3xl ${t.glow}`} />
      <div className="relative">
        <span className={`status-pop mx-auto grid size-16 place-items-center rounded-full ${t.badge}`}>
          <svg viewBox="0 0 24 24" width={30} height={30} fill="none" stroke="currentColor" strokeWidth={2.4} strokeLinecap="round" strokeLinejoin="round" aria-hidden className={tone === "success" ? "status-check" : ""}>
            {tone === "success" ? (
              <path d="m4 12 5 5L20 6" />
            ) : tone === "warning" ? (
              <>
                <circle cx="12" cy="12" r="9" />
                <path d="M12 7v5l3 2" />
              </>
            ) : (
              <>
                <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3" />
                <path d="M12 9v4M12 17h.01" />
              </>
            )}
          </svg>
        </span>
        <h2 className="mt-5 text-2xl font-semibold tracking-tight text-balance">{title}</h2>
        {detail && <p className="mx-auto mt-3 w-fit max-w-full truncate rounded-full bg-ink/5 px-3.5 py-1 text-xs font-medium text-muted">{detail}</p>}
        <div className="mt-4">{children}</div>
      </div>
    </div>
  );
}
