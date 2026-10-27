import { readFile } from "node:fs/promises";
import { join } from "node:path";
import { ImageResponse } from "next/og";
import { site } from "@/config/site";

export const dynamic = "force-static";

export const alt = "Explore: new free wallpapers every hour";
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";

/** The link preview of Explore, like Wallpaper of the day's. */
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
        <img src={`data:image/png;base64,${logo}`} width={120} height={120} alt="" />
        <div style={{ marginTop: 44, fontSize: 64, fontWeight: 600, letterSpacing: -2 }}>Explore</div>
        <div style={{ marginTop: 16, fontSize: 34, color: "#6b6b6b" }}>{`New free wallpapers every hour · ${site.name}`}</div>
      </div>
    ),
    size,
  );
}
