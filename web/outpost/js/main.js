/* ==================================================================
   VERSO — Outpost desk: boot.

   Order matters: host first (everything reads from it), then saved state and palette (so nothing
   paints against a half-applied palette), then the wallpaper, widgets and layout.
================================================================== */

import * as Host from "/shell/js/host.js";
import * as Core from "./core.js";
import * as Input from "./input.js";
import * as Layout from "./layout.js";
import * as Widgets from "./widgets.js";
import * as Background from "./background.js";
import * as Fireflies from "./fireflies.js";

async function start() {
  await Host.connect();
  document.body.dataset.host = Host.get().kind;

  Core.loadState();
  Core.applyPalette(Core.getState().theme);

  Input.start();
  Background.start();
  Widgets.start();
  Layout.start();
  Fireflies.init();
  Fireflies.setEnabled(Core.getState().flies !== false);
}

start();
