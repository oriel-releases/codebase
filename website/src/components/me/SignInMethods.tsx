"use client";

import Image from "next/image";
import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { PasswordField } from "@/components/login/EmailForm";

/** Mirrors lib/sign-in-methods' Methods (kept here so this file stays free of Firebase). */
export type MethodsView = { google: { email: string } | null; password: boolean; emailVerified: boolean };

/**
 * How a change on the account went: done, failed (with words to show, or none when the person closed Google's
 * window), or Firebase wants proof it's really them first.
 */
export type Outcome = { ok: true } | { ok: false; error?: string } | { ok: false; confirm: "google" | "password" };

export type MethodActions = {
  addPassword: (password: string) => Promise<Outcome>;
  changePassword: (current: string, next: string) => Promise<Outcome>;
  connectGoogle: () => Promise<Outcome>;
  disconnect: (method: "google" | "password") => Promise<Outcome>;
  confirm: (password?: string) => Promise<Outcome>;
  sendVerification: () => Promise<Outcome>;
};

const minLength = 8;
const card = "rounded-[2rem] bg-surface text-left text-ink";
const pill =
  "inline-flex shrink-0 items-center justify-center gap-2 rounded-full px-3.5 py-1.5 text-[13px] font-medium whitespace-nowrap transition-colors focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-accent disabled:opacity-60";

type Dialog =
  | { kind: "set-password" }
  | { kind: "change-password" }
  | { kind: "disconnect"; method: "google" | "password" }
  /** Proof first, then `retry` (the change that asked for it), which then reports `success`. */
  | { kind: "confirm"; with: "google" | "password"; retry: () => Promise<Outcome>; what: string; success: string };

/** The ways this account can sign in, and the buttons to add, change or remove them. */
export function SignInMethods({ methods, email, actions }: { methods: MethodsView; email: string; actions: MethodActions }) {
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const [notice, setNotice] = useState<{ tone: "error" | "success"; text: string } | null>(null);
  const [connecting, setConnecting] = useState(false);
  const [verifyState, setVerifyState] = useState<"idle" | "sending" | "sent">("idle");
  const both = Boolean(methods.google) && methods.password;

  async function connect() {
    setNotice(null);
    setConnecting(true);
    const outcome = await actions.connectGoogle();
    setConnecting(false);
    const success = "Google connected. You can now sign in with either.";
    if (outcome.ok) setNotice({ tone: "success", text: success });
    else if ("confirm" in outcome) setDialog({ kind: "confirm", with: outcome.confirm, retry: actions.connectGoogle, what: "connect Google", success });
    else if (outcome.error) setNotice({ tone: "error", text: outcome.error });
  }

  async function verify() {
    setVerifyState("sending");
    const outcome = await actions.sendVerification();
    if (outcome.ok) setVerifyState("sent");
    else {
      setVerifyState("idle");
      if ("error" in outcome && outcome.error) setNotice({ tone: "error", text: outcome.error });
    }
  }

  return (
    <section aria-labelledby="methods-title" className={`${card} p-5 sm:p-6`}>
      <div className="px-1">
        <h2 id="methods-title" className="text-lg font-semibold tracking-tight">
          How you sign in
        </h2>
        <p className="mt-1 text-sm text-pretty text-muted">
          {both ? "Either one gets you into Oriel, on the website and in the apps." : "Add a second way in, so you're never locked out of your account."}
        </p>
      </div>

      <ul className="mt-4 space-y-1">
        <MethodRow
          icon={<Image src="/google-g-logo.webp" alt="" width={18} height={18} />}
          title="Google"
          detail={methods.google ? methods.google.email || "Connected" : "Not connected · sign in with one tap"}
          status={methods.google ? "on" : "off"}
        >
          {methods.google ? (
            both && (
              <button type="button" onClick={() => setDialog({ kind: "disconnect", method: "google" })} className={`${pill} bg-ink/5 text-ink`}>
                Disconnect
              </button>
            )
          ) : (
            <button type="button" onClick={connect} disabled={connecting} className={`${pill} bg-ink text-paper`}>
              {connecting && <Spinner />}
              {connecting ? "Connecting…" : "Connect"}
            </button>
          )}
        </MethodRow>

        <MethodRow
          icon={<KeyIcon />}
          title="Email and password"
          detail={
            methods.password ? (
              <>
                {email}
                {!methods.emailVerified && (
                  <>
                    {" · "}
                    {verifyState === "sent" ? (
                      <span className="text-emerald-600 dark:text-emerald-400">Link sent, check your inbox</span>
                    ) : (
                      <>
                        <span className="text-amber-600 dark:text-amber-400">Not verified</span>{" "}
                        <button type="button" onClick={verify} disabled={verifyState === "sending"} className="font-medium text-ink underline-offset-4 hover:underline disabled:opacity-60">
                          {verifyState === "sending" ? "Sending…" : "Send link"}
                        </button>
                      </>
                    )}
                  </>
                )}
              </>
            ) : (
              "Not set · sign in with your email too"
            )
          }
          status={methods.password ? "on" : "off"}
        >
          {methods.password ? (
            <>
              <button type="button" onClick={() => setDialog({ kind: "change-password" })} className={`${pill} bg-ink/5 text-ink`}>
                Change
              </button>
              {both && (
                <button type="button" onClick={() => setDialog({ kind: "disconnect", method: "password" })} className={`${pill} bg-ink/5 text-ink`}>
                  Remove
                </button>
              )}
            </>
          ) : (
            <button type="button" onClick={() => setDialog({ kind: "set-password" })} className={`${pill} bg-ink text-paper`}>
              Set a password
            </button>
          )}
        </MethodRow>
      </ul>

      <p aria-live="polite" className={`px-1 text-sm text-pretty ${notice ? "mt-3" : ""} ${notice?.tone === "error" ? "text-red-600 dark:text-red-400" : "text-emerald-600 dark:text-emerald-400"}`}>
        {notice?.text}
      </p>

      <MethodDialog
        dialog={dialog}
        email={email}
        actions={actions}
        onClose={() => setDialog(null)}
        onConfirmNeeded={(next) => setDialog(next)}
        onDone={(text) => {
          setDialog(null);
          setNotice({ tone: "success", text });
        }}
      />
    </section>
  );
}

function MethodRow({ icon, title, detail, status, children }: { icon: ReactNode; title: string; detail: ReactNode; status: "on" | "off"; children: ReactNode }) {
  return (
    <li className="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-2xl px-3 py-2.5">
      <span className="relative grid size-9 shrink-0 place-items-center rounded-full bg-ink/5">
        {icon}
        {status === "on" && (
          <span aria-hidden className="absolute -right-0.5 -bottom-0.5 grid size-3.5 place-items-center rounded-full bg-emerald-500 text-white ring-2 ring-surface">
            <svg viewBox="0 0 24 24" width={9} height={9} fill="none" stroke="currentColor" strokeWidth={4} strokeLinecap="round" strokeLinejoin="round">
              <path d="m5 12 5 5L20 7" />
            </svg>
          </span>
        )}
      </span>
      <span className="min-w-0 flex-1 basis-48">
        <span className="flex items-center gap-2 text-sm font-medium">
          {title}
          <span className="sr-only">{status === "on" ? "(on)" : "(off)"}</span>
        </span>
        <span className="block text-xs break-words text-muted">{detail}</span>
      </span>
      <span className="ml-12 flex gap-2 sm:ml-0">{children}</span>
    </li>
  );
}

/** One window for every change: set or change the password, confirm a removal, or prove it's you first. */
function MethodDialog({
  dialog,
  email,
  actions,
  onClose,
  onConfirmNeeded,
  onDone,
}: {
  dialog: Dialog | null;
  email: string;
  actions: MethodActions;
  onClose: () => void;
  onConfirmNeeded: (dialog: Dialog) => void;
  onDone: (message: string) => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  // Closing clears `dialog`, but the window still needs its content while it fades out.
  const [shown, setShown] = useState<Dialog | null>(dialog);
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  // A fresh window each time: no leftover passwords or errors.
  if (dialog && dialog !== shown) {
    setShown(dialog);
    setCurrent("");
    setNext("");
    setError(undefined);
  }

  const open = Boolean(dialog);
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    if (open && !el.open) el.showModal();
    if (!open && el.open) el.close();
  }, [open]);

  function cancel() {
    if (!busy) onClose();
  }

  function finish(outcome: Outcome, retry: () => Promise<Outcome>, what: string, success: string) {
    if (outcome.ok) onDone(success);
    else if ("confirm" in outcome) onConfirmNeeded({ kind: "confirm", with: outcome.confirm, retry, what, success });
    else setError(outcome.error);
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const d = dialog;
    if (!d || busy) return;
    setError(undefined);
    if ((d.kind === "set-password" || d.kind === "change-password") && next.length < minLength) {
      setError(`Pick a longer password: at least ${minLength} characters.`);
      return;
    }
    setBusy(true);
    try {
      if (d.kind === "set-password") {
        const run = () => actions.addPassword(next);
        finish(await run(), run, "set a password", "Password set. You can now sign in with your email too.");
      } else if (d.kind === "change-password") {
        const run = () => actions.changePassword(current, next);
        finish(await run(), run, "change your password", "Password changed.");
      } else if (d.kind === "disconnect") {
        const run = () => actions.disconnect(d.method);
        const success = d.method === "google" ? "Google disconnected." : "Password removed.";
        finish(await run(), run, d.method === "google" ? "disconnect Google" : "remove your password", success);
      } else {
        const proof = await actions.confirm(d.with === "password" ? current : undefined);
        if (!proof.ok) {
          if ("error" in proof && proof.error) setError(proof.error);
          return;
        }
        const outcome = await d.retry();
        if (outcome.ok) onDone(d.success);
        else setError("error" in outcome ? outcome.error : "Something went wrong. Please try again.");
      }
    } finally {
      setBusy(false);
    }
  }

  const d = dialog ?? shown;
  const content = d && dialogText(d, email);
  const danger = d?.kind === "disconnect";

  return (
    <dialog
      ref={ref}
      aria-labelledby="method-title"
      onCancel={(e) => {
        e.preventDefault();
        cancel();
      }}
      onClick={(e) => {
        if (e.target === e.currentTarget) cancel();
      }}
      className="confirm m-auto w-[min(26rem,calc(100vw-2rem))] rounded-[2rem] bg-paper p-0 text-ink shadow-2xl ring-1 ring-line"
    >
      {d && content && (
        <form onSubmit={submit} className="p-7 sm:p-8">
          <span
            className={`mx-auto grid size-14 place-items-center rounded-full ${danger ? "bg-red-600/10 text-red-600 dark:bg-red-400/10 dark:text-red-400" : "bg-accent/15 text-accent"}`}
          >
            {d.kind === "confirm" && d.with === "google" ? <Image src="/google-g-logo.webp" alt="" width={24} height={24} /> : <KeyIcon size={24} />}
          </span>
          <h2 id="method-title" className="mt-5 text-center text-xl font-semibold tracking-tight text-balance">
            {content.title}
          </h2>
          <p className="mt-2 text-center text-sm text-pretty text-muted">{content.message}</p>

          {/* Lets password managers file the new password under the right account. */}
          {(d.kind === "set-password" || d.kind === "change-password") && <input type="email" name="email" autoComplete="username" value={email} readOnly hidden />}

          <div className="mt-6 flex flex-col gap-3 text-left">
            {(d.kind === "change-password" || (d.kind === "confirm" && d.with === "password")) && (
              <PasswordField label="Current password" value={current} onChange={setCurrent} autoComplete="current-password" disabled={busy} autoFocus />
            )}
            {(d.kind === "set-password" || d.kind === "change-password") && (
              <PasswordField
                label="New password"
                value={next}
                onChange={setNext}
                autoComplete="new-password"
                disabled={busy}
                hint={`At least ${minLength} characters.`}
                autoFocus={d.kind === "set-password"}
              />
            )}
          </div>

          {error && (
            <p role="alert" className="mt-4 text-center text-sm text-balance text-red-600 dark:text-red-400">
              {error}
            </p>
          )}

          <div className="mt-7 grid grid-cols-2 gap-3">
            <button type="button" onClick={cancel} disabled={busy} className={`${pill} bg-surface px-5 py-3 text-sm text-ink`}>
              Cancel
            </button>
            <button type="submit" disabled={busy} className={`${pill} px-5 py-3 text-sm ${danger ? "bg-red-600 text-white" : "bg-ink text-paper"}`}>
              {busy && <Spinner />}
              {busy ? "Working…" : content.confirm}
            </button>
          </div>
        </form>
      )}
    </dialog>
  );
}

function dialogText(d: Dialog, email: string): { title: string; message: ReactNode; confirm: string } {
  switch (d.kind) {
    case "set-password":
      return {
        title: "Set a password",
        message: (
          <>
            Then you can also sign in with <span className="font-medium text-ink">{email}</span> and this password.
          </>
        ),
        confirm: "Set password",
      };
    case "change-password":
      return {
        title: "Change your password",
        message: "Oriel on your other devices may ask you to sign in again afterwards.",
        confirm: "Change password",
      };
    case "disconnect":
      return d.method === "google"
        ? { title: "Disconnect Google?", message: "You'll sign in with your email and password from now on.", confirm: "Disconnect" }
        : { title: "Remove your password?", message: "You'll sign in with Google only from now on.", confirm: "Remove password" };
    case "confirm":
      return {
        title: "Confirm it's you",
        message: d.with === "google" ? `To ${d.what}, sign in with Google once more.` : `To ${d.what}, enter your current password.`,
        confirm: d.with === "google" ? "Continue with Google" : "Confirm",
      };
  }
}

function Spinner() {
  return <span aria-hidden className="size-3.5 animate-spin rounded-full border-2 border-current/25 border-t-current motion-reduce:animate-none" />;
}

function KeyIcon({ size = 17 }: { size?: number }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      <path d="m15.5 7.5 2.3 2.3a1 1 0 0 0 1.4 0l2.1-2.1a1 1 0 0 0 0-1.4L19 4M21 2l-9.6 9.6" />
      <circle cx="7.5" cy="15.5" r="5.5" />
    </svg>
  );
}
