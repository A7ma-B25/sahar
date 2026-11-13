// The page's parts (plan 0.10c Task 4; study §5.6; the approved mockups): groups, rows, segmented, switches, fields,
// buttons, kbd. Text is always set with textContent (never innerHTML): her words, your name and the geocoder's.

/** An element: h('div', {class: 'row', role: 'group'}, child, 'text', …). Props starting with "on" are listeners. */
export function h(tag, props = {}, ...children) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(props || {})) {
    if (v === undefined || v === null || v === false) continue;
    if (k === 'class') el.className = v;
    else if (k === 'text') el.textContent = v;
    else if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2).toLowerCase(), v);
    else el.setAttribute(k, v === true ? '' : String(v));
  }
  for (const c of children.flat()) if (c !== null && c !== undefined && c !== false) el.append(typeof c === 'string' ? document.createTextNode(c) : c);
  return el;
}

let uid = 0;
export const newId = (p = 'c') => `${p}${++uid}`;

export const title = (text) => h('h1', { class: 'title', text });
export const sub = (text) => h('p', { class: 'sub', text });
export const section = (text, id) => h('h2', { class: 'section', text, id });
export const group = (...children) => h('section', { class: 'group' }, ...children);
export const note = (text) => h('p', { class: 'note', text });

/** A label (and an optional description) on the start side, a control on the end side. */
export function row(labelText, control, { desc, labelId, forId } = {}) {
  const label = forId
    ? h('label', { class: 'label', for: forId, id: labelId, text: labelText })
    : h('span', { class: 'label', id: labelId, text: labelText });
  const descEl = desc !== undefined ? h('span', { class: 'desc', text: desc }) : null;
  return h('div', { class: 'row' }, h('span', { class: 'label-col' }, label, descEl), control);
}

/** A segmented choice (role radiogroup): arrow keys move the choice (mirrored in RTL), Home/End jump. */
export function segmented(labelId, labels, selected, onPick, id) {
  const track = h('div', { class: 'track', role: 'radiogroup', 'aria-labelledby': labelId, id });
  const buttons = labels.map((label, i) => h('button', {
    class: 'seg', type: 'button', role: 'radio', 'aria-checked': i === selected ? 'true' : 'false', tabindex: i === selected ? '0' : '-1',
    'data-index': String(i), text: label, id: id ? `${id}-${i}` : null,
    onclick: () => choose(i, false),
  }));
  function choose(i, focus) {
    buttons.forEach((b, j) => {
      b.setAttribute('aria-checked', j === i ? 'true' : 'false');
      b.tabIndex = j === i ? 0 : -1;
    });
    if (focus) buttons[i].focus();
    onPick(i);
  }
  track.addEventListener('keydown', (e) => {
    const cur = buttons.findIndex((b) => b.getAttribute('aria-checked') === 'true');
    const rtl = getComputedStyle(track).direction === 'rtl';
    let next = -1;
    if (e.key === 'ArrowRight') next = cur + (rtl ? -1 : 1);
    else if (e.key === 'ArrowLeft') next = cur + (rtl ? 1 : -1);
    else if (e.key === 'ArrowDown') next = cur + 1;
    else if (e.key === 'ArrowUp') next = cur - 1;
    else if (e.key === 'Home') next = 0;
    else if (e.key === 'End') next = buttons.length - 1;
    else return;
    e.preventDefault();
    next = (next + buttons.length) % buttons.length;
    if (next !== cur) choose(next, true);
  });
  track.append(...buttons);
  track.select = (i) => buttons.forEach((b, j) => { b.setAttribute('aria-checked', j === i ? 'true' : 'false'); b.tabIndex = j === i ? 0 : -1; });
  return track;
}

/** A switch row: the whole row is the button (role switch). */
export function switchRow(labelText, hint, on, onFlip, key) {
  const btn = h('button', { class: 'switch-row hv', type: 'button', role: 'switch', 'aria-checked': on ? 'true' : 'false', 'data-key': key },
    h('span', { class: 'switch-text' }, h('span', { class: 'label', text: labelText }), hint ? h('span', { class: 'desc', text: hint }) : null),
    h('span', { class: 'switch', 'aria-hidden': 'true' }, h('span', { class: 'switch-thumb' })));
  btn.addEventListener('click', () => {
    const next = btn.getAttribute('aria-checked') !== 'true';
    btn.setAttribute('aria-checked', next ? 'true' : 'false');
    onFlip(next, btn);
  });
  btn.set = (v) => btn.setAttribute('aria-checked', v ? 'true' : 'false');
  return btn;
}

/** A radio row (the reciters): the whole row is the radio. */
export function radioRow(labelText, on, onPick, value) {
  return h('button', { class: 'switch-row hv', type: 'button', role: 'radio', 'aria-checked': on ? 'true' : 'false', 'data-value': value, onclick: onPick },
    h('span', { class: 'label', text: labelText }),
    h('span', { class: 'radio-ring', 'aria-hidden': 'true' }, h('span', { class: 'radio-dot' })));
}

export function input({ id, placeholder, value = '', label }) {
  return h('input', { class: 'input', id, type: 'text', placeholder, value, 'aria-label': label, autocomplete: 'off', spellcheck: 'false' });
}

export function select(id, options, value, onChange) {
  const el = h('select', { class: 'input', id, onchange: () => onChange(el.value) },
    ...options.map(([v, text]) => h('option', { value: v, text })));
  el.value = value;
  return el;
}

export const ghost = (text, onclick, props = {}) => h('button', { class: 'ghost hv', type: 'button', text, onclick, ...props });
export const pri = (text, onclick, props = {}) => h('button', { class: 'pri', type: 'button', text, onclick, ...props });
export const dng = (text, onclick, props = {}) => h('button', { class: 'dng', type: 'button', text, onclick, ...props });
export const kbd = (text) => h('kbd', { text, dir: 'ltr' });
