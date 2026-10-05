import { release } from "@/config/release";
import { absoluteUrl, routes, site } from "@/config/site";
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
    `- Platform: ${release.requirements}`,
    `- Price: free, sign in with Google, no API key needed`,
    `- Latest version: ${release.version} (${release.releasedOn})`,
    `- Download: ${absoluteUrl(routes.download)}`,
    `- Photos: from Unsplash, credited to their photographers`,
    "",
    "## Features",
    ...features.map((f) => `- ${f.title}: ${f.body}`),
    "",
    "## Getting started",
    ...steps.map((s, i) => `${i + 1}. ${s.title}: ${s.body}`),
    "",
    "## FAQ",
    ...faq.flatMap((q) => [`### ${q.question}`, q.answer, ""]),
    "## Pages",
    `- [Home](${absoluteUrl(routes.home)}): what Oriel does`,
    `- [Download](${absoluteUrl(routes.download)}): get Oriel for Windows`,
    "",
  ].join("\n");
}
