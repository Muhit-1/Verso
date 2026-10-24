# Security model

Verso shows pages it did not write (an interactive wallpaper is somebody's HTML; a user can install widgets and
themes) next to a host that can start programs and move files. This is how those two are kept apart. It is also the
list of things to re-check after a WebView2 update.

## Trust levels

| What | Trusted? | Reaches the host bridge? |
|---|---|---|
| `web/outpost`, `web/dashboard`, `web/picker`, `web/editor` (shipped) | yes | yes — their own bridge each |
| Classic desk (`web/shell`, `web/themes`, `web/widgets`) | yes, but loads remote fonts | yes |
| User themes and widgets (`%APPDATA%\Verso\themes`, `widgets`, `dashboard-themes`) | **no** | must not |
| An interactive wallpaper (any HTML the user picks) | **no** | must not |
| Video, GIF, image wallpapers | not code | n/a |

## What is enforced

**On the host (`Hosting/WebViewGuard.cs`, applied to every WebView2 window)**

- The top frame can only be `https://verso.example/...`. Anything else is cancelled and logged.
- Frames can load Verso's own virtual hosts and `about:`; nothing from the internet or the disk.
- New windows, downloads and permission requests (camera, microphone, location, ...) are all refused.
- A web message is data: its shape is checked and its numbers clamped before use (`DeskWindow.OnWebMessage`).

**On the page**

- `outpost`, `dashboard`, `picker`, `editor` carry a Content-Security-Policy: `default-src 'none'`, scripts only from
  their own origin, no inline script, no `eval`, no remote origin other than Verso's own virtual hosts.
  `tests/web` fails the build if one of them loses it, gains an inline script, or starts loading from the internet.
- The wallpaper `<iframe>` is `sandbox="allow-scripts allow-same-origin"` with no referrer: no popups, no
  navigating the desk away, no forms. It is also a different origin (`verso.bg.example`) from the desk.
- Sticky notes are rich text, so the host reduces them to `b i u s strike ul ol li br div p` with no attributes before
  they are stored (`NotesLibrary.Sanitize`, covered by `NotesSanitizeTests`).

**On the data**

- Only a full path or `shell:AppsFolder\...` can be launched (`LauncherLibrary.IsAllowedTarget`). A hand-edited
  `settings.json` cannot turn a tile into a web address or an arbitrary `shell:` verb.
- Virtual-host mappings are narrow: the web folder, the three user folders, and the one folder holding the chosen
  wallpaper. `settings.json` and the notes are never served.
- Files are written to a temp file and moved into place; the previous version is kept as `.bak`.

## To verify on Windows (not provable from the code)

WebView2 exposes `AddHostObjectToScript` objects to the page; whether a **cross-origin iframe** inside that page can
see them depends on the WebView2 runtime version. Verso assumes it cannot. Check it, and again after every WebView2
SDK or runtime update:

1. Run Verso. In the dashboard choose `tools/probe-wallpaper/index.html` as an **interactive** background.
2. Look at the desk. Every line should say `PASS`.
3. If a `VersoHost` line says `FAIL`, an untrusted wallpaper can call the bridge. Fix before shipping: serve
   interactive wallpapers from a second WebView2 (no host objects) instead of an iframe, or register the objects on a
   `CoreWebView2Frame` for the trusted origin only.

## Known gaps

- Classic desk themes still load Google Fonts at runtime (`web/themes/*/theme.json`): the classic desk needs the
  network, and tells Google when it starts. The Outpost desk and dashboard bundle their fonts. `tests/web` pins this
  list so it cannot grow unnoticed.
- The classic desk has no Content-Security-Policy yet (it injects theme CSS and fonts dynamically).
- User widgets run in same-folder iframes with whatever `VersoHost` the classic shell hands them; there is no
  per-widget permission prompt yet.
- Releases are not code-signed, so Windows SmartScreen will warn on first run.
