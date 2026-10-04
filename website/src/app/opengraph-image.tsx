import { readFile } from "node:fs/promises";
import { join } from "node:path";
import { ImageResponse } from "next/og";
import { site } from "@/config/site";

export const dynamic = "force-static";

export const alt = `${site.name}: ${site.tagline}`;
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";

/** Social preview: the mark and the tagline on paper, same as the site. */
export default async function OpengraphImage() {
  const logo = await readFile(join(process.cwd(), "public/logo.png"), "base64");

  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          justifyContent: "center",
          background: "#fafafa",
          color: "#141414",
        }}
      >
        {/* eslint-disable-next-line @next/next/no-img-element -- ImageResponse needs a plain img */}
        <img src={`data:image/png;base64,${logo}`} width={160} height={160} alt="" />
        <div style={{ marginTop: 48, fontSize: 72, fontWeight: 600, letterSpacing: -2 }}>{site.name}</div>
        <div style={{ marginTop: 16, fontSize: 36, color: "#6b6b6b" }}>{site.tagline}</div>
      </div>
    ),
    size,
  );
}
