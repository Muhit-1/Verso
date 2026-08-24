# Host API

The one door between a widget and the machine.

Everything a widget can do to the computer it runs on, it does through
`window.VersoHost`. Nothing else. A widget never calls into .NET directly,
never opens a file, never reaches the network unless it declared that it
would.

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
| media | **Stubbed.** `getNowPlaying()` returns `null`, so the web layer falls back to its demo player. Phase 3 fills it from `GlobalSystemMediaTransportControlsSessionManager`, which reports whatever Spotify, YouTube Music or a browser tab is playing, and gives transport control back for free. |

`onNowPlayingChanged` is currently driven by a one-second poll rather than
an event. One call a second across a COM proxy costs nothing measurable, and
it means this file does not change shape when the real event arrives in
Phase 3.

## Rules for adding to this API

- **Everything crossing the boundary is data.** Never a handle, a path, a
  process, or anything a widget could use to reach past the host.
- **Every addition needs a permission.** See
  [widget-spec.md](widget-spec.md). A widget gets what it declared and
  nothing else.
- **Add it to the demo host too**, or you have just broken the ability to
  develop widgets in a browser.
- **Nothing blocking.** If it can take longer than a frame, it caches.
