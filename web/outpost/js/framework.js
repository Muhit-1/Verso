/* ==================================================================
   VERSO — Outpost widget framework

   THE INDEPENDENT WIDGET CONTRACT (CLAUDE.md, "ROUND 2"). Every widget is a self-contained module that is
   registered here and mounted/unmounted on its own:

   - its own element(s), added to the desk through ctx.mount() — never a shared container;
   - its own saved data, under its own key (ctx.state) — never another widget's;
   - its own timers, through ctx.every() — all cleared when the widget goes away, so nothing leaks;
   - its own on/off flag, kept by the host (settings.outpostWidgets) and switched from the dashboard or the
     desk's round menu. Switching one never touches another.

   A widget that throws while starting or stopping is contained: the others carry on.
================================================================== */

import * as Host from "/shell/js/host.js";
import * as Core from "./core.js";
import * as Layout from "./layout.js";

const defs = new Map();   // id -> definition { id, mount(ctx) -> { dispose?() } }
const live = new Map();   // id -> { ctx, instance }
const listeners = new Map();   // host event name -> Set of callbacks (see ctx.on)

/** The host tells the page something changed (apps, ...); only the widgets that asked for it hear about it. */
export function emit(name) {
  for (const fn of [...(listeners.get(name) ?? [])]) {
    try {
      fn();
    } catch (err) {
      console.error(`Verso: a listener for "${name}" failed`, err);
    }
  }
}

export function register(def) {
  defs.set(def.id, def);
}

function makeContext(id) {
  const timers = new Set();
  const elements = new Set();
  const subscriptions = new Set();   // [name, fn]

  return {
    id,
    host: Host.get(),

    /** This widget's own saved data. */
    state: {
      get: () => Core.getWidgetState(id),
      patch: patch => Core.patchWidgetState(id, patch)
    },

    /** A timer that is cleared automatically when the widget is turned off. */
    every(ms, fn) {
      const t = setInterval(fn, ms);
      timers.add(t);
      return t;
    },
    clear(t) {
      clearInterval(t);
      timers.delete(t);
    },

    /**
     * Puts one of this widget's elements on the desk, at its saved position or the default.
     * @param key  position key; defaults to the widget id. Multi-instance widgets pass their own.
     */
    mount(el, defaultPosition, key = id) {
      Layout.add(key, el, defaultPosition);
      elements.add(el);
      return el;
    },

    /** Takes one of this widget's elements off the desk (a multi-instance widget removing one of its own). */
    unmount(el) {
      Layout.remove(el);
      elements.delete(el);
    },

    /** Listens to a host event ("appsChanged"...). Dropped automatically when the widget goes away. */
    on(name, fn) {
      if (!listeners.has(name)) listeners.set(name, new Set());
      listeners.get(name).add(fn);
      subscriptions.add([name, fn]);
    },

    announce: Layout.toast,

    _cleanup() {
      for (const [name, fn] of subscriptions) listeners.get(name)?.delete(fn);
      subscriptions.clear();
      timers.forEach(clearInterval);
      timers.clear();
      elements.forEach(el => Layout.remove(el));
      elements.clear();
    }
  };
}

/** Starts the widgets that are on and stops the ones that are off — each on its own. */
export async function sync() {
  let list = [];
  try {
    list = await Host.get().getOutpostWidgets();
  } catch (err) {
    console.warn("Verso: could not read the widget list", err);
  }

  const wanted = new Map(list.map(w => [w.id, w.enabled]));

  for (const [id, def] of defs) {
    const want = wanted.get(id) ?? false;
    const running = live.get(id);

    if (want && !running) {
      const ctx = makeContext(id);
      try {
        live.set(id, { ctx, instance: def.mount(ctx) || {} });
      } catch (err) {
        console.error(`Verso: widget "${id}" failed to start`, err);
        ctx._cleanup();
      }
    } else if (!want && running) {
      try {
        running.instance.dispose?.();
      } catch (err) {
        console.error(`Verso: widget "${id}" failed to stop cleanly`, err);
      }
      running.ctx._cleanup();
      live.delete(id);
    }
  }
}

export const isRunning = id => live.has(id);
