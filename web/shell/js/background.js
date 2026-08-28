/* ==================================================================
   VERSO — background

   Renders whatever the user picked from the tray's "Set background…"
   dialog into the stage, when the stage is where it belongs.

   "image" never reaches here — it goes straight to the real Windows
   wallpaper on the native side (see DesktopWallpaper.SetImage), so
   the web layer has nothing to draw for it. This module only ever
   handles "video" and "interactive".

   The stage is hidden by CSS in overlay mode (see the LAYERS block in
   css/desk.css — a reshaped window has no pixels outside the widget
   panels to draw a background into at all), so this is a no-op there
   rather than silently loading a file nobody will ever see.
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
  } else if (background.kind === "interactive") {
    const frame = document.createElement("iframe");
    frame.src = background.url;
    frame.title = "Background";
    stage.appendChild(frame);
  }
}
