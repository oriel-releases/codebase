"use client";

import { doc, getDoc } from "firebase/firestore";
import { useEffect, useState, useSyncExternalStore } from "react";
import { site } from "@/config/site";
import { firestore } from "@/lib/firebase";
import { followCode, rememberFollow } from "@/lib/pending-share";
import { useUser } from "@/lib/use-user";
import { AnchorButton } from "@/components/ui/Button";
import { Avatar } from "@/components/share/Avatar";
import { LinkProblem, Placeholder, SignInFirst, askAgain } from "@/components/share/LinkStates";
import { Frame, WallpaperSkeleton, WallpaperStage, type StagePhoto } from "@/components/wallpaper/WallpaperStage";

/** What a follow code's document gives the page (see firestore.rules). */
type Owner = { uid: string; name: string; picture: string; photo: StagePhoto | null };

const subscribe = () => () => {};
/** The browser tab's title (outside the component: it changes the page, not React state). */
const setTitle = (title: string) => {
  document.title = title;
};

/** The code in the link: /follow/K7MQ2XPA. Null when there isn't a valid one. */
function codeFromPath(path: string): string | null {
  const code = (path.split("/")[2] ?? "").toUpperCase().replace("-", "");
  return followCode.test(code) ? code : null;
}

/**
 * The page a friend lands on: whose wallpaper, what it looks like now, and one button that opens Oriel to follow.
 * Only signed-in visitors see who it is (the code's document is only readable to them).
 */
export function FollowInvite() {
  const path = useSyncExternalStore(subscribe, () => window.location.pathname, () => null);
  const phone = useSyncExternalStore(subscribe, () => /Android|iPhone|iPad|iPod/i.test(navigator.userAgent), () => false);
  const code = path === null ? undefined : codeFromPath(path);
  const user = useUser();
  // undefined while loading, null when the code doesn't exist (any more).
  const [owner, setOwner] = useState<{ code: string; owner: Owner | null } | undefined>(undefined);

  // Remembered right away: if this person installs Oriel first, they follow as soon as the new app signs in.
  useEffect(() => {
    if (code) rememberFollow(code);
  }, [code]);

  const uid = user?.uid;
  useEffect(() => {
    if (!code || !uid) return;
    let cancelled = false;
    getDoc(doc(firestore(), "codes", code))
      .then((snap) => {
        if (cancelled) return;
        const d = snap.data();
        setOwner({
          code,
          owner: d
            ? {
                uid: String(d.uid ?? ""),
                name: String(d.name ?? ""),
                picture: String(d.picture ?? ""),
                photo:
                  typeof d.photoId === "string" && typeof d.photoPath === "string"
                    ? { id: d.photoId, path: d.photoPath, by: String(d.photographer ?? ""), handle: String(d.photographerHandle ?? "") }
                    : null,
              }
            : null,
        });
      })
      .catch(() => !cancelled && setOwner({ code, owner: null }));
    return () => {
      cancelled = true;
    };
  }, [code, uid]);

  const found = owner?.code === code ? owner?.owner : undefined;
  const name = found?.name ?? "";
  const sentence = name ? `${name} shares their wallpaper with you` : "A wallpaper is shared with you";
  useEffect(() => {
    if (found) setTitle(`${sentence} · ${site.name}`);
  }, [found, sentence]);

  if (code === undefined || user === undefined) return <WallpaperSkeleton />;
  if (code === null) return <LinkProblem title="This link looks broken" note={askAgain} />;
  if (user === null) return <SignInFirst what="whose wallpaper this is" />;
  if (found === undefined) return <WallpaperSkeleton />;
  if (found === null)
    return <LinkProblem title="This link doesn't work any more" note={<>Your friend may have turned sharing off. {askAgain}</>} />;
  if (found.uid === user.uid)
    return <LinkProblem title="This is your own link" note="Send it to friends: with it they get your wallpaper, and every change after it." />;

  const button = name ? `Follow ${name}'s wallpaper` : "Follow this wallpaper";
  const appLink = `oriel://follow?code=${code}`;
  const avatar = (size: string) => <Avatar picture={found.picture} name={name} className={size} />;

  if (found.photo) {
    return (
      <WallpaperStage
        photo={found.photo}
        label={sentence}
        labelAs="h1"
        badge={avatar("flex size-6 items-center justify-center rounded-full")}
        appLink={appLink}
        again="open this link again"
        phone={phone}
        button={button}
      />
    );
  }

  // No wallpaper on the code yet: their picture (or the Oriel mark) in its place.
  return (
    <Frame
      photo={
        <Placeholder
          mark={found.picture ? avatar("flex size-16 items-center justify-center rounded-full bg-ink text-2xl font-semibold text-paper sm:size-20") : undefined}
        >
          <h1 className="text-xl font-semibold tracking-tight text-balance sm:text-2xl">{sentence}</h1>
        </Placeholder>
      }
      action={
        phone ? (
          <span className="max-w-sm">Open this page on your Windows or Linux computer to follow.</span>
        ) : (
          <AnchorButton href={appLink} className="w-full py-3.5 text-base">
            {button}
          </AnchorButton>
        )
      }
      note={phone ? null : <>Nothing happens? Get {site.name} (it&apos;s free) first, then open this link again.</>}
    />
  );
}
