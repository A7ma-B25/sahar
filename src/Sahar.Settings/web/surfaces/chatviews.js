// The chat window as pure views (model → tree): thread, conversation list, empty state.
import { h } from './h.js';
import { icon } from './icons.js';

function header(m, emit) {
  return h('header', { 'data-drag': '1' },
    h('span', { cls: 'face' }, h('img', { src: '../sahar/sahar.svg', alt: '' })),
    h('span', { cls: 'who' }, h('b', null, m.name), h('span', { cls: m.typing ? 'ell typing' : 'ell' }, m.typing ? m.labels.typing : m.status)),
    h('button', { cls: 'ib hv', 'aria-label': m.labels.all, title: m.labels.all, 'data-id': 'all', on: { click: () => emit('all') } }, icon('menu', 20)),
    h('button', { cls: 'ib hv', 'aria-label': m.labels.new, title: m.labels.new, 'data-id': 'new', on: { click: () => emit('new') } }, icon('compose', 20)),
    h('button', { cls: 'ib hv', 'aria-label': m.labels.close, title: m.labels.close, 'data-id': 'close', on: { click: () => emit('close') } }, icon('x', 20)));
}

function message(x, first, emit) {
  if (x.kind === 'draft') {
    return h('div', { cls: 'draft', 'aria-label': x.draft.label, 'data-msg': x.id },
      h('span', { cls: 'lab' }, icon('pen', 14), x.draft.label),
      h('p', null, x.text),
      h('div', { cls: 'row2' },
        h('button', { cls: 'sec', 'data-id': 'copy-' + x.id, on: { click: () => emit('copy', x.id) } }, icon(x.draft.copied ? 'check' : 'copy', 15), x.draft.copied ? x.draft.copiedLabel : x.draft.copy),
        h('span', null, x.draft.note)));
  }
  const cls = `msg ${x.who === 'me' ? 'me' : 'her'}${first ? ' first' : ''}`;
  return h('div', { cls, 'data-msg': x.id, 'data-who': x.who },
    x.kind === 'ask' ? h('p', null, x.text) : x.text,
    x.receipt ? h('span', { cls: 'receipt' }, h('span', { style: 'display:flex' }, icon(x.receipt.icon || 'clock', 15)), x.receipt.text) : null,
    x.kind === 'ask' && x.ask && !x.ask.answered ? h('div', { cls: 'askbtns' },
      h('button', { cls: 'pri btn small', 'data-id': 'yes-' + x.id, on: { click: () => emit('ask', x.id + ':yes') } }, x.ask.yes),
      h('button', { cls: 'sec btn small', 'data-id': 'no-' + x.id, on: { click: () => emit('ask', x.id + ':no') } }, x.ask.no)) : null);
}

export function threadView(m, st, emit) {
  const inner = [];
  for (const day of m.days || []) {
    inner.push(h('div', { cls: 'day' }, day.label));
    for (const g of day.groups) {
      g.msgs.forEach((x, i) => inner.push(message({ ...x, who: g.who }, i === 0, emit)));
      inner.push(h('div', { cls: `time num ${g.who === 'me' ? 'me' : 'her'}` }, g.who === 'me' ? icon('check', 14) : null, g.time));
    }
  }
  if (m.partial) inner.push(h('div', { cls: 'msg her first', 'data-msg': 'partial' }, m.partial));
  else if (m.typing) inner.push(h('div', { cls: 'typingdots', 'aria-label': m.labels.typing }, h('span', { cls: 'dot' }), h('span', { cls: 'dot' }), h('span', { cls: 'dot' })));
  return h('div', { cls: 'thread scr', 'data-id': 'thread' }, h('div', { cls: 'inner' }, inner),
    st.newBelow ? h('button', { cls: 'newchip', 'data-id': 'newchip', on: { click: () => emit('bottom') } }, icon('arrowDown', 14), m.labels.newBelow) : null);
}

export function emptyView(m, st, emit) {
  return h('div', { cls: 'chatempty' }, h('img', { src: '../sahar/sahar.svg', alt: '' }),
    h('h3', null, m.empty.title), h('p', null, m.empty.hint),
    h('div', { cls: 'starters' }, m.empty.starters.map((s) => h('button', { cls: 'sec', 'data-id': 'starter', on: { click: () => emit('send', s.text) } },
      h('span', null, icon(s.icon, 16)), s.text))));
}

export function composer(m, st, emit) {
  const v = st.draft || '';
  return h('footer', null, h('div', { cls: 'composer' },
    h('textarea', { rows: 1, 'aria-label': m.labels.composer, placeholder: m.labels.placeholder, maxlength: m.max || 500, 'data-id': 'composer', value: v, spellcheck: 'false' }),
    h('button', { cls: `ib send ${v.trim() ? 'on' : ''}`, 'aria-label': m.labels.send, 'data-id': 'send', disabled: v.trim() ? null : true,
      on: { click: () => emit('send', v) } }, icon('send', 20))));
}

export function listView(m, st, emit) {
  const l = m.list;
  return [h('header', { 'data-drag': '1', style: 'padding:0 16px 0 10px' }, h('h2', null, l.title),
    h('button', { cls: 'ib hv', 'aria-label': m.labels.back, title: m.labels.back, 'data-id': 'back', on: { click: () => emit('all') } }, icon('x', 20))),
  h('div', { style: 'padding:10px 10px 0' }, h('button', { cls: 'sec', 'data-id': 'new', style: 'width:100%;height:42px;border-radius:12px;display:flex;align-items:center;justify-content:center;gap:8px;font-size:15px;font-weight:500;background:var(--soft);color:var(--softInk)',
    on: { click: () => emit('new') } }, icon('compose', 18), m.labels.new)),
  h('div', { cls: 'chats scr', role: 'list' }, (l.sections || []).map((s) => [h('p', { cls: 'grp' }, s.title), s.rows.map((r) => h('button', {
    cls: `crow ${r.sel ? 'sel' : 'hv'}`, role: 'listitem', 'data-id': 'open-' + r.id, on: { click: () => emit('open', r.id), contextmenu: (e) => { e.preventDefault(); emit('menu', r.id); } } },
    st.rename === r.id
      ? h('input', { 'data-id': 'rename', value: r.title, maxlength: 60, style: 'height:36px;font-size:15px;width:100%' })
      : [h('span', { cls: 'l1' }, h('b', { cls: 'ell' }, r.title), h('span', { cls: 'num' }, r.time)), h('span', { cls: 'l2 ell' }, r.last)],
    st.menu === r.id ? h('span', { cls: 'ctx', role: 'menu' },
      st.confirmDelete === r.id
        ? [h('span', { style: 'padding:6px 10px;font-size:13px;color:var(--t2)' }, l.sure),
          h('button', { cls: 'hv', role: 'menuitem', 'data-id': 'delete-yes', on: { click: (e) => { e.stopPropagation(); emit('delete', r.id); } } }, l.deleteYes),
          h('button', { cls: 'hv', role: 'menuitem', 'data-id': 'delete-no', on: { click: (e) => { e.stopPropagation(); emit('menu-close'); } } }, l.no)]
        : [h('button', { cls: 'hv', role: 'menuitem', 'data-id': 'rename-' + r.id, on: { click: (e) => { e.stopPropagation(); emit('rename-start', r.id); } } }, l.rename),
          h('button', { cls: 'hv', role: 'menuitem', 'data-id': 'delete-' + r.id, on: { click: (e) => { e.stopPropagation(); emit('delete-ask', r.id); } } }, l.del)]) : null))])),
  h('div', { cls: `chatfoot ${l.unsaved ? 'unsaved' : ''}` }, icon('lock', 14), l.footer)];
}

export function chatView(m, st, emit) {
  if (m.view === 'list') return h('div', { cls: 'card chat', role: 'dialog', 'aria-label': m.labels.dialog }, listView(m, st, emit));
  const body = m.view === 'empty' ? emptyView(m, st, emit) : threadView(m, st, emit);
  return h('div', { cls: 'card chat', role: 'dialog', 'aria-label': m.labels.dialog }, header(m, emit), body, composer(m, st, emit));
}

/** Review C1: where the thread goes after a render, decided before it is built (so «جديد» is in the same render and
 * nothing renders twice). He at the bottom, or a conversation opening (st.jump): the latest line. Scrolled up and a
 * line arrived: his place stays and «جديد» shows. Changes st.newBelow / st.jump; returns the new count. */
export function afterRender(st, { atBottom, n, lastCount }) {
  if (atBottom || st.jump) { st.newBelow = false; st.jump = false; return { toBottom: true, lastCount: n, again: false }; }
  if (n > lastCount) st.newBelow = true;
  return { toBottom: false, lastCount: n, again: false };
}
