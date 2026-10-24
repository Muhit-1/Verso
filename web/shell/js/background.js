/* ==================================================================
   VERSO — background

   Renders whatever the user picked from the tray's "Set background…"
   dialog into the stage, when the stage is where it belongs.

   "image" never reaches here — it goes straight to the real Windows
   wallpaper on the native side (see DesktopWallpaper.SetImage), so
   the web layer has nothing to draw for it. This module handles
   "video", "gif" and "interactive".

   The stage only ever has pixels to draw into on the "wallpaper" and
   "background" layers (see the LAYERS block in css/desk.css) — never
   "overlay", which is reshaped down to just the widget panels. A
   background layer's own window exists specifically so there is
   somewhere for this to draw on builds where wallpaper mode is not
   reachable; see DeskMode.Background in DeskWindow.cs.
================================================================== */

import * as Host from "./host.js";

export function start() {
  if (document.documentElement.dataset.layer === "overlay") {
    return;
  }

  const stage = document.querySelector(".stage");
  if (!stage) {
    return;
  }

  const background = Host.get().getBackground?.() ?? { kind: "none", url: null };
  if (!background.url) {
    return;
  }

  if (background.kind === "video") {
    const video = document.createElement("video");
    video.src = background.url;
    video.autoplay = true;
    video.loop = true;
    video.muted = true;
    video.playsInline = true;
    stage.appendChild(video);
  } else if (background.kind === "gif") {
    // GIF animation only ever plays through <img> — a <video> element
    // will not decode one at all, regardless of its src's actual format.
    const img = document.createElement("img");
    img.src = background.url;
    img.alt = "";
    stage.appendChild(img);
  } else if (background.kind === "interactive") {
    const frame = document.createElement("iframe");
    // someone else's page: no popups, no navigating the desk away, no referrer
    frame.setAttribute("sandbox", "allow-scripts allow-same-origin");
    frame.referrerPolicy = "no-referrer";
    frame.src = background.url;
    frame.title = "Background";
    stage.appendChild(frame);
  }
}
