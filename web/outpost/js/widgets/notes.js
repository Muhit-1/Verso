/* Sticky notes — like Microsoft Sticky Notes: as many as you like, each with its own colour, position and size.

   MULTI-INSTANCE: one element per note, each with its own position key ("note-<id>"). The notes live in the host
   (their own file), so the desk and the dashboard agree; this widget only draws them and sends clicks back.
   TYPING: this page is behind the desktop icons and cannot take keyboard input, so clicking a note asks the host to
   open a real pop-up editor window exactly over it (see TextEditorWindow.cs). The text comes back through the host
   and the note redraws. Nothing here touches another widget. */

import { register } from "../framework.js";
import * as Core from "../core.js";

const COLORS = ["#7ac55c", "#5aa9e6", "#f2c94c", "#f08fb0", "#b48ae0", "#a8a8a8"];
const NOTE_W = 300;
const NOTE_H = 220;

register({
  id: "notes",

  mount(ctx) {
    const host = ctx.host;
    const notes = new Map();   // note id -> { el, sig }
    let seq = 0;
    let disposed = false;

    /* ---- one note ---- */

    function build(note, index) {
      const el = document.createElement("div");
      el.className = "note";
      el.dataset.rs = "1";
      el.style.width = NOTE_W + "px";
      el.style.height = NOTE_H + "px";
      el.innerHTML = `
        <div class="strip" data-drag>
          <button class="mini add" title="New note"><svg viewBox="0 0 24 24"><path d="M12 5v14M5 12h14"/></svg></button>
          <span class="sp"></span>
          <button class="mini more" title="Note options"><svg viewBox="0 0 24 24"><circle cx="5" cy="12" r="1.2"/><circle cx="12" cy="12" r="1.2"/><circle cx="19" cy="12" r="1.2"/></svg></button>
        </div>
        <div class="nb"></div>
        <div class="nmenu">
          <div class="ncols">${COLORS.map(c => `<i class="ncol" data-c="${c}" style="background:${c}"></i>`).join("")}</div>
          <button class="mi del"><span>Delete note</span></button>
        </div>`;

      el.querySelector(".add").addEventListener("click", async () => {
        await host.newNote();
        refresh();
      });

      el.querySelector(".more").addEventListener("click", () => {
        const open = !el.classList.contains("menu-open");
        closeMenus();
        el.classList.toggle("menu-open", open);
      });

      el.querySelectorAll(".ncol").forEach(dot => dot.addEventListener("click", async () => {
        await host.setNoteColor(note.id, dot.dataset.c);
        refresh();
      }));

      // delete asks once: the first click arms it, the second deletes
      const del = el.querySelector(".del");
      del.addEventListener("click", async () => {
        if (!del.classList.contains("armed")) {
          del.classList.add("armed");
          del.firstElementChild.textContent = "Click again to delete";
          return;
        }
        await host.deleteNote(note.id);
        ctx.announce("Note deleted");
        refresh();
      });

      el.querySelector(".nb").addEventListener("click", () => {
        el.classList.add("editing");
        host.editNote(note.id, Core.screenRect(el));
      });

      ctx.mount(el, ({ area }) => ({
        // upper middle, clear of the Now playing pill and the clock; each new note steps down and right
        x: area.x + area.w * 0.3 + (index % 6) * 34,
        y: area.y + 70 + (index % 6) * 34
      }), "note-" + note.id);

      return { el, sig: null };
    }

    function update(entry, note) {
      const sig = note.color + "|" + note.html;
      if (sig === entry.sig) return;
      entry.sig = sig;
      entry.el.style.setProperty("--nc", note.color);
      // the host reduced the text to a few safe formatting tags before storing it
      entry.el.querySelector(".nb").innerHTML = note.html;
    }

    function closeMenus() {
      for (const { el } of notes.values()) {
        el.classList.remove("menu-open");
        const del = el.querySelector(".del");
        del.classList.remove("armed");
        del.firstElementChild.textContent = "Delete note";
      }
    }

    // anything outside a note's menu closes it
    const away = e => { if (!e.target.closest?.(".nmenu, .note .more")) closeMenus(); };
    addEventListener("pointerdown", away, true);

    /* ---- follow the library ---- */

    async function refresh() {
      const mine = ++seq;
      let list = [];
      try {
        list = await host.getNotes();
      } catch (err) {
        console.warn("Verso: could not read the notes", err);
      }
      if (disposed || mine !== seq) return;

      const ids = new Set(list.map(n => n.id));
      for (const [id, entry] of notes) {
        if (!ids.has(id)) {
          ctx.unmount(entry.el);
          notes.delete(id);
          Core.clearPosition("note-" + id);
        }
      }
      list.forEach((note, index) => {
        if (!notes.has(note.id)) notes.set(note.id, build(note, index));
        update(notes.get(note.id), note);
      });
    }

    ctx.on("notesChanged", refresh);
    ctx.on("editorClosed", () => notes.forEach(({ el }) => el.classList.remove("editing")));
    refresh();

    return {
      dispose() {
        disposed = true;
        removeEventListener("pointerdown", away, true);
      }
    };
  }
});
