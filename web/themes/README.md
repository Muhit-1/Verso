# Themes

A theme is a folder with two files.

```
my-theme/
  theme.json    what it is called and what fonts it needs
  theme.css     one :root block of tokens
```

That is the whole format. A theme contains **no markup, no layout and no
widget names** — it can only change what the tokens in
`web/shell/css/base.css` evaluate to. That constraint is what makes a theme
written a year from now still work.

## Add one

1. Copy `_template/` and rename it. The folder name is the theme's id, so
   keep it lowercase-kebab-case.
2. Fill in every token in `theme.css`. Leaving one out is worse than
   getting it wrong — the shell falls back to the reference values and you
   get a confusing mix of two themes.
3. Set `name` and `description` in `theme.json`, and list any webfonts in
   its `fonts` array.
4. Add the folder name to `themes.json` — **only** needed to see it when
   opening `web/shell/index.html` directly in a browser, because a page
   cannot list a directory. The native host scans the folders and ignores
   that file.

Folders starting with `_` or `.` are skipped, which is how `_template`
stays out of the picker.

## Try one

Press <kbd>T</kbd> on the desk to cycle through installed themes, or append
`?theme=carbon-signal` to the URL to force one. The choice is remembered.

## Where they live

| | |
|---|---|
| `web/themes/` | ships with the app |
| `%APPDATA%\Verso\themes\` | installed by the user |

A user theme with the same folder name as a bundled one replaces it, which
is how you retint a stock theme without editing anything under Program
Files.

## The three that ship

| | | |
|---|---|---|
| **Smoked Petrol** | dark | The reference. Petrol ground, gilt indices, pale luminova green for what is live. |
| **Paper Recto** | light | Warm paper and ink. Proves the contract holds in the direction where glow-based designs fall apart. |
| **Carbon Signal** | dark | Near-black, steel, signal orange. Hard corners and a mono-forward stack — proves tokens carry geometry and type, not just colour. |

The full token reference, and what each one is for, is in
[docs/theme-spec.md](../../docs/theme-spec.md).
