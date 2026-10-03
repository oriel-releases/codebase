/**
 * View transition vocabulary, in one place so links and pages agree.
 * Types tag a navigation (see <Link transitionTypes>); names pair the same element across pages.
 */
export const navigation = {
  /** Deeper into the site: Home -> Download. Content slides left. */
  forward: ["nav-forward"],
  /** Back toward Home. Content slides right. */
  back: ["nav-back"],
} as const;

export const sharedElement = {
  logo: "hero-logo",
  downloadButton: "download-cta",
} as const;
