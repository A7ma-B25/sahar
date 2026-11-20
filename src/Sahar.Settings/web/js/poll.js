// 0.14.1: the settings app's pollers — one per key (a page's re-render replaces its own, never adds one), at most
// every 1.5 s, skipped while the window is hidden or minimised (WebView2 reports it hidden), never two requests at once.
const live = new Map();
export const MIN_MS = 1500;

export function startPoll(key, fn, ms = MIN_MS, t = globalThis) {
  stopPoll(key, t);
  let busy = false;
  const id = t.setInterval(async () => {
    if (busy || (t.document && t.document.visibilityState !== 'visible')) return;
    busy = true;
    try { await fn(); } catch { /* the next tick tries again */ } finally { busy = false; }
  }, Math.max(MIN_MS, ms));
  live.set(key, { id, t });
}

export function stopPoll(key, t = globalThis) {
  const p = live.get(key);
  if (!p) return;
  (p.t || t).clearInterval(p.id);
  live.delete(key);
}

export const polling = () => live.size;
