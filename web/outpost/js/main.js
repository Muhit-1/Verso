/* ==================================================================
   VERSO — Outpost desk: boot.

   The desk is a shell (background, layout engine, round menu) plus independent widgets. Each widget module
   registers itself; the framework starts the ones the host says are on and follows live changes — from the
   dashboard or from the round menu — one widget at a time.
================================================================== */

import * as Host from "/shell/js/host.js";
import * as Core from "./core.js";
import * as Input from "./input.js";
import * as Layout from "./layout.js";
import * as Framework from "./framework.js";
import * as Menu from "./menu.js";
import * as Background from "./background.js";

// every widget registers itself on import; none of them imports another
import "./widgets/clock.js";
import "./widgets/nowplaying.js";
import "./widgets/pomodoro.js";
import "./widgets/stopwatch.js";
import "./widgets/notes.js";
import "./widgets/launcher.js";
import "./widgets/fireflies.js";

/** The host owns the palette. A desk that saved one before that existed hands it over once. */
async function applyHostPalette() {
  const host = Host.get();
  let theme = "";
  try { theme = await host.getOutpostTheme(); } catch { /* fall back below */ }

  const saved = Core.getState().theme;
  if (!theme && saved) {
    theme = saved;
    host.setOutpostTheme(saved);
  }
  Core.applyPalette(theme || Core.PALETTES[0]);
}

async function start() {
  await Host.connect();
  document.body.dataset.host = Host.get().kind;

  Core.loadState();
  await applyHostPalette();

  // fonts first: the default positions depend on the size of the big clock
  await document.fonts.ready;

  Input.start();
  Background.start();
  Layout.start();
  Menu.start();

  // the host calls this when the dashboard switches a widget (see DeskWindow.NotifyWidgetsChanged)
  window.__verso.widgetsChanged = () => Framework.sync();
  // the dashboard's "Reset positions" button
  window.__verso.resetLayout = () => Layout.resetLayout();
  // the palette was changed in the dashboard
  window.__verso.themeChanged = () => applyHostPalette();
  // apps or boxes were added, moved or removed (here or in the dashboard)
  window.__verso.appsChanged = () => Framework.emit("appsChanged");
  // any other host event ("notesChanged", "editorClosed"...) goes to the widgets that listen for it
  window.__verso.emit = name => Framework.emit(name);

  await Framework.sync();

  // A paused desk (a maximised window in front) does not run the host's pushes until it is shown again;
  // catch up on everything then, so nothing changed in the meantime is missed.
  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState !== "visible") return;
    applyHostPalette();
    Framework.sync();
    Framework.emit("appsChanged");
    Framework.emit("notesChanged");
  });
}

start();
