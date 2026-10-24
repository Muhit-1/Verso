  // The editor page. The host opens it over a note or a box name; it reports text back through postMessage.
  // Everything typed stays text/HTML inside this page — the host reduces notes to a few safe tags before storing them.
  const body = document.getElementById("body");
  const post = msg => window.chrome?.webview?.postMessage(msg);
  let single = false;
  let timer = 0;

  const value = () => single ? body.innerText.replace(/\s+/g, " ").trim() : body.innerHTML;
  window.__editorValue = value;

  function changed() {
    clearTimeout(timer);
    timer = setTimeout(() => post({ type: "change", value: value() }), 800);   // autosave
  }

  function refreshTools() {
    document.querySelectorAll(".tools [data-cmd]").forEach(b => {
      let on = false;
      try { on = document.queryCommandState(b.dataset.cmd); } catch { /* not supported here */ }
      b.classList.toggle("on", on);
    });
  }

  body.addEventListener("input", () => { changed(); refreshTools(); });
  document.addEventListener("selectionchange", refreshTools);

  document.querySelectorAll(".tools [data-cmd]").forEach(b => {
    b.addEventListener("mousedown", e => e.preventDefault());   // keep the selection in the text
    b.addEventListener("click", () => { document.execCommand(b.dataset.cmd); body.focus(); changed(); refreshTools(); });
  });

  const finish = () => { clearTimeout(timer); post({ type: "done", value: value() }); };
  document.getElementById("done").addEventListener("click", finish);

  addEventListener("keydown", e => {
    if (e.key === "Escape") { e.preventDefault(); finish(); }
    else if (single && e.key === "Enter") { e.preventDefault(); finish(); }
  });

  // single-line mode: no pasted line breaks or formatting
  body.addEventListener("paste", e => {
    if (!single) return;
    e.preventDefault();
    document.execCommand("insertText", false, (e.clipboardData.getData("text/plain") || "").replace(/\s+/g, " "));
  });

  window.chrome?.webview?.addEventListener("message", e => {
    const m = e.data;
    if (m?.type !== "init") return;
    single = !!m.single;
    document.body.classList.toggle("single", single);
    if (m.color) document.documentElement.style.setProperty("--accent", m.color);
    if (single) body.textContent = m.value || "";
    else body.innerHTML = m.value || "";
    body.focus();
    // caret at the end, like clicking after the last word
    const r = document.createRange();
    r.selectNodeContents(body);
    r.collapse(false);
    const s = getSelection();
    s.removeAllRanges();
    s.addRange(r);
    if (single) { r.selectNodeContents(body); s.removeAllRanges(); s.addRange(r); }   // rename: select the whole name
    refreshTools();
  });

  post({ type: "ready" });
