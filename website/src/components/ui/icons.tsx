import type { SVGProps } from "react";

type IconProps = SVGProps<SVGSVGElement>;

const defaults: IconProps = { width: 18, height: 18, "aria-hidden": true, focusable: false };

export function WindowsIcon(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" {...defaults} {...props}>
      <path d="M3 3h8.5v8.5H3zM12.5 3H21v8.5h-8.5zM3 12.5h8.5V21H3zM12.5 12.5H21V21h-8.5z" />
    </svg>
  );
}

export function DownloadIcon(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" {...defaults} {...props}>
      <path d="M12 4v11m0 0-4.5-4.5M12 15l4.5-4.5M5 20h14" />
    </svg>
  );
}

export function ArrowDownIcon(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" {...defaults} {...props}>
      <path d="M12 5v14m0 0-6-6m6 6 6-6" />
    </svg>
  );
}

export function PlusIcon(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" {...defaults} {...props}>
      <path d="M12 5v14M5 12h14" />
    </svg>
  );
}

export function LinuxIcon(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" {...defaults} {...props}>
      <path d="M12 2c-2.2 0-3.5 1.9-3.5 4.4 0 1.3.2 2.2-.6 3.6C6.9 11.8 5.5 14 5.5 16.3c0 1.2.4 2 1.2 2.6-.2.9-.1 1.6.5 2 .8.5 1.9.1 2.7-.4.7.3 1.5.5 2.1.5s1.4-.2 2.1-.5c.8.5 1.9.9 2.7.4.6-.4.7-1.1.5-2 .8-.6 1.2-1.4 1.2-2.6 0-2.3-1.4-4.5-2.4-6.3-.8-1.4-.6-2.3-.6-3.6C15.5 3.9 14.2 2 12 2z" />
    </svg>
  );
}
