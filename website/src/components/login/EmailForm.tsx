"use client";

import { useId, useState, type FormEvent, type ReactNode } from "react";
import { createAccount, sendPasswordReset, signInError, signInWithPassword, type SignedIn } from "@/lib/sign-in";
import { minPasswordLength } from "@/lib/sign-in-methods";

export type EmailMode = "signin" | "signup" | "reset";

const field =
  "w-full rounded-2xl border border-line bg-surface px-4 py-3 text-sm text-ink placeholder:text-muted/70 transition-colors focus-visible:border-accent focus-visible:outline-2 focus-visible:outline-offset-0 focus-visible:outline-accent/40 disabled:opacity-60";
const label = "mb-1.5 block text-xs font-medium text-muted";
const linkButton = "font-medium text-ink underline-offset-4 hover:underline disabled:opacity-60";

/**
 * Email and password: sign in, create an account, or get a reset link. It only signs in; what happens next (open the
 * app, go back to the account page) is the caller's.
 */
export function EmailForm({
  mode,
  onMode,
  email,
  onEmail,
  autoFocus,
  onSignedIn,
}: {
  mode: EmailMode;
  onMode: (mode: EmailMode) => void;
  email: string;
  onEmail: (email: string) => void;
  autoFocus?: boolean;
  onSignedIn: (result: SignedIn) => void | Promise<void>;
}) {
  const id = useId();
  const [name, setName] = useState("");
  const [password, setPassword] = useState("");
  const [working, setWorking] = useState(false);
  const [error, setError] = useState<string>();
  const [resetSent, setResetSent] = useState(false);

  function switchTo(next: EmailMode) {
    setError(undefined);
    setResetSent(false);
    setPassword("");
    onMode(next);
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (working) return;
    setError(undefined);
    if (mode === "signup" && password.length < minPasswordLength) {
      setError(`Pick a longer password: at least ${minPasswordLength} characters.`);
      return;
    }
    setWorking(true);
    try {
      if (mode === "reset") {
        await sendPasswordReset(email);
        setResetSent(true);
      } else {
        const result = mode === "signup" ? await createAccount(name, email, password) : await signInWithPassword(email, password);
        await onSignedIn(result);
      }
    } catch (err) {
      // Asking for a reset link only fails for a mistyped address or a network problem: say so the same way.
      setError(signInError(err));
    } finally {
      setWorking(false);
    }
  }

  if (mode === "reset" && resetSent) {
    return (
      <div role="status" className="w-full rounded-3xl bg-surface p-5 text-center">
        <p className="text-sm font-medium">Check your inbox</p>
        <p className="mt-1.5 text-sm text-balance text-muted">
          If there&apos;s an Oriel account for <span className="font-medium text-ink">{email.trim()}</span>, a link to set a new password is on its way.
        </p>
        <button type="button" onClick={() => switchTo("signin")} className={`${linkButton} mt-4 text-sm`}>
          Back to sign in
        </button>
      </div>
    );
  }

  const submitLabel = { signin: "Sign in", signup: "Create account", reset: "Send reset link" }[mode];
  const workingLabel = { signin: "Signing in…", signup: "Creating your account…", reset: "Sending…" }[mode];

  return (
    <form onSubmit={submit} className="flex w-full flex-col gap-3 text-left">
      {mode === "reset" && <p className="text-center text-sm text-balance text-muted">Enter your email and we&apos;ll send you a link to set a new password.</p>}

      {mode === "signup" && (
        <div>
          <label htmlFor={`${id}-name`} className={label}>
            Name
          </label>
          <input
            id={`${id}-name`}
            name="name"
            autoComplete="name"
            maxLength={60}
            value={name}
            onChange={(e) => setName(e.target.value)}
            disabled={working}
            placeholder="What friends see"
            className={field}
          />
        </div>
      )}

      <div>
        <label htmlFor={`${id}-email`} className={label}>
          Email
        </label>
        <input
          id={`${id}-email`}
          name="email"
          type="email"
          inputMode="email"
          autoComplete={mode === "signup" ? "email" : "username"}
          autoCapitalize="none"
          spellCheck={false}
          required
          autoFocus={autoFocus}
          value={email}
          onChange={(e) => onEmail(e.target.value)}
          disabled={working}
          placeholder="you@example.com"
          className={field}
        />
      </div>

      {mode !== "reset" && (
        <PasswordField
          label="Password"
          action={
            mode === "signin" && (
              <button type="button" onClick={() => switchTo("reset")} className="text-xs font-medium text-muted underline-offset-4 hover:text-ink hover:underline">
                Forgot password?
              </button>
            )
          }
          value={password}
          onChange={setPassword}
          autoComplete={mode === "signup" ? "new-password" : "current-password"}
          disabled={working}
          hint={mode === "signup" ? `At least ${minPasswordLength} characters.` : undefined}
        />
      )}

      {error && (
        <p role="alert" className="text-center text-sm text-balance text-red-600 dark:text-red-400">
          {error}
        </p>
      )}

      <button
        type="submit"
        disabled={working}
        className="mt-1 inline-flex w-full items-center justify-center gap-2 rounded-full bg-surface px-6 py-3 text-sm font-medium text-ink ring-1 ring-line transition-[background-color,scale] active:scale-[0.98] disabled:opacity-60 motion-reduce:transition-none"
      >
        {working && <span aria-hidden className="size-4 animate-spin rounded-full border-2 border-ink/15 border-t-accent motion-reduce:animate-none" />}
        {working ? workingLabel : submitLabel}
      </button>

      <p className="text-center text-sm text-muted">
        {mode === "signin" ? (
          <>
            New to Oriel?{" "}
            <button type="button" onClick={() => switchTo("signup")} disabled={working} className={linkButton}>
              Create an account
            </button>
          </>
        ) : (
          <>
            {mode === "signup" ? "Already have an account? " : "Remembered it? "}
            <button type="button" onClick={() => switchTo("signin")} disabled={working} className={linkButton}>
              Sign in
            </button>
          </>
        )}
      </p>
    </form>
  );
}

/** A password input with a show/hide toggle; used here and on the account page. */
export function PasswordField({
  label: text,
  action,
  value,
  onChange,
  autoComplete,
  disabled,
  hint,
  autoFocus,
}: {
  label: string;
  /** A small link beside the label (such as "Forgot password?"). */
  action?: ReactNode;
  value: string;
  onChange: (value: string) => void;
  autoComplete: "current-password" | "new-password";
  disabled?: boolean;
  hint?: string;
  autoFocus?: boolean;
}) {
  const id = useId();
  const [shown, setShown] = useState(false);
  return (
    <div>
      <div className="flex items-baseline justify-between gap-3">
        <label htmlFor={id} className={label}>
          {text}
        </label>
        {action}
      </div>
      <div className="relative">
        <input
          id={id}
          name={autoComplete === "new-password" ? "new-password" : "password"}
          type={shown ? "text" : "password"}
          autoComplete={autoComplete}
          required
          autoFocus={autoFocus}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          disabled={disabled}
          aria-describedby={hint ? `${id}-hint` : undefined}
          className={`${field} pr-12`}
        />
        <button
          type="button"
          onClick={() => setShown(!shown)}
          aria-label={shown ? "Hide password" : "Show password"}
          aria-pressed={shown}
          className="absolute inset-y-0 right-1.5 my-auto grid size-9 place-items-center rounded-full text-muted transition-colors hover:text-ink focus-visible:outline-2 focus-visible:outline-accent"
        >
          <svg viewBox="0 0 24 24" width={18} height={18} fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden>
            {shown ? (
              <>
                <path d="M10.7 5.1A10.8 10.8 0 0 1 12 5c7 0 10 7 10 7a13.2 13.2 0 0 1-1.7 2.7M6.6 6.6A13.5 13.5 0 0 0 2 12s3 7 10 7a9.7 9.7 0 0 0 5.4-1.6" />
                <path d="M9.9 9.9a3 3 0 0 0 4.2 4.2M2 2l20 20" />
              </>
            ) : (
              <>
                <path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7S2 12 2 12Z" />
                <circle cx="12" cy="12" r="3" />
              </>
            )}
          </svg>
        </button>
      </div>
      {hint && (
        <p id={`${id}-hint`} className="mt-1.5 text-xs text-muted">
          {hint}
        </p>
      )}
    </div>
  );
}
