// The panel page: one warm WebView2 for pickers, input cards, ask bubbles, the tray menu and «الدرج». The host sends
// {t:'show'|'update'|'place'|'hide'|'theme'}; the page answers {t:'ready'|'measured'|'shown'|'event'|'hidden'}.
import { mount } from './h.js';
import { VIEWS, visibleRows } from './views.js';

const web = window.chrome && window.chrome.webview;
const post = (m) => web && web.postMessage(m);
const stage = document.getElementById('stage');

let cur = null;   // { seq, kind, model, st, margin }
let composing = false;

function emit(kind, value) {
  if (!cur) return;
  const st = cur.st;
  if (kind === 'confirm') { st.confirm = true; render(); return; }
  if (kind === 'confirm-no') { st.confirm = false; render(); return; }
  post({ t: 'event', seq: cur.seq, kind, value: value == null ? null : String(value) });
}

function render(first = false) {
  if (!cur) return;
  const view = VIEWS[cur.model.kind];
  if (!view) return;
  const old = stage.querySelector('[data-id=search],[data-id=value]');
  const caret = old && document.activeElement === old ? [old.selectionStart, old.selectionEnd] : null;
  const tree = view(cur.model, cur.st, emit);
  const el = mount(tree);
  if (!first) el.classList.remove('pop');
  el.style.left = cur.margin.l + 'px';
  el.style.top = cur.margin.t + 'px';
  stage.replaceChildren(el);
  if (cur.place) clasp(el);
  const field = el.querySelector('[data-id=search],[data-id=value]');
  if (field) {
    field.addEventListener('compositionstart', () => { composing = true; });
    field.addEventListener('compositionend', () => { composing = false; typed(field); });
    field.addEventListener('input', () => { if (!composing) typed(field); });
    if (first || caret) {
      field.focus();
      if (caret) field.setSelectionRange(caret[0], caret[1]);
      else field.setSelectionRange(field.value.length, field.value.length);
    }
  } else if (first) {
    (el.querySelector('.row.sel, .opt, .btn.pri, .item, button') || el).focus?.();
  }
  const sel = el.querySelector('[aria-selected=true]');
  if (sel) sel.scrollIntoView({ block: 'nearest' });
}

let liveTimer = 0;
function typed(field) {
  const st = cur.st;
  if (field.dataset.id === 'search') { st.query = field.value; st.selected = 0; render(); return; }
  st.value = field.value;
  st.dirty = true;   // his new text hides the old error until he submits again
  st.sugg = 0;
  if (cur.model.live) { clearTimeout(liveTimer); liveTimer = setTimeout(() => emit('input', field.value), 300); }
  render();
}

function clasp(card) {
  const p = cur.place;
  if (p.mode !== 'beside') return;
  const d = document.createElement('span');
  d.className = 'clasp';
  d.setAttribute('aria-hidden', 'true');
  d.style.top = (p.claspY - 7) + 'px';
  d.style[p.left ? 'right' : 'left'] = '-7px';
  card.appendChild(d);
}

function choices() {
  const m = cur.model;
  if (m.kind === 'list') return visibleRows(m, cur.st.query);
  if (m.kind === 'input') return m.suggestions || [];
  if (m.kind === 'ask' || m.kind === 'choose') return m.rows && m.rows.length ? m.rows : [];
  return [];
}

document.addEventListener('keydown', (e) => {
  if (!cur) return;
  const m = cur.model;
  const st = cur.st;
  const field = document.activeElement && document.activeElement.matches && document.activeElement.matches('input,textarea') ? document.activeElement : null;
  if (e.key === 'Escape') { e.preventDefault(); if (st.confirm) { st.confirm = false; render(); } else emit('close'); return; }
  if (composing || e.isComposing) return;
  const list = choices();
  if ((e.key === 'ArrowDown' || e.key === 'ArrowUp') && list.length) {
    e.preventDefault();
    const key = m.kind === 'input' ? 'sugg' : 'selected';
    const at = st[key] ?? (st.moved ? 0 : -1);
    st[key] = Math.max(0, Math.min(list.length - 1, at + (e.key === 'ArrowDown' ? 1 : -1)));
    st.moved = true;
    render();
    return;
  }
  if (m.back && (e.key === 'ArrowRight' || e.key === 'Backspace') && (!field || field.value === '')) { e.preventDefault(); emit('back'); return; }
  if ((m.kind === 'ask') && /^[1-9]$/.test(e.key) && m.buttons && m.buttons[+e.key - 1]) { e.preventDefault(); emit('pick', m.buttons[+e.key - 1].id); return; }
  if (e.key === 'Enter' && !e.shiftKey) {
    if (m.kind === 'list') { e.preventDefault(); const r = list[Math.max(0, st.selected ?? 0)]; if (r) emit('pick', r.id); return; }
    if (m.kind === 'input') {
      e.preventDefault();
      const s = (m.suggestions || [])[st.sugg ?? 0];
      if (s && st.moved) emit('pick', s.id); else emit('submit', st.value ?? m.field.value ?? '');
      return;
    }
    if (m.kind === 'ask' || m.kind === 'choose') {
      e.preventDefault();
      if (list.length) { const r = list[Math.max(0, st.selected ?? 0)]; if (r) emit('pick', r.id); return; }
      const b = (m.buttons || []).find((x) => x.primary) || (m.buttons || [])[0];
      if (b) emit('pick', b.id);
    }
  }
});

function show(msg) {
  document.documentElement.dataset.theme = msg.dark ? 'dark' : 'light';
  document.documentElement.classList.toggle('still', !!msg.still);
  cur = { seq: msg.seq, model: msg.model, st: {}, margin: msg.margin || { l: 24, t: 20 }, place: null };
  render(true);
  const card = stage.firstElementChild;
  card.classList.remove('pop');
  card.classList.add('pre');   // measured while the window is still hidden: invisible until it is placed
  const r = card.getBoundingClientRect();
  post({ t: 'measured', seq: msg.seq, w: Math.ceil(r.width), h: Math.ceil(r.height) });
}

if (web) {
  web.addEventListener('message', (e) => {
    const msg = e.data;
    if (!msg || typeof msg !== 'object') return;
    if (msg.t === 'show') show(msg);
    else if (msg.t === 'update' && cur && msg.seq === cur.seq) {
      cur.model = msg.model;
      if (msg.keep !== true) { cur.st.dirty = false; }
      render();
    } else if (msg.t === 'place' && cur && msg.seq === cur.seq) {
      cur.place = msg;
      render(true);
      requestAnimationFrame(() => requestAnimationFrame(() => post({ t: 'shown', seq: cur.seq })));
    } else if (msg.t === 'hide') {
      const card = stage.firstElementChild;
      const gone = cur && cur.seq;
      if (card) card.classList.add('out');
      // A new show may come before the fade ends: only what was hidden is cleared.
      setTimeout(() => { if (cur && cur.seq === gone) { stage.replaceChildren(); cur = null; } post({ t: 'hidden' }); }, 150);
    } else if (msg.t === 'theme') {
      document.documentElement.dataset.theme = msg.dark ? 'dark' : 'light';
    }
  });
  post({ t: 'ready' });
}

window.__state = () => (cur ? { seq: cur.seq, kind: cur.model.kind, st: cur.st, rows: stage.querySelectorAll('[role=option]').length,
  height: stage.firstElementChild ? stage.firstElementChild.getBoundingClientRect().height : 0, text: stage.innerText } : null);
