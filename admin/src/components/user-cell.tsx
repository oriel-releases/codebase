import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar";
import type { Account } from "@/lib/data";
import { initials } from "@/lib/format";

/** A person's picture, name and email, in a row of a list or table. */
export function UserCell({ account, size = "size-8", gold = false }: { account: Account; size?: string; gold?: boolean }) {
  const avatar = (
      <Avatar className={size}>
        <AvatarImage src={account.photoURL || undefined} alt="" referrerPolicy="no-referrer" />
        <AvatarFallback className="text-xs">{initials(account.displayName, account.email)}</AvatarFallback>
      </Avatar>
  );
  return (
    <div className="flex min-w-0 items-center gap-3">
      {gold ? (
        // A golden, glinting ring: a conic gradient of golds around the picture, with a warm glow.
        <span className="shrink-0 rounded-full bg-[conic-gradient(from_30deg,#a8741a,#ffe9a3,#d4a017,#fff6d0,#b8860b,#f5d77a,#a8741a)] p-[2.5px] shadow-[0_0_14px_2px_rgba(234,179,8,0.55),0_0_3px_rgba(255,236,160,0.9)]">
          {avatar}
        </span>
      ) : (
        avatar
      )}
      <div className="min-w-0">
        <p className="truncate text-sm font-medium">{account.displayName || account.email.split("@")[0]}</p>
        <p className="truncate text-xs text-muted-foreground">{account.email}</p>
      </div>
    </div>
  );
}
