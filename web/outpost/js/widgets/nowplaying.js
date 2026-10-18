/* Now playing — the real Windows media session. A pill that expands in place to the controls. */

import { register } from "../framework.js";
import { wasDragged } from "../layout.js";

const ICON = {
  music: '<svg viewBox="0 0 24 24"><path d="M9 18V5l12-2v13"/><circle cx="6" cy="18" r="3"/><circle cx="18" cy="16" r="3"/></svg>',
  chev: '<svg viewBox="0 0 24 24"><path d="M6 9l6 6 6-6"/></svg>',
  prev: '<svg viewBox="0 0 24 24"><path d="M19 20L9 12l10-8zM5 19V5"/></svg>',
  next: '<svg viewBox="0 0 24 24"><path d="M5 4l10 8-10 8zM19 5v14"/></svg>',
  play: '<path d="M6 4l14 8-14 8z"/>',
  pause: '<path d="M6 4h4v16H6zM14 4h4v16h-4z"/>'
};

const fmt = ms => {
  const s = Math.max(0, Math.floor(ms / 1000));
  return Math.floor(s / 60) + ":" + String(s % 60).padStart(2, "0");
};

register({
  id: "np",

  mount(ctx) {
    const host = ctx.host;
    const el = document.createElement("div");
    el.className = "np glass";
    el.innerHTML = `
      <div class="head" data-drag>${ICON.music}
        <div class="tx"><div class="lbl">NOW PLAYING</div><div class="mq"><span class="txt">Nothing playing</span></div></div>
        <span class="chev">${ICON.chev}</span></div>
      <div class="more">
        <div class="mrow"><div class="art"></div><div class="mt"><b class="title">Nothing playing</b><span class="artist"></span></div></div>
        <div class="bar"><i></i></div>
        <div class="tm"><span class="cur">0:00</span><span class="dur">0:00</span></div>
        <div class="ctl">
          <button class="rbtn prev">${ICON.prev}</button>
          <button class="rbtn main play"><svg viewBox="0 0 24 24" class="pi">${ICON.play}</svg></button>
          <button class="rbtn next">${ICON.next}</button>
        </div>
      </div>`;

    const q = s => el.querySelector(s);
    const mq = q(".mq");
    const txt = q(".txt");
    let lastText = "";
    let playing = false;
    const set = (node, text) => { if (node.textContent !== text) node.textContent = text; };

    function render(np) {
      const text = np && (np.title || np.artist) ? [np.title, np.artist].filter(Boolean).join(" — ") : "Nothing playing";
      if (text !== lastText) {
        lastText = text;
        txt.textContent = text;
        mq.classList.remove("scroll");
        // scroll only when it does not fit; stepped so a smooth scroll is not keeping a 60 fps compositor busy
        requestAnimationFrame(() => {
          if (txt.scrollWidth > mq.clientWidth) {
            txt.textContent = text + "   ·   " + text + "   ·   ";
            mq.style.setProperty("--mq-dur", Math.max(10, text.length * 0.32) + "s");
            mq.style.setProperty("--mq-steps", Math.max(8, Math.round(txt.scrollWidth / 2 / 3)));
            mq.classList.add("scroll");
          }
        });
      }
      if (!el.classList.contains("open")) return; // the expanded part is only worth updating while it is shown

      set(q(".title"), np?.title || "Nothing playing");
      set(q(".artist"), np ? np.artist || "" : "Start something in Spotify, YouTube…");
      set(q(".cur"), np ? fmt(np.positionMs) : "0:00");
      set(q(".dur"), np ? fmt(np.durationMs) : "0:00");
      q(".bar i").style.width = (np && np.durationMs > 0 ? Math.min(100, (100 * np.positionMs) / np.durationMs) : 0) + "%";
      if (np?.art?.length >= 2) q(".art").style.background = `linear-gradient(135deg, ${np.art[1]}, ${np.art[0]})`;
      playing = !!np?.isPlaying;
      q(".pi").innerHTML = playing ? ICON.pause : ICON.play;
    }

    q(".head").addEventListener("click", () => {
      if (wasDragged(el) || document.body.classList.contains("arrange")) return;
      el.classList.toggle("open");
      ctx.state.patch({ open: el.classList.contains("open") });
      render(host.getNowPlaying());
    });
    q(".play").addEventListener("click", () => (playing ? host.pause() : host.play()));
    q(".prev").addEventListener("click", () => host.previous());
    q(".next").addEventListener("click", () => host.next());

    if (ctx.state.get().open) el.classList.add("open");
    const unsubscribe = host.onNowPlayingChanged(render);
    ctx.mount(el, ({ area }) => ({ x: area.x + 112, y: area.y + 32 }));
    render(host.getNowPlaying());

    return { dispose: () => unsubscribe?.() };
  }
});
