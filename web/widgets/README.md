# Widgets

A widget is a folder with two files.

```
my-widget/
  widget.json   what it is, and what it needs
  index.html    the widget itself
```

Copy `_template/`, rename it, edit those two files. There is no build step
and nothing to register.

## The rules

1. **Reach the machine only through `window.VersoHost`.** Never fetch a
   local file, never touch `chrome.webview` directly, never assume a .NET
   object exists. Full API in [docs/host-api.md](../../docs/host-api.md).
   Look it up fresh every time you need it, inside `paint()` — not once at
   the top of the file. The loader injects it after your script starts
   running, so a cached `const` reads `null` forever even once a host
   arrives.
2. **Never set a colour, a font or a radius of your own.** Use the theme
   tokens — they are inherited from the shell, so a widget written today is
   correctly themed by a theme written next year.
   See [docs/theme-spec.md](../../docs/theme-spec.md).
3. **Do not draw your own panel.** The shell draws the background, border,
   blur and padding around you.
4. **Repaint only when something changed**, and declare how often you intend
   to in `tickMs`. The host uses it to throttle you when you are not visible.

`index.html` must work when opened directly in a browser. That is not a
convenience — a widget that needs the host in order to render is a widget
nobody else can develop.

## Permissions

A widget is granted exactly what it declares in `widget.json`, and nothing
else. Ask for nothing and the bridge you get has no methods on it.

| | |
|---|---|
| `tasks` | read and write the task list |
| `media` | read what is playing and control transport |
| `apps` | list and launch the apps the user added from the dashboard |
| `system` | CPU, memory, battery, network |
| `network` | reach the internet at all |

## Status

The manifest format is settled and the template is the reference for it.
The loader that renders these folders into the desk lands in **Phase 5** —
until then the three built-in complications are drawn by the shell itself.
Widgets written against this template now will not need rewriting.

Full spec: [docs/widget-spec.md](../../docs/widget-spec.md).
