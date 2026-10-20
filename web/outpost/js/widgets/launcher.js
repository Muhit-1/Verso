/* App launcher — see-through boxes of apps, placed anywhere on the desk (instead of taskbar pins).

   MULTI-INSTANCE: one element per box, each with its own position key ("box-<id>"). The apps and boxes live in the
   host (the app library), so the desk and the dashboard's Apps page always show the same thing; this widget only
   draws them and sends the user's clicks back. Nothing here touches another widget. */

import { register } from "../framework.js";
import * as Core from "../core.js";

const BOX_W = 340;
const BOX_H = 230;
const DRAG_START = 6;

const hue = name => [...name].reduce((h, c) => (h * 31 + c.charCodeAt(0)) % 360, 7);

register({
  id: "launcher",

  mount(ctx) {
    const host = ctx.host;
    const boxes = new Map();   // box id -> { el, sig }
    let seq = 0;
    let disposed = false;

    /* ---- one box ---- */

    function build(box, index) {
      const el = document.createElement("div");
      el.className = "box glass";
      el.dataset.rs = "1";
      el.style.width = BOX_W + "px";
      el.style.height = BOX_H + "px";
      el.innerHTML = `
        <div class="bh" data-drag>
          <span class="nm"></span>
          <div class="acts">
            <button class="mini add" title="Add an app"><svg viewBox="0 0 24 24"><path d="M12 5v14M5 12h14"/></svg></button>
            ${box.builtIn ? "" : '<button class="mini more" title="Box options"><svg viewBox="0 0 24 24"><circle cx="5" cy="12" r="1.2"/><circle cx="12" cy="12" r="1.2"/><circle cx="19" cy="12" r="1.2"/></svg></button>'}
          </div>
        </div>
        <div class="apps"></div>
        <div class="bmenu"><button class="mi del"><span>Delete box</span><span class="r">apps move to Recent</span></button></div>`;

      // "+" opens the installed-apps picker next to this box (a real window, so the search box can be typed in)
      el.querySelector(".add").addEventListener("click", () => host.openAppPicker(box.id, Core.screenRect(el)));

      // the name is typed in the pop-up editor window (the desk itself cannot take keyboard input);
      // the built-in Recent box keeps its name
      const nm = el.querySelector(".nm");
      if (!box.builtIn) {
        nm.classList.add("editable");
        nm.title = "Click to rename";
        nm.addEventListener("click", () => host.editBoxName(box.id, Core.screenRect(nm)));
      }
      el.querySelector(".more")?.addEventListener("click", () => el.classList.toggle("menu-open"));
      el.querySelector(".del").addEventListener("click", async () => {
        await host.deleteBox(box.id);
        ctx.announce("Box deleted — its apps moved to Recent");
        refresh();
      });

      ctx.mount(el, ({ area }) => ({
        x: area.x + area.w - BOX_W - 32,
        y: area.y + 110 + index * (BOX_H + 16)
      }), "box-" + box.id);

      return { el, sig: null };
    }

    function update(entry, box) {
      const { el } = entry;
      el.querySelector(".nm").textContent = box.name;

      const sig = JSON.stringify(box.apps);
      if (sig === entry.sig) return;
      entry.sig = sig;

      const grid = el.querySelector(".apps");
      grid.textContent = "";

      if (!box.apps.length) {
        const empty = document.createElement("div");
        empty.className = "empty";
        empty.textContent = box.builtIn
          ? "New apps land here. Add one with +, or in Settings → Apps."
          : "Empty box. Use + to add an app.";
        grid.appendChild(empty);
        return;
      }

      for (const app of box.apps) {
        const tile = document.createElement("div");
        tile.className = "tile" + (app.missing ? " missing" : "");
        tile.dataset.app = app.id;
        tile.title = app.missing ? `${app.name} — the file is gone (moved or deleted). Remove it and add it again.` : app.name;

        const icon = document.createElement("div");
        icon.className = "aico";
        icon.style.background = `hsl(${hue(app.name)} 45% 42%)`;
        icon.textContent = app.name.trim().slice(0, 1).toUpperCase();
        const label = document.createElement("span");
        label.textContent = app.name;
        tile.append(icon, label);
        grid.appendChild(tile);

        // the real icon arrives a moment later; the lettered tile is shown until then (or if there is none)
        Promise.resolve(host.getAppIcon(app.id)).then(url => {
          if (!url || !tile.isConnected) return;
          icon.textContent = "";
          icon.style.background = "none";
          const img = document.createElement("img");
          img.src = url;
          img.draggable = false;
          icon.appendChild(img);
        }).catch(() => { /* keep the lettered tile */ });

        tile.addEventListener("pointerdown", e => tileDown(e, app, tile));
      }
    }

    /* ---- click launches, a drag moves the app into another box ---- */

    let cancelTileDrag = null;   // set while a tile is being dragged; called when the pointer leaves the bare desktop

    /** Over the real desktop and not over any of our elements (box, card, menu, toast): the place a tile can be dropped to leave its box. */
    const bareDesktopAt = (x, y) => !document.elementsFromPoint(x, y).some(n => n.classList?.contains("mv") || n.id === "menu" || n.classList?.contains("utoast"));

    function tileDown(e, app, tile) {
      if (e.button !== 0) return;
      const start = { x: e.clientX, y: e.clientY };
      let ghost = null;
      let target = null;

      const over = (x, y) => {
        const hit = document.elementsFromPoint(x, y).find(n => n.classList?.contains("box"));
        return hit && !hit.contains(tile) ? hit : null;
      };

      const move = ev => {
        if (!ghost && Math.hypot(ev.clientX - start.x, ev.clientY - start.y) > DRAG_START) {
          ghost = tile.cloneNode(true);
          ghost.classList.add("ghost");
          document.body.appendChild(ghost);
          tile.style.opacity = ".35";
        }
        if (!ghost) return;
        ghost.style.left = ev.clientX - 37 + "px";
        ghost.style.top = ev.clientY - 30 + "px";
        const next = over(ev.clientX, ev.clientY);
        if (next !== target) {
          target?.classList.remove("drop");
          next?.classList.add("drop");
          target = next;
        }
        // not over any box: releasing here puts the app on the desktop
        ghost.classList.toggle("out", !boxAt(ev.clientX, ev.clientY) && bareDesktopAt(ev.clientX, ev.clientY));
      };

      const stop = () => {
        removeEventListener("pointermove", move);
        removeEventListener("pointerup", up);
        cancelTileDrag = null;
      };

      // the pointer left the bare desktop (a release elsewhere is never delivered): drop the drag, change nothing
      cancelTileDrag = () => {
        stop();
        ghost?.remove();
        tile.style.opacity = "";
        target?.classList.remove("drop");
      };

      const up = async ev => {
        stop();
        if (!ghost) {
          // say "Opening" only if the host actually started it
          let status = "ok";
          try { status = await host.launchApp(app.id); } catch { status = "missing"; }
          if (status === "missing") {
            tile.classList.add("missing");
            ctx.announce(`Can't open ${app.name} — the file is gone. Remove it and add it again.`);
          } else {
            ctx.announce("Opening " + app.name);
          }
          return;
        }
        ghost.remove();
        tile.style.opacity = "";
        target?.classList.remove("drop");
        const dropOn = over(ev.clientX, ev.clientY);
        const boxId = dropOn && [...boxes].find(([, b]) => b.el === dropOn)?.[0];
        if (boxId) {
          await host.moveApp(app.id, boxId);
          refresh();
        } else if (!boxAt(ev.clientX, ev.clientY) && bareDesktopAt(ev.clientX, ev.clientY)) {
          putOnDesktop(app);
        }
      };

      addEventListener("pointermove", move);
      addEventListener("pointerup", up);
    }

    /** The tile was dropped on the bare desktop: the app leaves its box and appears on the desktop. */
    async function putOnDesktop(app) {
      let r;
      try { r = await host.putOnDesktop(app.id); } catch { r = { result: "failed" }; }
      if (r.result === "moved" || r.result === "created") undoToast(`${r.name} is back on your desktop`, r.undo);
      else if (r.result === "store") ctx.announce("Store apps can't be placed on the desktop yet");
      else if (r.result === "missing") ctx.announce(`${app.name}'s file is gone — nothing to put on the desktop`);
      else ctx.announce("Couldn't put it on the desktop");
      refresh();
    }

    /* ---- a toast with an Undo button (the plain toast is click-through) ---- */

    let undoEl = null;
    let undoTimer = 0;

    function hideUndo() {
      clearTimeout(undoTimer);
      undoEl?.remove();
      undoEl = null;
    }

    function undoToast(text, token) {
      hideUndo();
      if (!token) { ctx.announce(text); return; }

      undoEl = document.createElement("div");
      undoEl.className = "utoast";
      undoEl.innerHTML = '<span class="ut"></span><button class="ubtn">Undo</button>';
      undoEl.querySelector(".ut").textContent = text;
      undoEl.querySelector(".ubtn").addEventListener("click", async () => {
        hideUndo();
        let ok = false;
        try { ok = await host.undoLauncher(token); } catch { /* fall through */ }
        ctx.announce(ok ? "Undone" : "Can't undo that anymore");
        refresh();
      });
      document.body.appendChild(undoEl);
      undoTimer = setTimeout(hideUndo, 9000);
    }

    /* ---- drag a desktop icon onto a box ----
       The real desktop icons are Explorer's and sit in FRONT of this page, so a drop never arrives here as a drag-and-drop
       (and Explorer moves the icon). What does reach us is the mirrored mouse, and Explorer's selection: pressing an icon
       selects it at once. So: after a press, ask the host whether an icon is selected; if so and the pointer travels, light
       up the box under it; on release over a box, ask the host to add what is selected. */

    let press = null;   // { x, y, icon: bool|null, moving: bool, over: boxElement|null }

    const boxAt = (x, y) => document.elementsFromPoint(x, y).find(n => n.classList?.contains("box")) ?? null;
    const lightUp = box => {
      if (press?.over === box) return;
      press?.over?.classList.remove("drop");
      box?.classList.add("drop");
      if (press) press.over = box;
    };

    const onDown = e => {
      if (e.button !== 0 || e.target.closest?.("button, .tile, .nb, [data-drag], .grip, #menu, .nmenu")) return;
      const mine = (press = { x: e.clientX, y: e.clientY, icon: null, moving: false, over: null });
      // Explorer selects the pressed icon at mouse-down; give it a moment, then look
      setTimeout(async () => {
        try {
          const count = await host.getDesktopSelectionCount();
          if (press === mine) mine.icon = count > 0;
        } catch { /* no host: nothing to drag */ }
      }, 120);
    };

    const onMove = e => {
      if (!press || press.icon === false) return;
      if (!press.moving && Math.hypot(e.clientX - press.x, e.clientY - press.y) > 12) press.moving = true;
      if (press.moving && press.icon) lightUp(boxAt(e.clientX, e.clientY));
    };

    const onUp = async e => {
      const done = press;
      press = null;
      if (!done) return;
      done.over?.classList.remove("drop");
      if (!done.moving || !done.icon) return;

      const box = boxAt(e.clientX, e.clientY);
      const id = box && [...boxes].find(([, b]) => b.el === box)?.[0];
      if (!id) return;

      try {
        const r = await host.addDesktopSelection(id);
        if (r.moved) undoToast(r.moved === 1 ? "Moved into the box — the desktop icon is gone" : `Moved ${r.moved} shortcuts into the box`, r.undo);
        else if (r.added) ctx.announce(r.added === 1 ? "Added to the box" : `Added ${r.added} apps to the box`);
        else if (r.already) ctx.announce("Already in your library");
        else ctx.announce("That icon can't be added (This PC, Recycle Bin…)");
      } catch (err) {
        console.warn("Verso: could not add the desktop icon", err);
      }
    };

    addEventListener("pointerdown", onDown, true);
    addEventListener("pointermove", onMove, true);
    addEventListener("pointerup", onUp, true);

    /* ---- follow the library ---- */

    async function refresh() {
      const mine = ++seq;
      let list = [];
      try {
        list = await host.getApps();
      } catch (err) {
        console.warn("Verso: could not read the app library", err);
      }
      if (disposed || mine !== seq) return;

      const ids = new Set(list.map(b => b.id));
      for (const [id, entry] of boxes) {
        if (!ids.has(id)) {
          ctx.unmount(entry.el);
          boxes.delete(id);
          Core.clearPosition("box-" + id);
        }
      }
      list.forEach((box, index) => {
        if (!boxes.has(box.id)) boxes.set(box.id, build(box, index));
        update(boxes.get(box.id), box);
      });
    }

    ctx.on("appsChanged", refresh);
    ctx.on("pointerLeft", () => cancelTileDrag?.());
    refresh();

    return {
      dispose() {
        disposed = true;
        hideUndo();
        removeEventListener("pointerdown", onDown, true);
        removeEventListener("pointermove", onMove, true);
        removeEventListener("pointerup", onUp, true);
      }
    };
  }
});
