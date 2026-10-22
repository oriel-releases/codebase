/** The Oriel mark, the website's: black on light, inverted to white in dark mode. */
export function Logo({ size = 28, className = "" }: { size?: number; className?: string }) {
  return <img src="/logo.png" alt="Oriel" width={size} height={size} className={`dark:invert ${className}`} />;
}
