import { lastReleasedOn, releases } from "@/config/release";
import { absoluteUrl, routes, site } from "@/config/site";
import { extensionFaq, extensionFeatures } from "@/content/extension";
import { faq, features, steps } from "@/content/home";

/**
 * /llms.txt: a plain-text summary for AI answer engines (GEO), built from the same
 * content the pages render so the two never drift apart.
 */
export function buildLlmsTxt(): string {
  return [
    `# ${site.name}`,
    "",
    `> ${site.description}`,
    "",
    "## Key facts",
    `- Platforms: Windows (${releases.windows.requirements}); Linux (${releases.linux.requirements})`,
    `- Price: free, sign in with Google, no API key needed`,
    `- Latest version: ${releases.windows.version} for Windows, ${releases.linux.version} for Linux (${lastReleasedOn})`,
    `- Download: ${absoluteUrl(routes.download)}`,
    `- Browser extension: free new tab extension for Chrome, Edge, Brave, Opera, Vivaldi and Firefox (${absoluteUrl(routes.extension)})`,
    `- Privacy: no ads, no analytics, no tracking; see ${absoluteUrl(routes.privacy)}`,
    `- Photos: from Unsplash, credited to their photographers`,
    "",
    "## Features",
    ...features.map((f) => `- ${f.title}: ${f.body}`),
    "",
    "## Browser extension",
    ...extensionFeatures.map((f) => `- ${f.title}: ${f.body}`),
    "",
    ...extensionFaq.flatMap((q) => [`### ${q.question}`, q.answer, ""]),
    "## Getting started",
    ...steps.map((s, i) => `${i + 1}. ${s.title}: ${s.body}`),
    "",
    "## FAQ",
    ...faq.flatMap((q) => [`### ${q.question}`, q.answer, ""]),
    "## Pages",
    `- [Home](${absoluteUrl(routes.home)}): what Oriel does`,
    `- [Download](${absoluteUrl(routes.download)}): get Oriel for Windows or Linux`,
    `- [Extension](${absoluteUrl(routes.extension)}): the new tab extension for Chrome, Edge and Firefox`,
    `- [Privacy](${absoluteUrl(routes.privacy)}): what is stored and how to remove it`,
    "",
  ].join("\n");
}
