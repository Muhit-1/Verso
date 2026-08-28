# Widget spec

A widget is a folder with an `index.html` and a `widget.json`. That is the
entire format. No build step, no framework, no registration.

Written down now, at Phase 0, rather than at Phase 5 where the plan
originally put it — because it costs an hour today and it means the first
widgets are born conformant instead of being rewritten later.

## The folder

```
my-widget/
  widget.json     required
  index.html      required
  anything-else   your own css, js, images
```

| | |
|---|---|
| `web/widgets/` | ships with the app |
| `%APPDATA%\Verso\widgets\` | installed by the user |

Folders starting with `_` or `.` are skipped, which is how `_template`
stays out of the picker.

## widget.json

```json
{
  "id": "clock",
  "name": "Clock",
  "author": "you",
  "version": "1.0.0",
  "description": "One line, shown in the picker.",
  "entry": "index.html",
  "permissions": ["media"],
  "defaultSize": { "w": 4, "h": 3 },
  "interactive": true,
  "tickMs": 1000,
  "minShellVersion": "0.1.0"
}
```

| field | |
|---|---|
| `id` | Must equal the folder name. The folder wins if they disagree. |
| `entry` | Relative to the widget folder. |
| `permissions` | What the widget may reach. See below. |
| `defaultSize` | Grid cells, used the first time it is placed. |
| `interactive` | Whether it needs mouse input forwarded to it. Say `false` if you can — forwarding is expensive and it is the hard part of the whole app. |
| `tickMs` | How often you intend to repaint, in ms. `0` means event-driven only. The host uses this to throttle you when you are not visible. |

## Permissions

A widget is granted exactly what it declares, and nothing else. Ask for
nothing and the bridge you get has no methods on it.

| | |
|---|---|
| `tasks` | Read and write the task list. |
| `media` | Read what is playing; control transport. |
| `apps` | List and launch the apps the user added from the dashboard. |
| `system` | CPU, memory, battery, network. |
| `network` | Reach the internet at all. |

`apps` is deliberately narrow: `launchApp(id)` only accepts an id already
returned by `getLauncherEntries()`, which only ever contains what the user
picked themselves from a native file dialog. A widget cannot add an entry
and cannot launch an arbitrary path — see [host-api.md](host-api.md).

Declaring `network` is a visible choice on purpose. A widget that phones
home should have to say so in a file the user can read.

## The four rules

**1. Reach the machine only through `window.VersoHost`.**
Never fetch a local file, never touch `chrome.webview` directly, never
assume a .NET object exists. If the host is absent, degrade to something
sensible and keep rendering. Full API in [host-api.md](host-api.md).

Look it up **fresh every time you need it — never cache it once at the top
of your script.** The loader sets `window.VersoHost` on your iframe only
after your own script has already started running, so `const host =
window.VersoHost` at module scope reads `null` even when a host is on its
way. Reading it again inside `paint()` — which `tickMs` already has you
calling on a timer — costs nothing and always sees the current answer.
`_template/index.html` shows the pattern.

**2. Never set a colour, a font or a radius of your own.**
Use the theme tokens. They are inherited from the shell, so a widget
written today is correctly themed by a theme written next year. Full list
in [theme-spec.md](theme-spec.md).

**3. Do not draw your own panel.**
The shell draws the background, border, blur and padding around you.
Drawing your own gives a double frame — and your blur would not work
anyway, because `backdrop-filter` cannot see through an iframe boundary.
Your background stays `transparent`.

**4. Repaint only when something changed.**
Compare against the last value before touching the DOM. On a wallpaper that
runs for eight hours straight, that is the difference between idle and a
warm laptop. Declare your rate in `tickMs` and hold to it.

## It must run standalone

`index.html` opened directly in a browser has to work. That is not a
convenience — a widget that needs the host in order to render is a widget
nobody else can develop, and a widget system nobody can develop for is
worth nothing.

```js
const host = window.VersoHost ?? null;
```

Check, do not assume.

## Isolation

Each widget will be rendered in its own frame, so one widget's CSS can
never touch another's and one widget's crash cannot take the desk down. It
is also why rule 3 exists: the panel chrome has to be drawn outside the
frame for the blur to composite against the wallpaper.

## Status

The manifest format above is settled and `web/widgets/_template/` is the
reference implementation of it.

The **loader** that reads these folders and renders them into the desk
lands in **Phase 5**. Until then the three built-in complications — dial,
tasks, player — are drawn by the shell itself, in
`web/shell/js/desk.js`. Widgets written against the template now will not
need rewriting when the loader arrives.

## Starting one

```bash
node tools/serve.mjs
```

Then open `http://localhost:5173/widgets/_template/index.html`.
