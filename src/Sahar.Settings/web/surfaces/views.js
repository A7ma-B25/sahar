// The panel surfaces as pure views (model + state → tree). The engine builds every model (her words already turned by
// Address and in his digits); the page only filters, highlights and reports what he picked.
import { h } from './h.js';
import { icon } from './icons.js';
import { matches } from './fold.js';

const head = (m, emit) => h('div', { cls: 'head' },
  h('span', { cls: 'sp1', 'aria-hidden': 'true' }), h('span', { cls: 'sp2', 'aria-hidden': 'true' }),
  h('h3', null,
    m.back ? h('button', { cls: 'back hv', 'aria-label': m.backLabel || 'رجوع', 'data-id': 'back', on: { click: () => emit('back') } }, icon('chevStart', 16)) : null,
    m.title),
  m.sub ? h('div', { cls: 'sub' },
    m.sub.action
      ? h('button', { cls: 'chip hv', 'data-id': m.sub.action, on: { click: () => emit('action', m.sub.action) } },
        m.sub.icon ? icon(m.sub.icon, 14) : null, m.sub.text, icon('chevEnd', 14))
      : [m.sub.icon ? h('span', { style: 'display:flex;color:var(--softInk)' }, icon(m.sub.icon, 14)) : null, m.sub.text]) : null);

/** The rows a list shows for a query: pinned rows (the resume row) only while the field is empty. */
export function visibleRows(m, query) {
  return (m.rows || []).filter((r) => (query ? !r.pinned && matches(query, r.label, r.num) : true));
}

function row(r, i, selected, m, emit, query) {
  const sel = i === selected;
  const cls = ['row', sel || r.sel ? 'sel' : 'hv', r.dim ? 'dim' : ''].filter(Boolean).join(' ');
  const meta = sel && query && m.enterHint ? m.enterHint : r.meta;
  return h('button', { cls, role: 'option', 'aria-selected': sel ? 'true' : 'false', 'data-id': r.id, 'data-i': i,
    on: { click: () => emit('pick', r.id) } },
    r.play ? h('span', { cls: 'badge' }, icon('play', 14))
      : r.numText ? h('span', { cls: 'badge num' }, r.numText)
        : r.icon ? h('span', { cls: 'ic' }, icon(r.icon, 18)) : null,
    h('span', { cls: 'lbl ell', style: r.strong ? 'font-weight:500' : null }, r.label),
    meta ? h('span', { cls: 'meta num' }, meta) : null,
    r.check ? h('span', { cls: 'check' }, icon('check', 16)) : null);
}

export function listView(m, st, emit) {
  const rows = visibleRows(m, st.query);
  const selected = Math.min(st.selected ?? (rows.findIndex((r) => r.sel) >= 0 ? rows.findIndex((r) => r.sel) : 0), rows.length - 1);
  const listH = m.listHeight || 302;
  let body;
  if ((m.rows || []).length === 0 && m.empty) body = emptyBox(m.empty, listH);
  else if (rows.length === 0 && m.none) body = emptyBox(m.none, listH);
  else body = h('div', { cls: 'list scr', role: 'listbox', style: `height:${listH}px` }, rows.map((r, i) => row(r, i, st.query || st.moved ? selected : -1, m, emit, st.query)));
  return h('div', { cls: 'card panel pop', role: 'dialog', 'aria-label': m.title, 'data-kind': m.kind },
    head(m, emit),
    m.search ? h('label', { cls: 'field' }, h('span', { cls: 'ic' }, icon('search', 18)),
      h('input', { 'aria-label': m.search.placeholder, placeholder: m.search.placeholder, autocomplete: 'off', spellcheck: 'false', value: st.query || '', 'data-id': 'search' })) : null,
    body,
    m.now ? h('div', { cls: 'now' }, h('span', { cls: 'ic' }, icon('bars', 18)),
      h('span', { cls: 'txt' }, h('b', { cls: 'ell' }, m.now.title), h('span', { cls: 'ell' }, m.now.sub)),
      h('button', { cls: 'hv', 'aria-label': m.now.pauseLabel, 'data-id': 'now-pause', on: { click: () => emit('action', 'now-pause') } }, icon(m.now.paused ? 'play' : 'pause', 16)),
      h('button', { cls: 'hv', 'aria-label': m.now.stopLabel, 'data-id': 'now-stop', on: { click: () => emit('action', 'now-stop') } }, icon('stop', 16))) : null,
    foot(m, st, emit));
}

function emptyBox(e, height) {
  return h('div', { cls: 'empty', style: `height:${height}px;flex:none` }, e.icon ? h('span', { cls: 'ic' }, icon(e.icon, 22)) : null,
    h('b', null, e.title), e.hint ? h('span', null, e.hint) : null);
}

function foot(m, st, emit) {
  const f = m.foot || {};
  const a = f.action;
  if (a && st.confirm) {
    return h('div', { cls: 'foot' }, h('span', { cls: 'q' }, a.confirm.q), h('span', { cls: 'grow' }),
      h('button', { cls: 'pri btn small', 'data-id': 'confirm-yes', on: { click: () => emit('action', a.id) } }, a.confirm.yes),
      h('button', { cls: 'sec btn small', 'data-id': 'confirm-no', on: { click: () => emit('confirm-no') } }, a.confirm.no));
  }
  return h('div', { cls: 'foot' },
    a ? h('button', { cls: 'act hv', 'data-id': a.id, on: { click: () => (a.confirm ? emit('confirm') : emit('action', a.id)) } }, a.icon ? icon(a.icon, 15) : null, a.label) : null,
    f.link ? h('button', { cls: 'act hv', 'data-id': f.link.id, on: { click: () => emit('action', f.link.id) } }, f.link.icon ? icon(f.link.icon, 15) : null, f.link.label, icon('chevEnd', 13)) : null,
    h('span', { cls: 'grow' }), f.hint || 'Esc لسكّر');
}

export function inputView(m, st, emit) {
  const f = m.field || {};
  const value = st.value ?? f.value ?? '';
  const error = st.dirty ? null : m.error;
  const input = f.multi
    ? h('textarea', { 'aria-label': m.title, placeholder: f.placeholder, maxlength: f.max || 500, 'data-id': 'value', value, spellcheck: 'false' })
    : h('input', { 'aria-label': m.title, placeholder: f.placeholder, maxlength: f.max || 120, 'data-id': 'value', value, autocomplete: 'off', spellcheck: 'false',
      inputmode: f.numeric ? 'numeric' : null });
  const sugg = m.suggestions || [];
  return h('div', { cls: 'card panel input pop', role: 'dialog', 'aria-label': m.title, 'data-kind': m.kind },
    head(m, emit),
    h('div', { cls: ['field', f.multi ? 'multi' : '', error ? 'err' : ''].filter(Boolean).join(' ') },
      f.icon ? h('span', { cls: 'ic' }, icon(f.icon, 18)) : null, input, f.suffix ? h('span', { cls: 'suffix' }, f.suffix) : null),
    error ? h('div', { cls: 'error', role: 'alert' }, error) : (m.helper ? h('div', { cls: 'helper' }, m.helper) : null),
    sugg.length ? h('div', { cls: 'list', role: 'listbox' }, sugg.map((s, i) => h('button', { cls: `sugg ${i === (st.sugg ?? 0) ? 'sel' : 'hv'}`, role: 'option', 'data-id': s.id,
      on: { click: () => emit('pick', s.id) } }, s.label))) : null,
    h('div', { cls: 'btns', style: 'padding:4px 4px 2px' },
      h('button', { cls: 'pri btn', 'data-id': 'submit', on: { click: () => emit('submit', st.value ?? value) } }, m.primary.icon ? icon(m.primary.icon, 16) : null, m.primary.label),
      m.secondary ? h('button', { cls: 'sec btn', 'data-id': 'secondary', on: { click: () => emit('secondary', st.value ?? value) } }, m.secondary.label) : null,
      m.secondary ? null : h('span', { cls: 'hint' }, m.hint || 'Esc لسكّر')),
    m.recent && m.recent.rows.length ? h('div', { cls: 'recent' }, h('p', null, m.recent.title),
      m.recent.rows.map((r) => h('div', { cls: 'r' }, h('span', { cls: 'ell' }, r.text), h('span', { cls: 'num' }, r.meta)))) : null);
}

export function askView(m, st, emit) {
  const opts = m.rows || [];
  return h('div', { cls: 'card bubble pop', role: 'dialog', 'aria-label': m.text || m.title, 'data-kind': m.kind },
    h('p', null, m.text || m.title),
    opts.length ? h('div', { cls: 'opts', role: 'listbox' }, opts.map((r, i) => h('button', { cls: `opt ${i === st.selected ? 'sel' : 'hv'}`, role: 'option', 'data-id': r.id,
      on: { click: () => emit('pick', r.id) } }, r.icon ? h('span', { style: 'display:flex;color:var(--t2)' }, icon(r.icon, 18)) : null, r.label))) : null,
    (m.buttons || []).length ? h('div', { cls: 'btns' }, m.buttons.map((b, i) => h('button', {
      cls: `btn small ${b.primary ? 'pri' : 'sec'}`, 'data-id': b.id, 'data-key': String(i + 1), on: { click: () => emit('pick', b.id) } },
      b.icon ? icon(b.icon, 15) : null, b.label))) : null,
    h('span', { cls: 'tail', 'aria-hidden': 'true' }));
}

export function trayView(m, st, emit) {
  return h('div', { cls: 'card panel tray pop', role: 'menu', 'aria-label': m.name, 'data-kind': 'tray' },
    h('div', { cls: 'trayhead' }, h('span', { cls: 'face' }, h('img', { src: '../sahar/sahar.svg', alt: '' })),
      h('span', null, h('b', null, m.name), h('span', null, m.status))),
    h('div', { cls: 'seg', role: 'radiogroup', 'aria-label': m.seg.label }, m.seg.options.map((o) => h('button', {
      cls: o.id === m.seg.value ? 'on' : 'hv', role: 'radio', 'aria-checked': o.id === m.seg.value ? 'true' : 'false', 'data-id': 'seg-' + o.id,
      on: { click: () => emit('seg', o.id) } }, o.label))),
    m.items.map((it) => h('button', { cls: 'item hv', role: it.toggle === undefined || it.toggle === null ? 'menuitem' : 'menuitemcheckbox',
      'aria-checked': it.toggle === undefined || it.toggle === null ? null : String(!!it.toggle), 'data-id': it.id, on: { click: () => emit('pick', it.id) } },
      h('span', { cls: 'ic' }, icon(it.icon, 20)), h('span', null, it.label),
      it.toggle === undefined || it.toggle === null
        ? (it.meta ? h('span', { cls: 'meta' }, it.meta) : null)
        : h('span', { cls: 'withmeta' }, it.meta ? h('span', { cls: 'meta', style: 'margin:0' }, it.meta) : null, h('span', { cls: `tog ${it.toggle ? 'on' : ''}` })))),
    h('div', { cls: 'sep' }),
    h('button', { cls: 'item hv', role: 'menuitem', 'data-id': m.quit.id, on: { click: () => emit('pick', m.quit.id) } }, h('span', { cls: 'ic' }, icon('power', 20)), h('span', null, m.quit.label)));
}

export function drawerView(m, st, emit) {
  const any = (m.sections || []).some((s) => s.rows.length);
  return h('div', { cls: 'card panel drawer pop', role: 'dialog', 'aria-label': m.title, 'data-kind': 'drawer' },
    head(m, emit),
    any ? h('div', { cls: 'list scr', style: `max-height:${m.listHeight || 420}px` },
      m.sections.filter((s) => s.rows.length).map((s) => [h('p', { cls: 'sect' }, s.title), s.rows.map((r) => h(r.id ? 'button' : 'div', {
        cls: `drow ${r.soft ? 'soft' : ''} ${r.id ? 'hv' : ''}`, 'data-id': r.id || null, on: r.id ? { click: () => emit('pick', r.id) } : null },
        h('span', { cls: 'ic' }, icon(r.icon || 'bell', 16)),
        h('span', { cls: 'txt' }, h('b', { cls: 'ell', style: 'font-weight:400' }, r.label, r.live ? h('span', { cls: 'live', 'aria-label': 'شغّال' }) : null), r.sub ? h('span', { cls: 'ell' }, r.sub) : null),
        r.meta ? h('span', { cls: 'meta num' }, r.meta) : null))]))
      : emptyBox(m.empty, 200),
    foot(m, st, emit));
}

export const VIEWS = { list: listView, input: inputView, ask: askView, choose: askView, tray: trayView, drawer: drawerView };
