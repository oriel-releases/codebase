import { site } from "@/config/site";
import { AnchorButton } from "@/components/ui/Button";
import { TelegramIcon } from "@/components/ui/icons";

/** Where to get help: opens the Telegram support bot. */
export function SupportCard({ className = "" }: { className?: string }) {
  return (
    <div
      data-spotlight className={`group relative overflow-hidden rounded-3xl border border-line bg-surface p-6 text-center transition-[translate,box-shadow,border-color] duration-300 ease-out hover:-translate-y-0.5 hover:border-[#2AABEE]/40 hover:shadow-xl hover:shadow-[#2AABEE]/10 motion-reduce:transition-none motion-reduce:hover:translate-y-0 sm:flex sm:items-center sm:justify-between sm:gap-6 sm:text-left ${className}`}
    >
      {/* A soft glow behind the icon, so the card reads as a friendly door rather than a form. */}
      <div aria-hidden className="pointer-events-none absolute -top-16 -left-10 size-48 rounded-full bg-accent/15 blur-3xl" />
      <div className="relative flex flex-col items-center gap-4 sm:flex-row">
        <span className="grid size-12 shrink-0 place-items-center rounded-2xl bg-ink text-paper transition-[transform,background-color] duration-300 group-hover:-rotate-6 group-hover:scale-110 group-hover:bg-[#2AABEE]">
          <TelegramIcon width={24} height={24} />
        </span>
        <div>
          <h3 className="font-semibold tracking-tight">Still have questions?</h3>
          <p className="mt-1 text-sm text-balance text-muted">Message the Oriel support bot on Telegram. Bugs, ideas and &ldquo;how do I&hellip;&rdquo; all welcome.</p>
        </div>
      </div>
      <AnchorButton href={site.supportUrl} target="_blank" rel="noopener noreferrer" className="relative mt-5 w-full sm:mt-0 sm:w-auto">
        Open Telegram
      </AnchorButton>
    </div>
  );
}

/**
 * A small link card to support, for pages where people can get stuck (login, download): the Telegram logo, a short
 * question and the action. It lifts and tints with Telegram's blue on hover, and the arrow nudges forward.
 */
export function SupportHint({ children = "Need help?" }: { children?: string }) {
  return (
    <a
      href={site.supportUrl}
      target="_blank"
      rel="noopener noreferrer"
      className="group inline-flex max-w-full items-center gap-3 rounded-full border border-line bg-surface px-5 py-2.5 text-left transition-[translate,box-shadow,background-color,border-color] duration-200 ease-out hover:-translate-y-0.5 hover:border-[#2AABEE]/40 hover:bg-[#2AABEE]/10 hover:shadow-lg hover:shadow-[#2AABEE]/10 motion-reduce:transition-none motion-reduce:hover:translate-y-0 focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-accent"
    >
      <TelegramIcon width={20} height={20} className="shrink-0 text-ink transition-[transform,color] duration-200 group-hover:-rotate-12 group-hover:scale-110 group-hover:text-[#2AABEE]" />
      <span className="min-w-0 text-xs leading-tight">
        <span className="block text-muted">{children}</span>
        <span className="block font-medium text-ink">Message support</span>
      </span>
      <svg
        viewBox="0 0 24 24"
        width={14}
        height={14}
        fill="none"
        stroke="currentColor"
        strokeWidth={2}
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden
        className="shrink-0 text-muted transition-[transform,color] duration-200 group-hover:translate-x-1 group-hover:text-ink"
      >
        <path d="M5 12h14M13 6l6 6-6 6" />
      </svg>
    </a>
  );
}
