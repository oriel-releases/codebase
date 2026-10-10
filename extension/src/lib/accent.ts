import type { Photo } from "./photo";

const hex = /^#[0-9a-f]{6}$/i;

/**
 * The accent color for the page's controls: the vivid color the desktop app picked from the wallpaper (the same one it
 * sets as the Windows accent), and only while the page shows that very photo. Own photos keep the default look.
 */
export function accentFor(photo: Photo, followsDesktop: boolean, desktopPhotoId: string | undefined): string | undefined {
  return followsDesktop && desktopPhotoId === photo.id && photo.color && hex.test(photo.color) ? photo.color : undefined;
}
