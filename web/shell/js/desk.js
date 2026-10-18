/* ==================================================================
   VERSO — the desk

   The shell's own remaining job: the ground and the minute track.
   The clock, tasks and player used to be hardcoded here too — they
   now live in web/widgets/dial, web/widgets/tasks and
   web/widgets/player, loaded the same way any other widget is. See
   js/widgets.js.

   The DOM is only touched when a value actually changes. On a
   wallpaper that runs for eight hours straight, that is the
   difference between idle and a warm laptop.
================================================================== */

import * as Host from "./host.js";
import * as Theme from "./theme.js";
import * as Regions from "./regions.js";
import * as Background from "./background.js";
import * as Layout from "./layout.js";
import * as Widgets from "./widgets.js";

const $ = id => document.getElementById(id);

/* ==================================================================
   THEME SWITCHING

   The only theme UI that exists until the settings window lands.
   Press T to cycle; a toast names what you landed on.
================================================================== */

function startThemeSwitch() {
  const toast = $("themeToast");
  const label = $("themeName");
  let hideTimer = 0;

  function announce(manifest) {
    if (!manifest) return;
    label.textContent = manifest.name ?? manifest.id;
    toast.classList.add("show");
    clearTimeout(hideTimer);
    hideTimer = setTimeout(() => toast.classList.remove("show"), 1600);
  }

  Theme.onChange(announce);

  document.addEventListener("keydown", ev => {
    // Never while someone is typing — including into a widget's own
    // input, which this document cannot see keystrokes for anyway,
    // but a stray click into the page itself still can be typing.
    if (ev.target instanceof HTMLInputElement) return;
    if (ev.ctrlKey || ev.altKey || ev.metaKey) return;
    if (ev.key !== "t" && ev.key !== "T") return;
    ev.preventDefault();
    Theme.cycle(ev.shiftKey ? -1 : 1);
  });
}

/* ==================================================================
   LAYER

   The host tells us, via ?layer=, which part of the desk this window
   is responsible for. See the LAYERS block in css/desk.css for what
   each one means and why overlay mode has to avoid soft edges.
================================================================== */

function applyLayer() {
  const requested = new URLSearchParams(location.search).get("layer");
  const layer = ["wallpaper", "overlay", "background"].includes(requested) ? requested : "all";

  document.body.classList.add("layer-" + layer);
  document.documentElement.dataset.layer = layer;
  return layer;
}

/* ==================================================================
   START

   Theme first and awaited, so nothing paints against a half-applied
   palette. Then the host, then the widgets, then layout — each
   widget has to exist in the DOM before layout can measure and place
   it, and layout has to be final before regions reports it.

   "background" is the odd one out: a fully click-through window (see
   DeskWindow.cs's DeskMode.Background) that exists only to show the
   stage. There is nothing on it a user could ever interact with, so
   it skips widgets, layout, regions and the minute-track/theme-switch
   UI entirely rather than mounting interactive things nobody can
   reach.
================================================================== */

async function start() {
  const layer = applyLayer();

  await Theme.start();
  await Host.connect();
  Background.start();

  document.body.dataset.host = Host.get().kind;
  document.body.dataset.layer = layer;

  if (layer === "background") {
    return;
  }

  startThemeSwitch();

  Widgets.start();
  Layout.start();

  // Last: the host reshapes its window to whatever we report, so the
  // geometry has to be final before the first message goes out.
  if (layer === "overlay") {
    Regions.start();
  }
}

start();
