import Link from "next/link";
import type { ComponentProps } from "react";

type Variant = "primary" | "secondary";
type Size = "sm" | "md";

const base =
  "shine inline-flex items-center justify-center gap-2 rounded-full font-medium whitespace-nowrap transition-[color,background-color,translate,scale,box-shadow] duration-200 ease-out hover:-translate-y-0.5 active:translate-y-0 active:scale-[0.98] motion-reduce:transition-none motion-reduce:hover:translate-y-0 focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-accent";

const variants: Record<Variant, string> = {
  primary: "bg-ink text-paper hover:shadow-lg hover:shadow-ink/20",
  secondary: "bg-surface text-ink hover:bg-line hover:shadow-md hover:shadow-ink/10",
};

const sizes: Record<Size, string> = {
  sm: "px-3.5 py-1.5 text-[13px]",
  md: "px-5 py-2.5 text-sm",
};

type StyleProps = { variant?: Variant; size?: Size };

function buttonClass({ variant = "primary", size = "md" }: StyleProps, extra = ""): string {
  return `${base} ${variants[variant]} ${sizes[size]} ${extra}`;
}

type ButtonLinkProps = ComponentProps<typeof Link> & StyleProps;

/** A fully rounded link styled as a button. */
export function ButtonLink({ variant, size, className, ...props }: ButtonLinkProps) {
  return <Link className={buttonClass({ variant, size }, className)} {...props} />;
}

type AnchorButtonProps = ComponentProps<"a"> & StyleProps;

/**
 * A plain anchor styled as a button, for file downloads and in-page jumps (#section).
 * Unlike next/link, the browser re-scrolls on every click, even when the hash is already set.
 */
export function AnchorButton({ variant, size, className, ...props }: AnchorButtonProps) {
  return <a className={buttonClass({ variant, size }, className)} {...props} />;
}
