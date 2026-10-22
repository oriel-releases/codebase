import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar";
import type { Account } from "@/lib/data";
import { initials } from "@/lib/format";

/** A person's picture, name and email, in a row of a list or table. */
export function UserCell({ account, size = "size-8" }: { account: Account; size?: string }) {
  return (
    <div className="flex min-w-0 items-center gap-3">
      <Avatar className={size}>
        <AvatarImage src={account.photoURL || undefined} alt="" referrerPolicy="no-referrer" />
        <AvatarFallback className="text-xs">{initials(account.displayName, account.email)}</AvatarFallback>
      </Avatar>
      <div className="min-w-0">
        <p className="truncate text-sm font-medium">{account.displayName || account.email.split("@")[0]}</p>
        <p className="truncate text-xs text-muted-foreground">{account.email}</p>
      </div>
    </div>
  );
}
