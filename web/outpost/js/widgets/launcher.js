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

      el.querySelector(".add").addEventListener("click", () => host.addApp(box.id));

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
        tile.className = "tile";
        tile.dataset.app = app.id;
        tile.title = app.name;

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
      };

      const up = async ev => {
        removeEventListener("pointermove", move);
        removeEventListener("pointerup", up);
        if (!ghost) {
          host.launchApp(app.id);
          ctx.announce("Opening " + app.name);
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
        }
      };

      addEventListener("pointermove", move);
      addEventListener("pointerup", up);
    }

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
    refresh();

    return { dispose() { disposed = true; } };
  }
});
