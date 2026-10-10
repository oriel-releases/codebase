import { downloadPage } from "@/content/download";
import Image from "next/image";
import { RevealImage } from "@/components/ui/RevealImage";

const utm = "utm_source=oriel&utm_medium=referral";

/**
 * What Oriel does, shown rather than told: a desktop with a fresh photo on it and Oriel's little note about it.
 * One hotlinked Unsplash photo (credited on the image, as Unsplash asks), drawn at the size it is shown.
 */
export function DesktopPreview() {
  const p = downloadPage.preview;
  return (
    <figure className="relative isolate aspect-[16/10] w-full overflow-hidden rounded-[2rem] bg-surface text-white shadow-2xl shadow-ink/15 ring-1 ring-ink/10 sm:aspect-[2/1]">
      <RevealImage
        src={`https://images.unsplash.com/${p.path}?w=1600&q=80&fm=jpg&auto=format&fit=crop&crop=entropy`}
        alt={p.alt}
        className="absolute inset-0 -z-10 size-full object-cover"
      />
      <div aria-hidden className="absolute inset-0 -z-10 bg-gradient-to-t from-black/60 via-black/0 to-black/10" />

      {/* Oriel's note, as it looks in the corner of a real desktop. */}
      <div className="absolute top-5 right-5 flex items-center gap-3 rounded-2xl bg-black/35 py-2.5 pr-4 pl-2.5 text-left ring-1 ring-white/15 backdrop-blur-md sm:top-7 sm:right-7">
        <span className="flex size-8 items-center justify-center rounded-xl bg-white">
          {/* Always the black mark here: it sits on a white tile in both themes. */}
          <Image src="/logo.png" alt="" width={18} height={18} />
        </span>
        <span>
          <span className="block text-[13px] font-medium">New wallpaper</span>
          <span className="block text-xs text-white/70">Today, 09:00 · Nature</span>
        </span>
      </div>

      <figcaption className="absolute inset-x-0 bottom-0 p-5 text-left text-sm text-white/80 sm:px-7 sm:pb-6">
        Photo by{" "}
        <a href={`https://unsplash.com/@${p.handle}?${utm}`} target="_blank" rel="noopener noreferrer" className="font-medium text-white underline underline-offset-4">
          {p.photographer}
        </a>{" "}
        on{" "}
        <a href={`https://unsplash.com/photos/${p.id}?${utm}`} target="_blank" rel="noopener noreferrer" className="font-medium text-white underline underline-offset-4">
          Unsplash
        </a>
      </figcaption>
    </figure>
  );
}
