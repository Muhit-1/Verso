# Live wallpapers

Backgrounds you set from the tray's **Set background…** dialog (or the
dashboard) — the video/interactive side of Verso, not the widget side.
See [docs/plan.md](../../docs/plan.md), Phase 3.

This is not a scanned catalog the way `web/widgets/` and `web/themes/` are.
A background is picked as a single file, so there is no manifest and
nothing to register — you just point the file picker at one of these.

```
live wallpaper/
  interactive wallpaper/   HTML/CSS/JS pages — a full-bleed web page,
                            exactly like a widget's index.html but with
                            no host, no permissions, nothing to declare.
                            "Set background…" → pick its index.html.

  live wallpaper/           Video files (.mp4, .webm) you've collected —
                            downloaded from wherever, dropped here for
                            your own organization. "Set background…" →
                            pick the file directly.
```

## Writing an interactive wallpaper

It's a web page. That's the whole spec:

- Must render something reasonable at any window size — it fills the
  whole screen, at whatever resolution the desktop is.
- Self-contained: no `window.VersoHost`, no permissions, no manifest.
  A background isn't loaded through the widget loader, so none of
  `docs/widget-spec.md`'s rules apply here — pick your own colours,
  draw your own everything.
- Keep it cheap. This runs for as long as the desktop is up, same as
  every other rule in this project about not being a battery hog.

See `interactive wallpaper/ripple/` for a real one, not a stub — a
GPU-driven water ripple that responds to clicks.
