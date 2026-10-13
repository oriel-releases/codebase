"use client";

import { useState } from "react";

/**
 * Someone's Google profile picture, or their initial when there is none (or it doesn't load). Only pictures on
 * Google's image server are ever loaded (firestore.rules only lets those be stored; this checks again).
 */
export function Avatar({ picture, name, className }: { picture: string; name: string; className: string }) {
  const [failed, setFailed] = useState(false);
  const initial = name ? name.charAt(0).toUpperCase() : "★";
  if (!picture.startsWith("https://lh3.googleusercontent.com/") || failed) return <span className={className}>{initial}</span>;
  return (
    // eslint-disable-next-line @next/next/no-img-element
    <img src={picture} alt="" referrerPolicy="no-referrer" onError={() => setFailed(true)} className={`${className} object-cover`} />
  );
}
