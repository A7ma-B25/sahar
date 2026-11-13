// The page ↔ host bridge (plan 0.10c Task 3): requests carry an id; replies echo it. Anything else is a push.
const pending = new Map();
const pushHandlers = [];
let nextId = 1;
const webview = globalThis.chrome && globalThis.chrome.webview;

if (webview) {
  webview.addEventListener('message', (e) => {
    let msg;
    try { msg = JSON.parse(e.data); } catch { return; }
    if (msg && msg.id != null && pending.has(msg.id)) {
      const { resolve, timer } = pending.get(msg.id);
      clearTimeout(timer);
      pending.delete(msg.id);
      resolve(msg);
      return;
    }
    for (const h of pushHandlers) h(msg);
  });
}

/** Sends {id, type, ...body}; resolves with the reply (10 s at most, then {ok:false, error:'timeout'}). */
export function request(type, body = {}) {
  const id = nextId++;
  return new Promise((resolve) => {
    if (!webview) { resolve({ id, type, ok: false, error: 'no-host' }); return; }
    const timer = setTimeout(() => { pending.delete(id); resolve({ id, type, ok: false, error: 'timeout' }); }, 10000);
    pending.set(id, { resolve, timer });
    webview.postMessage(JSON.stringify({ ...body, id, type }));
  });
}

/** A message the host sends unasked ({type:"focus"}). */
export function onPush(handler) { pushHandlers.push(handler); }

/** Tells the host something without waiting (ready, theme). */
export function tell(type, body = {}) {
  if (webview) webview.postMessage(JSON.stringify({ ...body, type }));
}
