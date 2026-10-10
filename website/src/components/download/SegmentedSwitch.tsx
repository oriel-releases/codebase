"use client";

import type { ComponentType, SVGProps } from "react";

export type SwitchOption<K extends string> = { key: K; label: string; Icon: ComponentType<SVGProps<SVGSVGElement>> };

/** A row of choices in one pill, with a lighter pill that glides under the chosen one (Windows / Linux, Chrome / Edge / Firefox). */
export function SegmentedSwitch<K extends string>({
  options,
  value,
  onChange,
  label,
}: {
  options: SwitchOption<K>[];
  value: K;
  onChange: (key: K) => void;
  label: string;
}) {
  const index = Math.max(0, options.findIndex((o) => o.key === value));
  const share = 100 / options.length;
  return (
    <div
      role="radiogroup"
      aria-label={label}
      className="relative isolate grid rounded-full bg-surface p-1"
      style={{ gridTemplateColumns: `repeat(${options.length}, minmax(0, 1fr))` }}
    >
      <span
        aria-hidden
        className="absolute inset-y-1 left-1 -z-10 rounded-full bg-paper shadow-sm ring-1 ring-line transition-[translate] duration-300 ease-out motion-reduce:transition-none"
        style={{ width: `calc(${share}% - ${0.5 / options.length}rem)`, translate: `${index * 100}% 0` }}
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
            className={`inline-flex items-center justify-center gap-2 rounded-full px-5 py-2 text-sm font-medium transition-colors duration-300 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent ${
              active ? "text-ink" : "text-muted"
            }`}
          >
            <Icon width={15} height={15} />
            {text}
          </button>
        );
      })}
    </div>
  );
}
