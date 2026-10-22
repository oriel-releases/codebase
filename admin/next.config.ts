import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Every page is static: `next build` writes plain files to out/ for Cloudflare Pages. All data is read in the
  // browser, and firestore.rules decides who may read it.
  output: "export",
  images: { unoptimized: true },
};

export default nextConfig;
