/*
 * A static file server for the web layer, with no dependencies.
 *
 *   node tools/serve.mjs          -> http://localhost:5173/shell/index.html
 *   node tools/serve.mjs 8080
 *
 * The web layer has no build step and never will — a widget is meant to be
 * a folder anyone can write in an afternoon. But it does use ES modules and
 * fetch, and both need a real origin, so file:// will not do.
 *
 * The native host serves the same folder over a virtual https:// host, so
 * what you see here is what you get on the desktop.
 */
import { createServer } from "node:http";
import { createReadStream } from "node:fs";
import { stat } from "node:fs/promises";
import { extname, join, normalize, sep } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = join(fileURLToPath(new URL("../", import.meta.url)), "web");
const PORT = Number(process.argv[2]) || 5173;

const TYPES = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".mjs": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".svg": "image/svg+xml",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".webp": "image/webp",
  ".woff2": "font/woff2",
  ".ico": "image/x-icon"
};

createServer(async (req, res) => {
  const url = new URL(req.url, `http://localhost:${PORT}`);
  const path = decodeURIComponent(url.pathname);

  // Redirect rather than serve the shell in place: every stylesheet and
  // module in it is a relative path, and those resolve against the URL the
  // browser thinks it is on, not the file we happened to send.
  if (path === "/") {
    res.writeHead(302, { location: "/shell/index.html" }).end();
    return;
  }

  // normalize() collapses any ../ before it reaches the filesystem.
  const target = join(ROOT, normalize(path));
  if (!target.startsWith(ROOT + sep)) {
    res.writeHead(403).end("forbidden");
    return;
  }

  try {
    const info = await stat(target);
    if (info.isDirectory()) {
      res.writeHead(302, { location: path.replace(/\/?$/, "/") + "index.html" }).end();
      return;
    }

    res.writeHead(200, {
      "content-type": TYPES[extname(target).toLowerCase()] ?? "application/octet-stream",
      "content-length": info.size,
      // Editing a theme and hitting reload has to show the change.
      "cache-control": "no-store"
    });
    createReadStream(target).pipe(res);
  } catch {
    res.writeHead(404, { "content-type": "text/plain" }).end(`not found: ${path}`);
  }
}).listen(PORT, () => {
  console.log(`Verso web layer  ->  http://localhost:${PORT}/shell/index.html`);
  console.log(`serving           ${ROOT}`);
});
