import { extension, type ExtensionStore } from "@/config/extension";
import { GlobeIcon, PuzzleIcon } from "@/components/ui/icons";

/** One card per browser store: a link once the listing is approved, otherwise a quiet "Coming soon" so nothing points at a dead page. */
export function ExtensionStores() {
  return (
    <ul className="grid w-full gap-3 sm:grid-cols-3 sm:gap-4">
      {extension.stores.map((store) => (
        <StoreCard key={store.id} store={store} />
      ))}
    </ul>
  );
}

function StoreCard({ store }: { store: ExtensionStore }) {
  const Icon = store.id === "firefox" ? GlobeIcon : PuzzleIcon;
  const body = (
    <>
      <span className="flex size-10 items-center justify-center rounded-full bg-ink/5">
        <Icon width={20} height={20} />
      </span>
      <span className="mt-4 block text-sm font-medium">{store.name}</span>
      <span className="mt-1 block text-xs text-pretty text-muted">{store.browsers}</span>
      <span className={`mt-4 inline-block rounded-full px-3 py-1 text-xs font-medium ${store.url ? "bg-ink text-paper" : "bg-surface text-muted"}`}>
        {store.url ? "Add to browser" : "Coming soon"}
      </span>
    </>
  );
  const card = "flex h-full flex-col items-center rounded-[2rem] bg-surface p-6 text-center";
  return (
    <li>
      {store.url ? (
        <a href={store.url} target="_blank" rel="noopener noreferrer" data-spotlight className={`${card} transition-[translate,box-shadow] duration-200 hover:-translate-y-1 hover:shadow-xl hover:shadow-ink/10`}>
          {body}
        </a>
      ) : (
        <div className={card}>{body}</div>
      )}
    </li>
  );
}
