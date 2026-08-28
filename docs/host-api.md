# Host API

The one door between a widget and the machine.

Everything a widget can do to the computer it runs on, it does through
`window.VersoHost`. Nothing else. A widget never calls into .NET directly,
never opens a file, never reaches the network unless it declared that it
would.

**The dashboard uses a different, separate door.** `web/dashboard/` reaches
`window.chrome.webview.hostObjects.VersoDashboard` — a second bridge
(`DashboardBridge`/`IVersoDashboard`) carrying privileges no widget should
ever have (enable a widget, hide the taskbar, pick a background file, add
or remove a launcher entry). It is injected only into the dashboard's own
WebView2, never the desk's, and the rest of this document — the shape,
the three layers, the rules for adding to it — is about `VersoHost`
specifically. See the remarks on `IVersoDashboard` in
`Verso.Core/Contracts/IVersoDashboard.cs` for the dashboard's own contract.

Two reasons this boundary is absolute. It is how the app eventually reaches
macOS and Linux by rewriting only the host. And it is the only version of
this that has a business in it, because it is the only version where other
people can safely write widgets.

## The shape

```js
VersoHost.hostVersion                      // "0.1.0"

VersoHost.loadTasks()                      // -> [{id, text, done}]
VersoHost.saveTasks(tasks)                 // -> void

VersoHost.getNowPlaying()                  // -> NowPlaying | null
VersoHost.getQueue()                       // -> [{title, artist}]
VersoHost.play()
VersoHost.pause()
VersoHost.next()
VersoHost.previous()
VersoHost.onNowPlayingChanged(cb)

VersoHost.getThemeId()                     // -> "smoked-petrol"
VersoHost.setThemeId(id)
VersoHost.getThemes()                      // -> [ThemeManifest]

VersoHost.getLauncherEntries()             // -> [{id, name}]
VersoHost.launchApp(id)
```

```ts
NowPlaying = {
  title, artist, album,
  durationMs, positionMs,
  isPlaying,
  art: [hexA, hexB]   // two colours sampled from the artwork, dark first
}
```

`art` is two colours rather than a bitmap on purpose. The widget paints a
gradient from them — cheaper to render, and it degrades gracefully when the
artwork is unavailable, which for browser-based players is most of the time.

## Three layers, and why

```
  widget            calls the small synchronous API above
    |
  web/shell/js/host.js
    |               caches, falls back to demo data, normalises async→sync
    |
  ScriptBridge (C#) COM-visible, exchanges JSON strings
    |
  IVersoHost (C#)   the typed contract; what a future Mac host implements
```

**The API a widget sees is synchronous.** The COM proxy underneath is not —
every call across it returns a promise. So `host.js` resolves the bridge
once at startup, keeps a cache, refreshes it in the background, and hands
out the last known value. A widget repainting once a second must never
await anything.

**`ScriptBridge` exchanges JSON strings, not objects.** That looks clumsy
next to the typed `IVersoHost`, and it is deliberate: WebView2 marshals host
objects through COM, and COM does not carry .NET collections or nullable
value types across to JS in any way worth debugging at 1am. One string, one
`JSON.parse`, no marshalling surprises. Note the capitalised method names on
the raw bridge — `LoadTasks`, not `loadTasks` — because it is a .NET object.
`host.js` is what presents the lowercase API.

**There is always a host.** If the bridge is missing, or present but
throwing, `host.js` substitutes a self-contained demo implementation with
seeded tasks and a fake player that advances its own playhead. A widget desk
rendering demo data is a far better failure than one rendering nothing, and
it is what lets the entire web layer be built before any C# exists.

Check which one you got with `document.body.dataset.host` — `"host"` or
`"demo"`.

## For a widget specifically

A widget's `window.VersoHost` is not the raw bridge above — it is a
*permission-scoped* view of it, built by `web/shell/js/widgets.js` from the
same already-connected host the shell itself uses. It carries only the
methods the widget's `permissions` in `widget.json` declared (`tasks` →
`loadTasks`/`saveTasks`; `media` → the transport methods); ask for nothing
and the object has nothing beyond `hostVersion` on it.

It is set on the widget's iframe **after that iframe's own script has
already started running** — see the timing note on rule 1 in
[widget-spec.md](widget-spec.md). Look it up fresh each time you need it,
not once at the top of the file.

## Wiring it up, on the C# side

```csharp
core.AddHostObjectToScript("VersoHost", bridge);
```

That single line is the whole integration. The class it is given must be
`[ComVisible(true)]` and `[ClassInterface(ClassInterfaceType.AutoDual)]`.

The web layer is served from `https://verso.local/` via
`SetVirtualHostNameToFolderMapping` rather than from `file://`, because
`file://` pages get no useful `localStorage`, cannot fetch sibling files,
and cannot load module scripts.

## What is real, and what is not

| | |
|---|---|
| tasks | **Real.** Persisted to `%APPDATA%\Verso\tasks.json`, written atomically. |
| themes | **Real.** Scans both theme folders; the choice persists to `settings.json`. |
| widgets | **Real.** Scans both widget folders; see [widget-spec.md](widget-spec.md) and `web/shell/js/widgets.js`. |
| launcher | **Real.** Entries are added from the dashboard, persisted to `settings.json`, launched via `Process.Start`. |
| media | **Real.** `SmtcNowPlayingSource` wraps `GlobalSystemMediaTransportControlsSessionManager`, which reports whatever Spotify, YouTube Music or a browser tab is playing, and gives transport control back for free. `null` is still a real, correct answer — it means Windows currently has no active media session, and the web layer's demo-player fallback is exactly the right response to that. |

`onNowPlayingChanged` is driven by a one-second poll rather than a push
event, deliberately, even though the C# side does raise real events —
COM events do not survive the JS boundary cleanly, and one call a second
across the proxy costs nothing measurable.

## Rules for adding to this API

- **Everything crossing the boundary is data.** Never a handle, a path, a
  process, or anything a widget could use to reach past the host.
- **Every addition needs a permission.** See
  [widget-spec.md](widget-spec.md). A widget gets what it declared and
  nothing else.
- **Add it to the demo host too**, or you have just broken the ability to
  develop widgets in a browser.
- **Nothing blocking.** If it can take longer than a frame, it caches.
