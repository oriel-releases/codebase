"use client";

import type { ComponentType, SVGProps } from "react";

export type SwitchOption<K extends string> = {
  key: K;
  label: string;
  Icon: ComponentType<SVGProps<SVGSVGElement>>;
};

/** A row of choices in one pill, with a lighter pill that glides under the chosen one (Windows / Linux, Chrome / Edge / Firefox). */
export function SegmentedSwitch<K extends string>({
  options,
  value,
  onChange,
  label,
  bare = false,
}: {
  options: SwitchOption<K>[];
  value: K;
  onChange: (key: K) => void;
  label: string;
  /** No background of its own: the parent draws the pill (see GlidingBox). */
  bare?: boolean;
}) {
  const index = Math.max(
    0,
    options.findIndex((o) => o.key === value),
  );
  const share = 100 / options.length;
  // Three or four choices don't fit side by side on a phone: there each one puts its icon above its label.
  const stack = options.length > 2;
  const round = stack ? "rounded-[1.25rem] sm:rounded-full" : "rounded-full";
  return (
    <div
      role="radiogroup"
      aria-label={label}
      className={`relative isolate grid p-1 ${stack ? "rounded-[1.5rem] sm:rounded-full" : "rounded-full"} ${bare ? "" : "bg-surface"}`}
      style={{
        gridTemplateColumns: `repeat(${options.length}, minmax(0, 1fr))`,
      }}
    >
      <span
        aria-hidden
        className={`absolute inset-y-1 left-1 -z-10 ${round} bg-paper shadow-sm ring-1 ring-line transition-[translate] duration-300 ease-out motion-reduce:transition-none`}
        style={{
          width: `calc(${share}% - ${0.5 / options.length}rem)`,
          translate: `${index * 100}% 0`,
        }}
      />
      {options.map(({ key, label: text, Icon }) => {
        const active = key === value;
        return (
          <button
            key={key}
            type="button"
            role="radio"
            aria-checked={active}
            onClick={() => onChange(key)}
            className={`inline-flex min-w-0 items-center justify-center overflow-hidden ${round} whitespace-nowrap font-medium ${
              stack
                ? "flex-col gap-1 px-2 py-2 text-xs sm:flex-row sm:gap-2 sm:px-4 sm:text-sm"
                : "gap-2 px-5 py-2 text-sm"
            } transition-colors duration-300 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent ${
              active ? "text-ink" : "text-muted"
            }`}
          >
            <Icon width={15} height={15} className="shrink-0" />
            {text}
          </button>
        );
      })}
    </div>
  );
}
