/*
 * Checks on the web layer that need no browser and no .NET:  node --test tests/web
 *
 * The web layer has no build step, so nothing else notices a typo in a manifest, a script that points at a file
 * that was renamed, or a page that quietly started loading something from the internet. These tests do.
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, readdirSync, existsSync, statSync } from "node:fs";
import { join, dirname, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

const WEB = resolve(dirname(fileURLToPath(import.meta.url)), "../../web");

const walk = (dir, out = []) => {
  for (const name of readdirSync(dir)) {
    const full = join(dir, name);
    statSync(full).isDirectory() ? walk(full, out) : out.push(full);
  }
  return out;
};
const rel = f => relative(WEB, f).split(sep).join("/");
const all = walk(WEB);
const read = f => readFileSync(f, "utf8");
const json = f => JSON.parse(read(f));
const isTemplate = f => rel(f).split("/").some(part => part.startsWith("_"));

// Pages the host serves as the top-level document of its own window. These are the ones that get a
// Content-Security-Policy, and that must work with no network.
const LOCKED_PAGES = ["outpost/index.html", "dashboard/index.html", "picker/index.html", "editor/index.html"];

test("every JSON file parses", () => {
  for (const f of all.filter(f => f.endsWith(".json"))) {
    assert.doesNotThrow(() => json(f), `${rel(f)} is not valid JSON`);
  }
});

test("theme and widget registries list folders that exist", () => {
  const registries = [
    ["themes/themes.json", "themes", "themes", "theme.json"],
    ["dashboard/themes/themes.json", "themes", "dashboard/themes", "theme.json"],
    ["widgets/widgets.json", "widgets", "widgets", "widget.json"]
  ];
  for (const [file, key, root, manifest] of registries) {
    const listed = json(join(WEB, file))[key];
    assert.ok(Array.isArray(listed) && listed.length > 0, `${file}: no "${key}" list`);
    for (const id of listed) {
      const path = join(WEB, root, id, manifest);
      assert.ok(existsSync(path), `${file} lists "${id}" but ${root}/${id}/${manifest} is missing`);
      assert.equal(json(path).id, id, `${root}/${id}/${manifest}: id does not match its folder`);
    }
  }
});

test("every installed theme and widget is listed in its registry", () => {
  const unlisted = [];
  for (const [file, key, root, manifest] of [
    ["themes/themes.json", "themes", "themes", "theme.json"],
    ["dashboard/themes/themes.json", "themes", "dashboard/themes", "theme.json"],
    ["widgets/widgets.json", "widgets", "widgets", "widget.json"]
  ]) {
    const listed = new Set(json(join(WEB, file))[key]);
    for (const name of readdirSync(join(WEB, root))) {
      if (name.startsWith("_") || !existsSync(join(WEB, root, name, manifest))) continue;
      if (!listed.has(name)) unlisted.push(`${root}/${name}`);
    }
  }
  assert.deepEqual(unlisted, [], "folders with a manifest that no registry lists (the browser fallback will not show them)");
});

test("every theme and widget manifest has what the host needs, and its files exist", () => {
  for (const root of ["themes", "dashboard/themes"]) {
    for (const name of readdirSync(join(WEB, root))) {
      const f = join(WEB, root, name, "theme.json");
      if (name.startsWith("_") || !existsSync(f)) continue;
      const m = json(f);
      for (const key of ["id", "name", "scheme", "stylesheet"]) assert.ok(m[key], `${root}/${name}: "${key}" missing`);
      assert.ok(["dark", "light"].includes(m.scheme), `${root}/${name}: scheme must be dark or light`);
      assert.ok(existsSync(join(WEB, root, name, m.stylesheet)), `${root}/${name}: stylesheet ${m.stylesheet} is missing`);
    }
  }
  for (const name of readdirSync(join(WEB, "widgets"))) {
    const f = join(WEB, "widgets", name, "widget.json");
    if (name.startsWith("_") || !existsSync(f)) continue;
    const m = json(f);
    for (const key of ["id", "name", "entry"]) assert.ok(m[key], `widgets/${name}: "${key}" missing`);
    assert.ok(existsSync(join(WEB, "widgets", name, m.entry)), `widgets/${name}: entry ${m.entry} is missing`);
    assert.ok(Array.isArray(m.permissions ?? []), `widgets/${name}: permissions must be a list`);
  }
});

test("local script, style and image references in pages resolve", () => {
  const broken = [];
  for (const f of all.filter(f => f.endsWith(".html") && !isTemplate(f))) {
    for (const m of read(f).matchAll(/\b(?:src|href)\s*=\s*"([^"#?]+)[^"]*"/g)) {
      const target = m[1];
      if (/^(?:[a-z]+:|\/\/|data:|mailto:)/i.test(target)) continue;
      const path = target.startsWith("/") ? join(WEB, target) : resolve(dirname(f), target);
      if (!existsSync(path)) broken.push(`${rel(f)} -> ${target}`);
    }
  }
  assert.deepEqual(broken, []);
});

test("ES module imports resolve", () => {
  const broken = [];
  for (const f of all.filter(f => f.endsWith(".js"))) {
    for (const m of read(f).matchAll(/\bimport\s+(?:[^"'`;]*?\sfrom\s+)?["']([^"']+)["']/g)) {
      const target = m[1];
      if (!target.startsWith(".") && !target.startsWith("/")) continue;
      const path = target.startsWith("/") ? join(WEB, target) : resolve(dirname(f), target);
      if (!existsSync(path)) broken.push(`${rel(f)} -> ${target}`);
    }
  }
  assert.deepEqual(broken, []);
});

test("CSS url() references resolve", () => {
  const broken = [];
  for (const f of all.filter(f => f.endsWith(".css") && !isTemplate(f))) {
    for (const m of read(f).matchAll(/url\(\s*['"]?([^)'"]+?)['"]?\s*\)/g)) {
      const target = m[1].split(/[?#]/)[0];
      if (!target || /^(?:[a-z]+:|\/\/|data:|%23)/i.test(target)) continue;   // %23n is an SVG fragment inside a data URL
      const path = target.startsWith("/") ? join(WEB, target) : resolve(dirname(f), target);
      if (!existsSync(path)) broken.push(`${rel(f)} -> ${target}`);
    }
  }
  assert.deepEqual(broken, []);
});

test("the host's own pages carry a Content-Security-Policy that forbids inline script and remote loading", () => {
  for (const page of LOCKED_PAGES) {
    const html = read(join(WEB, page));
    const csp = html.match(/<meta\s+http-equiv="Content-Security-Policy"\s+content="([^"]+)"/i)?.[1];
    assert.ok(csp, `${page}: no Content-Security-Policy`);

    const directive = name => csp.split(";").map(s => s.trim()).find(s => s.startsWith(name + " "));
    assert.match(csp, /default-src 'none'/, `${page}: default-src should be 'none'`);
    assert.ok(directive("script-src"), `${page}: no script-src`);
    assert.doesNotMatch(directive("script-src"), /unsafe-inline|unsafe-eval|\*|https?:\/\/(?!verso\.)/, `${page}: script-src is too open`);
    assert.match(csp, /base-uri 'none'/, `${page}: base-uri`);
    assert.match(csp, /form-action 'none'/, `${page}: form-action`);

    for (const source of csp.match(/https?:\/\/[^\s;]+/g) ?? []) {
      assert.match(source, /^https:\/\/verso\.[a-z.-]*example$/, `${page}: CSP allows a non-Verso origin: ${source}`);
    }
  }
});

test("the host's own pages have no inline script and no inline event handlers", () => {
  for (const page of LOCKED_PAGES) {
    const html = read(join(WEB, page));
    for (const m of html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)) {
      assert.match(m[1], /\bsrc=/, `${page}: inline <script> (CSP would block it)`);
    }
    assert.doesNotMatch(html, /\son[a-z]+\s*=\s*["']/i, `${page}: inline event handler (CSP would block it)`);
  }
});

test("the host's own pages load nothing from the internet", () => {
  const external = /(?:src|href)\s*=\s*"(https?:\/\/[^"]+)"|url\(\s*['"]?(https?:\/\/[^)'"]+)|@import\s+['"]?(https?:\/\/[^'";)]+)/gi;
  for (const page of LOCKED_PAGES) {
    const dir = dirname(join(WEB, page));
    const files = walk(dir).filter(f => /\.(html|css|js)$/.test(f) && !f.includes(`${sep}themes${sep}`));
    for (const f of files) {
      for (const m of read(f).matchAll(external)) {
        const url = m[1] ?? m[2] ?? m[3];
        assert.match(url, /^https:\/\/verso\.[a-z.-]*example/, `${rel(f)} loads ${url}`);
      }
    }
  }
});

test("dashboard themes do not pull in remote fonts or styles (the dashboard works offline)", () => {
  for (const f of all.filter(f => f.startsWith(join(WEB, "dashboard")) && /\.(css|json)$/.test(f))) {
    assert.doesNotMatch(read(f), /fonts\.googleapis|fonts\.gstatic|@import\s+url\(\s*['"]?https?:/i, `${rel(f)} reaches for the network`);
  }
});

test("interactive wallpaper frames are sandboxed", () => {
  for (const f of ["outpost/js/background.js", "shell/js/background.js"]) {
    assert.match(read(join(WEB, f)), /setAttribute\("sandbox"/, `${f}: the wallpaper iframe is not sandboxed`);
  }
});

// KNOWN GAP, kept visible rather than hidden: the classic desk's themes still ask Google Fonts for their typefaces,
// so the classic desk needs the network (and tells Google when it starts). The Outpost desk and the dashboard bundle
// theirs. Delete this expectation when the classic themes ship their fonts.
test("known gap: only the classic desk's themes use remote fonts", () => {
  const users = all.filter(f => /fonts\.googleapis\.com\/css/.test(read(f))).map(rel).sort();
  assert.deepEqual(users, [
    "themes/carbon-signal/theme.json",
    "themes/paper-recto/theme.json",
    "themes/smoked-petrol/theme.json"
  ]);
});
