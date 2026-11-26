// The chat window page (V15Chat): the engine sends the whole model on every change; the page keeps only what he is
// typing, the scroll position and the list's little menus. Nothing here is logged or sent anywhere but the host.
import { mount } from './h.js';
import { chatView } from './chatviews.js';

const web = window.chrome && window.chrome.webview;
const post = (m) => web && web.postMessage(m);
const stage = document.getElementById('stage');
let model = null;
let margin = { l: 24, t: 20 };
const st = { draft: '', newBelow: false };
let composing = false;
let lastCount = 0;

function emit(kind, value) {
  if (kind === 'menu') { st.menu = value; st.confirmDelete = null; render(); return; }
  if (kind === 'menu-close') { st.menu = null; st.confirmDelete = null; render(); return; }
  if (kind === 'delete-ask') { st.confirmDelete = value; render(); return; }
  if (kind === 'rename-start') { st.rename = value; st.menu = null; render(); const f = stage.querySelector('[data-id=rename]'); if (f) { f.focus(); f.select(); } return; }
  if (kind === 'bottom') { const t = thread(); if (t) t.scrollTop = t.scrollHeight; st.newBelow = false; render(); return; }
  if (kind === 'send') {
    const text = String(value || '').trim();
    if (!text) return;
    st.draft = '';
    post({ t: 'event', kind: 'send', value: text.slice(0, model.max || 500) });
    render();
    const t = thread();
    if (t) t.scrollTop = t.scrollHeight;
    return;
  }
  if (kind === 'delete') { st.menu = null; st.confirmDelete = null; }
  post({ t: 'event', kind, value: value == null ? null : String(value) });
}

const thread = () => stage.querySelector('[data-id=thread]');
const count = (m) => (m.days || []).reduce((n, d) => n + d.groups.reduce((k, g) => k + g.msgs.length, 0), 0) + (m.partial ? 1 : 0) + (m.typing ? 1 : 0);

function render() {
  if (!model) return;
  if (composing) { st.pending = true; return; }   // never under an IME composition
  const t0 = thread();
  const atBottom = !t0 || t0.scrollHeight - t0.scrollTop - t0.clientHeight < 24;
  const top = t0 ? t0.scrollTop : 0;
  const focused = document.activeElement && document.activeElement.dataset && document.activeElement.dataset.id;
  const old = stage.querySelector('[data-id=composer]');
  const caret = old ? [old.selectionStart, old.selectionEnd] : null;
  const el = mount(chatView(model, st, emit));
  el.style.left = margin.l + 'px';
  el.style.top = margin.t + 'px';
  stage.replaceChildren(el);
  const t = thread();
  const n = count(model);
  if (t) {
    if (atBottom || st.jump) { t.scrollTop = t.scrollHeight; st.newBelow = false; st.jump = false; }
    else { t.scrollTop = top; if (n > lastCount) { st.newBelow = true; render(); return; } }
  }
  lastCount = n;
  const box = stage.querySelector('[data-id=composer]');
  if (box) {
    grow(box);
    box.addEventListener('compositionstart', () => { composing = true; });
    box.addEventListener('compositionend', () => { composing = false; st.draft = box.value; sendState(box); if (st.pending) { st.pending = false; render(); } });
    box.addEventListener('input', () => { st.draft = box.value; grow(box); if (!composing) sendState(box); });
    box.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' && !e.shiftKey && !e.isComposing && !composing) { e.preventDefault(); emit('send', box.value); }
    });
    if (focused === 'composer' || focused === undefined || focused === null) {
      box.focus();
      if (caret) box.setSelectionRange(caret[0], caret[1]);
    }
  }
  const rn = stage.querySelector('[data-id=rename]');
  if (rn) {
    rn.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') { e.preventDefault(); const id = st.rename; st.rename = null; post({ t: 'event', kind: 'rename', value: id + '\n' + rn.value }); }
      if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); st.rename = null; render(); }
    });
    rn.addEventListener('click', (e) => e.stopPropagation());
  }
  const head = stage.querySelector('[data-drag]');
  if (head) head.addEventListener('mousedown', (e) => { if (e.button === 0 && !e.target.closest('button')) post({ t: 'drag' }); });
}

// Only the send button's state changes while he types: a full render would move the caret under an IME.
function sendState(box) {
  const b = stage.querySelector('[data-id=send]');
  if (!b) return;
  const on = box.value.trim().length > 0;
  b.classList.toggle('on', on);
  if (on) b.removeAttribute('disabled'); else b.setAttribute('disabled', '');
}

function grow(box) {
  box.style.height = 'auto';
  box.style.height = Math.min(120, Math.max(40, box.scrollHeight)) + 'px';
}

document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') {
    e.preventDefault();
    if (st.menu || st.rename) { st.menu = null; st.rename = null; st.confirmDelete = null; render(); return; }
    emit('close');
  }
});
document.addEventListener('mousedown', (e) => { if (st.menu && !e.target.closest('.ctx')) { st.menu = null; st.confirmDelete = null; render(); } });
stage.addEventListener('scroll', () => {}, true);
document.addEventListener('scroll', (e) => {
  const t = thread();
  if (t && e.target === t && st.newBelow && t.scrollHeight - t.scrollTop - t.clientHeight < 24) { st.newBelow = false; render(); }
}, true);

if (web) {
  web.addEventListener('message', (e) => {
    const msg = e.data;
    if (!msg || typeof msg !== 'object') return;
    if (msg.t === 'show' || msg.t === 'update') {
      if (msg.dark !== undefined) document.documentElement.dataset.theme = msg.dark ? 'dark' : 'light';
      if (msg.still !== undefined) document.documentElement.classList.toggle('still', !!msg.still);
      if (msg.margin) margin = msg.margin;
      const opening = msg.t === 'show';
      if (opening || (model && model.conv !== msg.model.conv)) st.jump = true;   // a conversation opens at its latest line
      model = msg.model;
      render();
      if (opening) {
        const card = stage.firstElementChild;
        card.classList.add('pre');
        const r = card.getBoundingClientRect();
        post({ t: 'measured', seq: msg.seq, w: Math.ceil(r.width), h: Math.ceil(r.height) });
      }
    } else if (msg.t === 'place') {
      const card = stage.firstElementChild;
      if (card) { card.classList.remove('pre'); card.classList.add('pop'); }
      requestAnimationFrame(() => requestAnimationFrame(() => post({ t: 'shown', seq: msg.seq })));
    } else if (msg.t === 'hide') {
      const card = stage.firstElementChild;
      if (card) card.classList.add('out');
      setTimeout(() => post({ t: 'hidden' }), 150);
    } else if (msg.t === 'theme') {
      document.documentElement.dataset.theme = msg.dark ? 'dark' : 'light';
    } else if (msg.t === 'focus') {
      const box = stage.querySelector('[data-id=composer]');
      if (box) box.focus();
    }
  });
  post({ t: 'ready' });
}

window.__state = () => {
  const t = thread();
  const her = stage.querySelector('.msg.her');
  const me = stage.querySelector('.msg.me');
  return model ? {
    view: model.view, msgs: stage.querySelectorAll('.msg,.draft').length, atBottom: t ? t.scrollHeight - t.scrollTop - t.clientHeight < 24 : true,
    herX: her ? her.getBoundingClientRect().x : null, meX: me ? me.getBoundingClientRect().x : null, newBelow: !!st.newBelow,
    text: stage.innerText,
  } : null;
};
