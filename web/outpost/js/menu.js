/* ==================================================================
   VERSO — the round desk button and its menu.

   Replaces the old MOVE / THEME buttons: one small round button; its menu holds Arrange, the palette, a switch
   for every widget (each on/off by itself), hide-all, and a way into the dashboard. It is not a widget — it
   cannot be turned off, otherwise there would be no way back.
================================================================== */

import * as Host from "/shell/js/host.js";
import * as Core from "./core.js";
import * as Layout from "./layout.js";
import * as Framework from "./framework.js";

const ICON = {
  move: '<svg viewBox="0 0 24 24"><path d="M5 9l-3 3 3 3M9 5l3-3 3 3M15 19l-3 3-3-3M19 9l3 3-3 3M2 12h20M12 2v20"/></svg>',
  eye: '<svg viewBox="0 0 24 24"><path d="M1 12s4-7 11-7 11 7 11 7-4 7-11 7S1 12 1 12z"/><circle cx="12" cy="12" r="3"/></svg>',
  plus: '<svg viewBox="0 0 24 24"><path d="M12 5v14M5 12h14"/></svg>',
  gear: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="3"/><path d="M19 12a7 7 0 0 0-.1-1.2l2-1.5-2-3.4-2.3 1a7 7 0 0 0-2-1.2L14.3 3h-4.6l-.3 2.7a7 7 0 0 0-2 1.2l-2.3-1-2 3.4 2 1.5a7 7 0 0 0 0 2.4l-2 1.5 2 3.4 2.3-1a7 7 0 0 0 2 1.2l.3 2.7h4.6l.3-2.7a7 7 0 0 0 2-1.2l2.3 1 2-3.4-2-1.5c.1-.4.1-.8.1-1.2z"/></svg>'
};


let button;
let menu;

export function start() {
  menu = document.getElementById("menu");

  button = document.createElement("button");
  button.className = "rb glass";
  button.title = "Desk menu";
  button.textContent = "!";
  Layout.add("rb", button, ({ w, area }) => ({ x: area.x + area.w - 32 - w, y: area.y + 32 }));

  button.addEventListener("click", () => {
    if (!Layout.wasDragged(button)) toggle();
  });

  // anything outside the menu closes it
  addEventListener("pointerdown", e => {
    if (!e.target.closest("#menu, .rb")) close();
  }, true);
}

async function toggle() {
  if (menu.classList.contains("open")) {
    close();
    return;
  }
  await render();
  place();
  menu.classList.add("open");
  button.classList.add("open");
}

function close() {
  menu.classList.remove("open");
  button?.classList.remove("open");
}

function place() {
  const r = button.getBoundingClientRect();
  const a = Layout.area();
  const w = menu.offsetWidth;
  const h = menu.offsetHeight;
  let x = r.left;
  let y = r.bottom + 10;
  if (x + w > a.x + a.w - 12) x = r.right - w;
  if (y + h > a.y + a.h - 12) y = r.top - h - 10;
  menu.style.left = Math.max(12, x) + "px";
  menu.style.top = Math.max(12, y) + "px";
}

async function render() {
  const host = Host.get();
  let widgets = [];
  try { widgets = await host.getOutpostWidgets(); } catch { /* the menu still works without the list */ }
  let palettes = [];
  try { palettes = await host.getOutpostThemes(); } catch { /* the dots are optional */ }
  const theme = document.body.dataset.theme;
  const hidden = document.body.classList.contains("hide-ui");

  menu.innerHTML = `
    <button class="mi" data-a="arrange">${ICON.move}<span>Arrange (move / resize)</span></button>
    <div class="sep1"></div>
    <h4>Theme</h4>
    <div class="dots">${palettes.map(p => `<i class="dot${p.id === theme ? " on" : ""}" data-t="${p.id}" title="${p.name}" style="background:linear-gradient(90deg,${p.from},${p.to})"></i>`).join("")}</div>
    <div class="sep1"></div>
    <h4>Widgets — each is independent</h4>
    ${widgets.map(w => `<button class="mi" data-w="${w.id}"><span>${w.name}</span><span class="r"><i class="sw${w.enabled ? " on" : ""}"></i></span></button>`).join("")}
    <div class="sep1"></div>
    <button class="mi" data-a="newnote">${ICON.plus}<span>New sticky note</span></button>
    <button class="mi" data-a="newbox">${ICON.plus}<span>New app box</span></button>
    <button class="mi" data-a="hide">${ICON.eye}<span>${hidden ? "Show" : "Hide"} all widgets</span></button>
    <button class="mi" data-a="settings">${ICON.gear}<span>Open Settings…</span></button>`;

  menu.querySelectorAll("[data-t]").forEach(dot => dot.addEventListener("click", () => {
    Core.applyPalette(dot.dataset.t);
    host.setOutpostTheme(dot.dataset.t);
    menu.querySelectorAll("[data-t]").forEach(d => d.classList.toggle("on", d === dot));
  }));

  menu.querySelectorAll("[data-w]").forEach(row => row.addEventListener("click", async () => {
    const sw = row.querySelector(".sw");
    const on = !sw.classList.contains("on");
    sw.classList.toggle("on", on);
    // the host switches ONE widget; the framework then starts or stops only that one
    await host.setOutpostWidgetEnabled(row.dataset.w, on);
    await Framework.sync();
  }));

  menu.querySelectorAll("[data-a]").forEach(item => item.addEventListener("click", () => {
    const action = item.dataset.a;
    close();
    if (action === "arrange") Layout.arrange(true);
    if (action === "hide") document.body.classList.toggle("hide-ui");
    if (action === "settings") host.openDashboard();
    if (action === "newbox") newBox(host);
    if (action === "newnote") newNote(host);
  }));
}

/** Makes a new app box. The host also switches the App launcher on, so the box shows up at once. */
async function newBox(host) {
  await host.addBox();
  await Framework.sync();
  Layout.toast("New app box — drag it where you like");
}

/** Makes a new sticky note. The host also switches the Sticky notes widget on, so it shows up at once. */
async function newNote(host) {
  await host.newNote();
  await Framework.sync();
  Layout.toast("New note — click it to type");
}
