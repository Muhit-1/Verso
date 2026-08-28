# Dashboard theme spec

A dashboard theme is a folder containing `theme.json` and `theme.css`,
living under `web/dashboard/themes/`. Same contract shape as
[theme-spec.md](theme-spec.md) — folder name is the id, both a bundled and
a user root are scanned, a user theme with a bundled theme's id replaces
it, `_`-prefixed folders are skipped — but a **different token set**, and
scanned separately. A dashboard theme has no effect on the desk, and a
desk theme has no effect on the dashboard.

The reason this is a second contract rather than reusing the desk's: the
desk's tokens (`--lume`, `--chalk`, `--panel-blur`, …) describe a
wallpaper widgets sit on top of. The dashboard is a normal application
window — a sidebar, cards, sliders, swatch pickers — and its two
reference designs (Fieldnote, Sunburst) differ in more than palette: they
disagree on border weight, shadow style (soft blur vs. hard offset) and
corner radius too. Geometry is therefore part of this contract, not left
to `web/dashboard/css/*.css` the way it would be if only colour varied.

## theme.json

Identical shape to a desk theme's `theme.json` — see
[theme-spec.md](theme-spec.md#themejson). `scheme` still means `dark` or
`light`.

## The tokens

Every token must be defined; a missing one falls back to the reference
values in `web/dashboard/css/base.css`.

### Surfaces

| token | |
|---|---|
| `--ink` | Primary text. |
| `--ink-muted` | Secondary text: hints, descriptions, timestamps. |
| `--surface` | The window's own background. |
| `--sidebar` | Sidebar background — a theme may make this the same as `--surface`, or distinct. |
| `--card` | Panel/card fill: preview boxes, list rows, the theme-picker cards. |
| `--border` | Hairline/border colour used everywhere a rule is drawn. |

### Accent

| token | |
|---|---|
| `--accent` | The one interactive colour: active nav item, selected swatch, primary buttons, the "on" state of a switch. |
| `--accent-soft` | Tinted background for an active/selected state that isn't a solid fill. |
| `--accent-2` | A second accent for a second meaning only — e.g. a widget preview's own colour distinct from the UI's own active-state colour. Never used for interactive state. |
| `--on-accent` | Text/icon colour sitting on a solid `--accent` fill. |

### Geometry

| token | |
|---|---|
| `--border-width` | Hairline width used on cards, chips and rows. |
| `--radius-card` | Corner radius for cards, list rows, preview boxes. |
| `--radius-pill` | Corner radius for pills/chips/switches (`999px` for a true pill). |
| `--shadow-card` | Full CSS `box-shadow` value. A soft theme uses blur; a theme like Sunburst uses a hard offset in `--border` — either is valid, the value is opaque. |

### Type

| token | |
|---|---|
| `--font-display` | Section headings and the sidebar's brand mark only. |
| `--font-sans` | Body copy: labels, descriptions, list rows. |
| `--font-mono` | Numeric/tabular text: percentages, slider values. |

Every stack must end in a face already on a stock Windows machine — the
dashboard has to read correctly before any network exists, same rule as
the desk.

## Writing one

1. Copy `web/dashboard/themes/_template/`, rename the folder.
2. Fill in every token above.
3. Set `name`, `description` and `fonts` in `theme.json`.
4. Add the folder name to `web/dashboard/themes/themes.json` if you want
   to see it when `web/dashboard/index.html` is opened directly in a
   plain browser (no native host to list a directory — same reason
   `web/themes/themes.json` exists).
5. Pick it from the dashboard's own Theme section.
