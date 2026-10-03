import Image from "next/image";
import { site } from "@/config/site";

type Props = { size?: number; priority?: boolean; className?: string };

/** The Oriel mark. Black on light, inverted to white in dark mode. `className` can resize it responsively. */
export function Logo({ size = 28, priority = false, className = "" }: Props) {
  return (
    <Image
      src="/logo.png"
      alt={`${site.name} logo`}
      width={size}
      height={size}
      priority={priority}
      className={`dark:invert ${className}`}
    />
  );
}
