# Theme spec

A theme is a folder containing `theme.json` and `theme.css`. It may contain
nothing else that matters — no selectors, no layout, no markup, no widget
names. A theme can only change what the tokens listed here evaluate to.

That constraint is the whole point. It is why a theme written a year from
now still works, and why a widget written by someone else is correctly
themed without either author knowing about the other.

## Where themes live

| | |
|---|---|
| `web/themes/` | ships with the app |
| `%APPDATA%\Verso\themes\` | installed by the user |

Both are scanned. A user theme whose folder name matches a bundled one
replaces it. Folders starting with `_` or `.` are skipped.

Opened directly in a browser the shell cannot list a directory, so it reads
`web/themes/themes.json` instead. The native host ignores that file.

## theme.json

```json
{
  "id": "smoked-petrol",
  "name": "Smoked Petrol",
  "author": "Verso",
  "version": "1.0.0",
  "description": "One line, shown in the picker.",
  "scheme": "dark",
  "stylesheet": "theme.css",
  "preview": null,
  "fonts": ["https://fonts.googleapis.com/css2?family=..."],
  "minShellVersion": "0.1.0"
}
```

| field | |
|---|---|
| `id` | Must equal the folder name. The folder wins if they disagree. |
| `scheme` | `dark` or `light`. Sets `data-scheme` on `<html>`; does not change any token. |
| `stylesheet` | Relative to the theme folder. |
| `preview` | Optional swatch for the picker, relative to the theme folder. |
| `fonts` | Stylesheet URLs injected **before** the theme. Never `@import` inside `theme.css` — an `@import` blocks the first paint of the whole desk. |

## The tokens

Every token must be defined. Leaving one out is worse than getting it
wrong: the shell falls back to the reference values in
`web/shell/css/base.css` and you get a confusing mix of two themes.

### Ground

| token | |
|---|---|
| `--ink` | The base colour behind everything. |
| `--ink-deep` | The darkest, or in a light theme the heaviest, shade. |
| `--ink-panel` | Panel fill. Keep it translucent or `--panel-blur` does nothing. |
| `--stage` | A full CSS `background` value — gradients, a colour, anything. Composited over `--photo`. |
| `--photo` | `none`, or `url('your-image.jpg')` relative to `theme.css`. |
| `--grain` | Opacity of the noise layer, 0–1. Around 0.04–0.06. |
| `--grain-blend` | `overlay` for dark themes, `multiply` for light. |

The grain is not decoration. Large flat dark gradients band badly on 8-bit
panels, and the noise dithers it away.

### Primary accent — live, now, active

This is the current minute, the seconds register, the playhead, a ticked
task. **Give it exactly one job.** One accent that means one thing is the
design; a second meaning dilutes it into decoration.

| token | |
|---|---|
| `--lume` | The accent itself. |
| `--lume-dim` | Translucent, for borders and de-emphasised states. |
| `--lume-wash` | The faintest wash. Row hover. |
| `--lume-glow` | The colour of glows and shadows around `--lume`. |
| `--lume-hot` | Hover state of a solid `--lume` button. |
| `--on-lume` | Text and icons sitting **on** a solid `--lume` fill. A contrast decision, not a taste one. |

### Secondary accent — structure

Rules, indices, counts, hairlines. Never content.

`--brass` · `--brass-dim` · `--brass-hair`

### Type

| token | |
|---|---|
| `--chalk` | Primary text. |
| `--slate` | Secondary: labels, artists, dates. |
| `--slate-deep` | Tertiary: timestamps, done tasks, empty states. |

### Structure

| token | |
|---|---|
| `--rule-faint` | Separators between rows. |
| `--track` | The unfilled part of the progress bar. |
| `--tick` | A minute not yet reached. |
| `--tick-major` | Every fifth minute, not yet reached. |
| `--tick-spent` | A minute already spent. |
| `--tick-spent-major` | Every fifth minute, already spent. |

### Panels

Geometry is a theme's business too. `--panel-radius: 0` makes the desk read
as equipment; `12px` makes it read as software.

`--panel-blur` · `--panel-radius` · `--panel-shadow` · `--dial-shadow`

| token | |
|---|---|
| `--panel-solid` | **Opaque** panel fill, used in overlay mode. |

`--panel-solid` is not a duplicate of `--ink-panel`. Over the stage a panel is
translucent and blurred, and that is the whole look. In overlay mode there is
no stage behind it — the window is reshaped to the panel itself — so a
translucent fill composites against nothing and the panel loses its body.
Give `--panel-solid` the colour the translucent panel *appears* to be over
your stage, with no alpha.

`--panel-radius` does double duty in overlay mode: the host rounds the window
region's corners to match it, so the shape cut out of the desktop follows the
panel exactly.

### Type stacks

| token | |
|---|---|
| `--font-display` | The clock, and only the clock. |
| `--font-sans` | Body copy: tasks, track titles. |
| `--font-mono` | Labels, numbers, anything tabular. |

**Every stack must end in a face already present on a Windows machine.**
The desk has to read correctly at boot, before any network exists.

### Layout — optional

Proportions rather than colour, so most themes leave these alone. Override
them if your composition needs different ones.

| token | |
|---|---|
| `--safe` | Left column kept empty for desktop icons. |
| `--rail` | Width of the panel rail on the right. |
| `--taskbar` | Strip kept clear at the bottom. |

Two of these are load-bearing beyond aesthetics. `--safe` is why the
desktop icons are still usable, and `--taskbar` is why the taskbar does not
sit on top of a widget. A theme that zeroes them had better mean it.

## Writing one

1. Copy `web/themes/_template/`, rename the folder.
2. Fill in every token.
3. Set `name`, `description` and `fonts` in `theme.json`.
4. Add the folder name to `web/themes/themes.json` if you want to see it in
   a plain browser.
5. Press <kbd>T</kbd> on the desk to cycle, or use `?theme=your-theme`.

## Testing one

Check it against all three of these before calling it done.

- **At night and in daylight.** A theme tuned in a dark room reads as
  murk at noon.
- **With no network.** Disconnect and reload: the fallback faces in your
  stacks are what people see for the first second of every boot.
- **With `prefers-reduced-motion` on.** The drift and the transitions stop.
  A theme leaning on motion to hide a flat gradient falls apart here.

`paper-recto` exists mainly as a test of the contract, because a light
theme is where a design that leaned on glows falls apart. Read its comments
if you are porting a dark theme to light.
