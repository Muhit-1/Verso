  // The app picker page. The host (AppPickerWindow) opens it over a box; it lists the apps the host found
  // (Start menu, Store apps, Desktop shortcuts) and sends back the ones that were ticked.
  const $ = s => document.querySelector(s);
  const bridge = () => window.chrome?.webview?.hostObjects?.VersoPicker;
  const post = msg => window.chrome?.webview?.postMessage(msg);
  const hue = n => [...n].reduce((h, c) => (h * 31 + c.charCodeAt(0)) % 360, 7);
  const esc = s => String(s).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

  let apps = [];          // [{target, name, source, added}]
  let loaded = false;
  let filter = "All";
  let hl = 0;
  let selected = new Set();
  let boxId = "recent";
  let session = 0;        // bumps on every open, so a slow answer from the previous open is ignored
  const icons = new Map();

  const sourceLabel = s => s === "Desktop" ? "Desktop shortcut" : s;
  const chipLabel = f => f === "Desktop" ? "Desktop shortcuts" : f === "Store app" ? "Store apps" : f;

  function visible() {
    const q = $("#q").value.trim().toLowerCase();
    return apps.filter(a => (filter === "All" || a.source === filter) && (!q || a.name.toLowerCase().includes(q)));
  }

  function draw() {
    $("#chips").innerHTML = ["All", "Start menu", "Store app", "Desktop"]
      .map(f => `<button class="chip${f === filter ? " on" : ""}" data-f="${f}">${chipLabel(f)}</button>`).join("");

    const list = visible();
    hl = Math.min(hl, Math.max(0, list.length - 1));
    let html = "";
    let last = "";

    if (!loaded) {
      html = '<div class="none">Reading your installed apps…</div>';
    } else if (!list.length) {
      html = `<div class="none">${apps.length ? `No installed app matches “${esc($("#q").value)}”.` : "No installed apps were found."}<br><button class="btn link" id="browse2">Browse for a file instead…</button></div>`;
    }

    list.forEach((a, i) => {
      const letter = (a.name[0] || "#").toUpperCase();
      if (!$("#q").value && letter !== last) {
        html += `<div class="sec">${esc(letter)}</div>`;
        last = letter;
      }
      html += `<div class="row${selected.has(a.target) ? " sel" : ""}${a.added ? " added" : ""}${i === hl ? " hl" : ""}" data-i="${i}">
        <div class="aico" data-t="${esc(a.target)}" style="background:hsl(${hue(a.name)} 45% 42%)">${esc(letter)}</div>
        <span class="nm">${esc(a.name)}</span><span class="tag">${esc(sourceLabel(a.source))}</span><span class="ck">✓</span></div>`;
    });

    $("#list").innerHTML = html;
    $("#list").__rows = list;
    wireIcons();

    const n = selected.size;
    $("#cnt").textContent = n ? `${n} selected` : "Nothing selected";
    $("#add").disabled = !n;
    $("#add").textContent = n ? `Add ${n} app${n > 1 ? "s" : ""}` : "Add";
  }

  // icons arrive lazily, only for rows that are actually on screen
  const watcher = new IntersectionObserver(entries => {
    for (const e of entries) {
      if (!e.isIntersecting) continue;
      watcher.unobserve(e.target);
      setIcon(e.target);
    }
  }, { root: null, rootMargin: "200px" });

  function wireIcons() {
    document.querySelectorAll(".aico[data-t]").forEach(el => {
      if (icons.has(el.dataset.t) && icons.get(el.dataset.t)) paint(el, icons.get(el.dataset.t));
      else watcher.observe(el);
    });
  }

  function paint(el, url) {
    if (!url) return;
    el.textContent = "";
    el.style.background = "none";
    const img = document.createElement("img");
    img.src = url;
    img.draggable = false;
    el.appendChild(img);
  }

  async function setIcon(el) {
    const target = el.dataset.t;
    const mine = session;
    let url = icons.get(target);
    if (url === undefined) {
      try { url = await bridge().GetIcon(target); } catch { url = ""; }
      icons.set(target, url || "");
    }
    if (mine === session && el.isConnected) paint(el, url);
  }

  function toggle(a) {
    if (!a || a.added) return;
    if (!selected.delete(a.target)) selected.add(a.target);
    draw();
  }

  async function commit() {
    if (!selected.size) return;
    const chosen = apps.filter(a => selected.has(a.target)).map(a => ({ target: a.target, name: a.name }));
    await bridge().Add(JSON.stringify(chosen), $("#target").value);
    close();
  }

  const close = () => bridge()?.Close();

  async function open(id) {
    const mine = ++session;
    boxId = id || "recent";
    apps = [];
    loaded = false;
    selected = new Set();
    filter = "All";
    hl = 0;
    $("#q").value = "";
    draw();
    $("#q").focus();

    try {
      const boxes = JSON.parse(await bridge().GetBoxes());
      $("#target").innerHTML = boxes.map(b => `<option value="${esc(b.id)}"${b.id === boxId ? " selected" : ""}>${esc(b.name)}</option>`).join("");
    } catch { /* the select stays empty; Add falls back to the first box on the host */ }

    // the host reads the Start menu on its own thread; ask until it has the answer
    for (let tries = 0; tries < 100 && mine === session; tries++) {
      let json = "";
      try { json = await bridge().GetApps(); } catch { /* try again */ }
      if (json) {
        apps = JSON.parse(json);
        loaded = true;
        draw();
        return;
      }
      await new Promise(r => setTimeout(r, 150));
    }
    if (mine === session) { loaded = true; draw(); }
  }

  $("#list").addEventListener("click", e => {
    if (e.target.id === "browse2") { bridge().Browse($("#target").value); return; }
    const row = e.target.closest(".row");
    if (row) toggle($("#list").__rows?.[+row.dataset.i]);
  });
  $("#chips").addEventListener("click", e => {
    const chip = e.target.closest(".chip");
    if (chip) { filter = chip.dataset.f; hl = 0; draw(); $("#q").focus(); }
  });
  $("#q").addEventListener("input", () => { hl = 0; draw(); });
  $("#add").addEventListener("click", commit);
  $("#cancel").addEventListener("click", close);
  $("#browse").addEventListener("click", () => bridge().Browse($("#target").value));

  addEventListener("keydown", e => {
    const list = visible();
    if (e.key === "Escape") { e.preventDefault(); close(); }
    else if (e.key === "ArrowDown") { e.preventDefault(); hl = Math.min(list.length - 1, hl + 1); draw(); document.querySelector(".row.hl")?.scrollIntoView({ block: "nearest" }); }
    else if (e.key === "ArrowUp") { e.preventDefault(); hl = Math.max(0, hl - 1); draw(); document.querySelector(".row.hl")?.scrollIntoView({ block: "nearest" }); }
    else if (e.key === " " && document.activeElement !== $("#q")) { e.preventDefault(); toggle(list[hl]); }
    else if (e.key === "Enter") {
      e.preventDefault();
      if (!selected.size && list[hl] && !list[hl].added) selected.add(list[hl].target);
      commit();
    }
  });

  window.chrome?.webview?.addEventListener("message", e => { if (e.data?.type === "init") open(e.data.boxId); });
  post({ type: "ready" });
