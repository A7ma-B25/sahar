// A tiny view layer: views return plain trees (testable in node), mount() turns them into DOM (never innerHTML).
const SVG = 'http://www.w3.org/2000/svg';

export function h(tag, attrs, ...kids) {
  return { tag, attrs: attrs || {}, kids: kids.flat(Infinity).filter((k) => k !== null && k !== undefined && k !== false && k !== '') };
}

export function mount(v, doc = document, ns = null) {
  if (typeof v === 'string' || typeof v === 'number') return doc.createTextNode(String(v));
  const inSvg = ns || (v.tag === 'svg' ? SVG : null);
  const el = inSvg ? doc.createElementNS(SVG, v.tag) : doc.createElement(v.tag);
  for (const [k, val] of Object.entries(v.attrs)) {
    if (val === null || val === undefined || val === false) continue;
    if (k === 'on') { for (const [ev, fn] of Object.entries(val)) el.addEventListener(ev, fn); continue; }
    if (k === 'ref') { val(el); continue; }
    if (k === 'value') { el.value = val; continue; }
    el.setAttribute(k === 'cls' ? 'class' : k, val === true ? '' : String(val));
  }
  for (const kid of v.kids) el.appendChild(mount(kid, doc, inSvg));
  return el;
}

/** Every node of a tree (tests). */
export function* walk(v) {
  if (!v || typeof v !== 'object') return;
  yield v;
  for (const k of v.kids) yield* walk(k);
}

/** The text a tree shows (tests). */
export function text(v) {
  if (typeof v === 'string' || typeof v === 'number') return String(v);
  if (!v) return '';
  return v.kids.map(text).join('');
}

export const has = (v, cls) => String(v.attrs.cls || '').split(/\s+/).includes(cls);
export const find = (v, pred) => { for (const n of walk(v)) if (pred(n)) return n; return null; };
export const findAll = (v, pred) => [...walk(v)].filter(pred);
