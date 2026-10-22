import { CircleHelp, Globe, Puzzle } from "lucide-react";
import type { SVGProps } from "react";
import { cn } from "@/lib/utils";

/** Brand marks copied from the website (website/src/components/ui/icons.tsx), one colour so they follow the text. */
type IconProps = SVGProps<SVGSVGElement>;

function WindowsIcon(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" {...props}>
      <path d="M3 3h8.5v8.5H3zM12.5 3H21v8.5h-8.5zM3 12.5h8.5V21H3zM12.5 12.5H21V21h-8.5z" />
    </svg>
  );
}

function LinuxIcon(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" {...props}>
      <path d="M12 2c-2.2 0-3.5 1.9-3.5 4.4 0 1.3.2 2.2-.6 3.6C6.9 11.8 5.5 14 5.5 16.3c0 1.2.4 2 1.2 2.6-.2.9-.1 1.6.5 2 .8.5 1.9.1 2.7-.4.7.3 1.5.5 2.1.5s1.4-.2 2.1-.5c.8.5 1.9.9 2.7.4.6-.4.7-1.1.5-2 .8-.6 1.2-1.4 1.2-2.6 0-2.3-1.4-4.5-2.4-6.3-.8-1.4-.6-2.3-.6-3.6C15.5 3.9 14.2 2 12 2z" />
    </svg>
  );
}

function AppleIcon(props: IconProps) {
  return (
    <svg viewBox="0 0 16 16" fill="currentColor" {...props}>
      <path d="M11.182.008C11.148-.03 9.923.023 8.857 1.18c-1.066 1.156-.902 2.482-.878 2.516s1.52.087 2.475-1.258.762-2.391.728-2.43m3.314 11.733c-.048-.096-2.325-1.234-2.113-3.422s1.675-2.789 1.698-2.854-.597-.79-1.254-1.157a3.7 3.7 0 0 0-1.563-.434c-.108-.003-.483-.095-1.254.116-.508.139-1.653.589-1.968.607-.316.018-1.256-.522-2.267-.665-.647-.125-1.333.131-1.824.328-.49.196-1.422.754-2.074 2.237-.652 1.482-.311 3.83-.067 4.56s.625 1.924 1.273 2.796c.576.984 1.34 1.667 1.659 1.899s1.219.386 1.843.067c.502-.308 1.408-.485 1.766-.472.357.013 1.061.154 1.782.539.571.197 1.111.115 1.652-.105.541-.221 1.324-1.059 2.238-2.758q.52-1.185.473-1.282" />
    </svg>
  );
}

function AndroidIcon(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" {...props}>
      <path
        fillRule="evenodd"
        d="M3.5 19.5a8.5 8.5 0 0 1 17 0zM8.5 14.5a1.25 1.25 0 1 0 0 2.5 1.25 1.25 0 0 0 0-2.5zm7 0a1.25 1.25 0 1 0 0 2.5 1.25 1.25 0 0 0 0-2.5z"
      />
      <path d="M7.4 12.2 5.6 9M16.6 12.2 18.4 9" fill="none" stroke="currentColor" strokeWidth={1.6} strokeLinecap="round" />
    </svg>
  );
}

export function PlatformIcon({ platform, className }: { platform: string; className?: string }) {
  const props = { className: cn("size-4 shrink-0", className), "aria-hidden": true, focusable: false } as const;
  switch (platform) {
    case "windows":
      return <WindowsIcon {...props} />;
    case "macos":
      return <AppleIcon {...props} />;
    case "linux":
      return <LinuxIcon {...props} />;
    case "android":
      return <AndroidIcon {...props} />;
    case "extension":
      return <Puzzle {...props} />;
    case "web":
      return <Globe {...props} />;
    default:
      return <CircleHelp {...props} />;
  }
}
