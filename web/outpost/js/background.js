/* ==================================================================
   VERSO — Outpost background

   The user's wallpaper, drawn by this page (it replaces the layer behind it, so it has to). The host
   maps the picked file's folder to https://verso.bg.example/ and hands back the URL; kind "none" shows
   the palette's own gradient (--stage), so a fresh install still looks right.
================================================================== */

import * as Host from "/shell/js/host.js";

export function start() {
  const bg = document.getElementById("bg");
  const { kind, url } = Host.get().getBackground() ?? { kind: "none", url: null };
  if (!url) return;

  if (kind === "video") {
    const video = document.createElement("video");
    video.src = url;
    video.autoplay = true;
    video.loop = true;
    video.muted = true;
    video.playsInline = true;
    video.disablePictureInPicture = true;
    bg.appendChild(video);
  } else if (kind === "gif" || kind === "image") {
    const img = document.createElement("img");
    img.src = url;
    img.alt = "";
    bg.appendChild(img);
  } else if (kind === "interactive") {
    // Look-only: the mouse is not mirrored into an interactive wallpaper, and the widgets are on top.
    const frame = document.createElement("iframe");
    frame.src = url;
    frame.title = "Wallpaper";
    bg.appendChild(frame);
  }
}
