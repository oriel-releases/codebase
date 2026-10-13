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
import { appLink, continueAsCurrent, handToExtension, hasGoogle, isFresh, signInError, signInWithGoogle, type SignedIn } from "@/lib/sign-in";
import { EmailForm, type EmailMode } from "@/components/login/EmailForm";
import { returnPath } from "@/lib/use-user";

type State =
  | { step: "idle"; error?: string }
  | { step: "working" }
  | { step: "done"; result: SignedIn; link: string; opened: boolean }
  | { step: "extension"; email: string }
  | { step: "extension-failed" }
  /** Signed in with email, for the extension: the website can only hand it a Google sign-in, so email goes in the extension itself. */
  | { step: "extension-google"; email: string; hasGoogle: boolean };

export type Account = { name: string; email: string; photo: string; google: boolean };

const query = (name: string) => (typeof window === "undefined" ? null : new URLSearchParams(window.location.search).get(name));

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

/** Google or email sign-in, then a prompt to open Oriel so the app picks up the account. */
export function LoginCard() {
  const router = useRouter();
  const [state, setState] = useState<State>({ step: "idle" });
  // The email form: which screen it is on, and the address (kept when switching between them).
  const [emailMode, setEmailMode] = useState<EmailMode>("signin");
  const [email, setEmail] = useState("");
  // "Use a different account": show every way in, even though someone is signed in here.
  const [choosing, setChoosing] = useState<false | "fresh" | "other">(false);
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
        setAccount(user ? { name: user.displayName ?? "", email: user.email ?? "", photo: user.photoURL ?? "", google: hasGoogle(user) } : null),
      );
    });
    return () => {
      cancelled = true;
      off();
    };
  }, [maybeSignedIn]);
  const known = maybeSignedIn ? account : null; // undefined: still checking

  // Sent here by a page that needs an account (/me, a share or follow link): once signed in, go straight back there
  // instead of handing the account to an app.
  const next = returnPath(query("next"));
  const goingBack = next !== null;
  useEffect(() => {
    if (known && next) router.replace(next);
  }, [known, next, router]);

  // A tab opened by the extension can close itself once the hand-over worked.
  useEffect(() => {
    if (state.step !== "extension") return;
    const timer = setTimeout(() => window.close(), 2500);
    return () => clearTimeout(timer);
  }, [state.step]);

  /** Where a finished sign-in goes, however it was made. */
  async function afterSignIn(result: SignedIn) {
    // /login?next=... comes from a page that needs an account: go back there instead of handing the account to an app.
    const back = returnPath(query("next"));
    if (back) {
      router.push(back);
      return;
    }
    // /login?for=extension is opened by the browser extension: the credential goes to it instead of the desktop app.
    if (query("for") === "extension") {
      if (result.handover.kind !== "google") {
        setState({ step: "extension-google", email: result.email, hasGoogle: result.hasGoogle });
        return;
      }
      const handed = await handToExtension({ ...result, handover: result.handover });
      setState(handed ? { step: "extension", email: result.email } : { step: "extension-failed" });
      return;
    }
    // Open the app right away. If the browser blocks it, the "Open Oriel" button is the fallback. The link is made
    // once: a shared wallpaper waiting in this browser goes along with it and is then forgotten.
    const link = appLink(result);
    forgetShare();
    window.location.href = link;
    setState({ step: "done", result, link, opened: true });
  }

  async function run(signIn: () => Promise<SignedIn>) {
    setState({ step: "working" });
    try {
      await afterSignIn(await signIn());
    } catch (e) {
      setState({ step: "idle", error: signInError(e) });
    }
  }

  const signIn = (options?: { hint?: string; chooser?: boolean }) => run(() => signInWithGoogle(options));

  /** "Continue as" the account signed in here: through Google when it has it (every app version and the extension take that). */
  function continueAs(known: Account) {
    if (known.google) return signIn({ hint: known.email });
    if (query("for") === "extension") return setState({ step: "extension-google", email: known.email, hasGoogle: false });
    return run(continueAsCurrent);
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

  if (state.step === "extension-google") {
    return (
      <StatusCard tone="warning" title="Sign in from the extension" detail={state.email}>
        <p className="text-sm text-balance text-muted">
          Email accounts sign in right in the extension: open a new tab, then Settings, Account, and use your email and password there.
        </p>
        {state.hasGoogle && (
          <button onClick={() => signIn({ hint: state.email })} className={`${buttonClass} mt-6 w-full`}>
            <Image src="/google-g-logo.webp" alt="" width={18} height={18} />
            Or continue with Google
          </button>
        )}
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

  // Nobody is signed in here: the ways in stay on screen from now on, even once a sign-in from them has gone through
  // (otherwise the form would turn into the "continue as" row halfway).
  if (known === null && !choosing) setChoosing("fresh");

  // Still checking, or already on its way back to the account page: a quiet placeholder the size of the account row.
  if (!choosing && (known === undefined || (known && goingBack))) {
    return (
      <LoginSlot kind="checking">
        <div aria-busy="true" className="flex h-[3.75rem] w-full items-center justify-center rounded-full border border-line bg-surface">
          <span aria-hidden className="size-5 animate-spin rounded-full border-2 border-ink/15 border-t-accent motion-reduce:animate-none" />
          <span className="sr-only">Checking your account</span>
        </div>
      </LoginSlot>
    );
  }

  // An account is already signed in here: show it (one tap to continue as it), instead of the sign-in options.
  if (known && !choosing) {
    return (
      <LoginSlot kind="account">
        <KnownAccount
          account={known}
          working={working}
          error={error}
          onContinue={() => continueAs(known)}
          onOther={() => {
            setState({ step: "idle" });
            setChoosing("other");
          }}
        />
      </LoginSlot>
    );
  }

  // Every way in: Google first (one tap, nothing to remember), then email and password.
  const autoFocusEmail = query("method") === "email";
  return (
    <LoginSlot kind="options">
      <div className="flex w-full flex-col items-center gap-5">
        <button onClick={() => signIn(known ? { chooser: true } : undefined)} disabled={working} className={`${buttonClass} w-full`}>
          <Image src="/google-g-logo.webp" alt="" width={18} height={18} />
          {working ? "Waiting for Google…" : "Continue with Google"}
        </button>
        {error}

        <div className="flex w-full items-center gap-4 text-xs text-muted" role="separator" aria-label="or">
          <span aria-hidden className="h-px flex-1 bg-line" />
          <span aria-hidden>or</span>
          <span aria-hidden className="h-px flex-1 bg-line" />
        </div>

        <EmailForm mode={emailMode} onMode={setEmailMode} email={email} onEmail={setEmail} autoFocus={autoFocusEmail} onSignedIn={afterSignIn} />

        {known && choosing === "other" ? (
          // Came here through "Use a different account": the way back.
          <button type="button" onClick={() => setChoosing(false)} disabled={working} className="text-sm text-muted underline underline-offset-4 hover:text-ink disabled:opacity-60">
            Back to {known.name || known.email}
          </button>
        ) : (
          // Help is for people who are not in yet: it is not shown once an account is signed in.
          <div className="mt-2">
            <SupportHint>Trouble signing in?</SupportHint>
          </div>
        )}
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
