"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { ImagePlus } from "lucide-react";
import { sync } from "@/content/home";
import { Button } from "@/components/ui/Button";

/**
 * "Try yours": a visitor's own photo, put on every device of the showcase. It is an object URL of the file, never
 * uploaded, gone with the tab. The hero's button picks it, the devices show it, and both read it from here.
 */

type OwnPhotoState = {
  /** The photo's object URL, or null for Oriel's own photos. */
  url: string | null;
  /** The last photo couldn't be decoded. */
  failed: boolean;
  /** Opens the file picker. */
  pick: () => void;
  /** Shows a file (picked or dropped onto the devices); anything but an image is ignored. */
  show: (file: File | undefined) => void;
  /** Back to Oriel's photos. */
  reset: () => void;
  /** A screen couldn't decode it (HEIC in most browsers, a broken file). */
  onError: () => void;
};

const OwnPhoto = createContext<OwnPhotoState>({
  url: null,
  failed: false,
  pick: () => {},
  show: () => {},
  reset: () => {},
  onError: () => {},
});

export const useOwnPhoto = () => useContext(OwnPhoto);

/** The id of the devices' section, which a new photo scrolls into view. */
export const devicesId = "devices";

export function OwnPhotoProvider({ children }: { children: ReactNode }) {
  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const current = useRef<string | null>(null);
  const picker = useRef<HTMLInputElement>(null);

  // Each new photo frees the last one's object URL, and so does leaving the page.
  const replace = useCallback((next: string | null) => {
    if (current.current) URL.revokeObjectURL(current.current);
    current.current = next;
    setUrl(next);
  }, []);
  useEffect(() => () => replace(null), [replace]);

  const show = useCallback(
    (file: File | undefined) => {
      if (!file?.type.startsWith("image/")) return;
      setFailed(false);
      replace(URL.createObjectURL(file));
      const reduce = window.matchMedia(
        "(prefers-reduced-motion: reduce)",
      ).matches;
      document
        .getElementById(devicesId)
        ?.scrollIntoView({ behavior: reduce ? "auto" : "smooth", block: "center" });
    },
    [replace],
  );

  const state: OwnPhotoState = {
    url,
    failed,
    pick: () => picker.current?.click(),
    show,
    reset: () => replace(null),
    onError: () => {
      replace(null);
      setFailed(true);
    },
  };

  return (
    <OwnPhoto.Provider value={state}>
      {children}
      <input
        ref={picker}
        type="file"
        accept="image/*"
        hidden
        onChange={(e) => {
          show(e.currentTarget.files?.[0]);
          // Picking the same file again still counts as a change.
          e.currentTarget.value = "";
        }}
      />
    </OwnPhoto.Provider>
  );
}

/** Opens the file picker: "Try yours", then "Try another" once a photo of the visitor's is up. */
export function TryYoursButton({
  variant,
}: {
  variant?: "primary" | "secondary";
}) {
  const { url, pick } = useOwnPhoto();
  return (
    <Button variant={variant} onClick={pick}>
      <ImagePlus size={16} aria-hidden />
      {url ? sync.another : sync.cta}
    </Button>
  );
}
