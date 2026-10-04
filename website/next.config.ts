import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Every page is static, so `next build` writes plain files to out/ for Firebase Hosting.
  output: "export",
  // No image server on static hosting; the only image is the small logo.
  images: { unoptimized: true },
};

export default nextConfig;
