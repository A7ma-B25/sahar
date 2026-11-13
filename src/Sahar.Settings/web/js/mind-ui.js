// 0.14 SEAM: the parts the four Mind pages share (mockups «الإصدار 14 — الذكاء»): chips, cards, item rows, the
// moon-colour progress bar, stat tiles, and hand-drawn 24-unit icons (the psHand wobble). Text via textContent only.
import { h } from './controls.js';

const NS = 'http://www.w3.org/2000/svg';
let handId = 0;

/** A hand-drawn stroke icon: 24-unit path data (one or more paths), drawn through the psHand wobble. */
export function hand(paths, size = 18) {
  const svg = document.createElementNS(NS, 'svg');
  for (const [k, v] of Object.entries({ 'aria-hidden': 'true', width: size, height: size, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor',
    'stroke-width': '1.7', 'stroke-linecap': 'round', 'stroke-linejoin': 'round' })) svg.setAttribute(k, String(v));
  const id = `mHand${++handId}`;
  const filter = document.createElementNS(NS, 'filter');
  filter.setAttribute('id', id);
  const turb = document.createElementNS(NS, 'feTurbulence');
  for (const [k, v] of Object.entries({ type: 'fractalNoise', baseFrequency: '0.09', numOctaves: '1', seed: '4', result: 'n' })) turb.setAttribute(k, v);
  const disp = document.createElementNS(NS, 'feDisplacementMap');
  for (const [k, v] of Object.entries({ in: 'SourceGraphic', in2: 'n', scale: '1.1', xChannelSelector: 'R', yChannelSelector: 'G' })) disp.setAttribute(k, v);
  filter.append(turb, disp);
  const defs = document.createElementNS(NS, 'defs');
  defs.append(filter);
  const g = document.createElementNS(NS, 'g');
  g.setAttribute('filter', `url(#${id})`);
  for (const d of [].concat(paths)) {
    const p = document.createElementNS(NS, 'path');
    p.setAttribute('d', d);
    g.append(p);
  }
  svg.append(defs, g);
  return svg;
}

/** Icons from the mockups (24-unit). */
export const HAND = {
  timer: 'M12 9.5v3.8l2.4 1.6M10 3h4M12 6a7.5 7.5 0 1 0 0 15 7.5 7.5 0 1 0 0-15',
  note: 'M6 3.5h8.5l3.5 3.5v13.5H6zM9 11.5h6M9 15h4',
  mic: 'M9 6.5a3 3 0 0 1 6 0v5a3 3 0 0 1-6 0zM6 11.5a6 6 0 0 0 12 0',
  shield: 'M12 3.5l7 3v5.5c0 4.4-3 7.6-7 8.5-4-.9-7-4.1-7-8.5V6.5z',
  terminal: 'M4 5.5h16v13H4zM7.5 10l2.5 2-2.5 2M12 14.5h4',
  plus: 'M4 12a8 8 0 1 0 16 0 8 8 0 1 0-16 0M12 8v8M8 12h8',
  bulb: 'M12 4c-3.6 0-6.5 2.6-6.5 6 0 2 1 3.6 2.5 4.6V18h8v-3.4c1.5-1 2.5-2.6 2.5-4.6 0-3.4-2.9-6-6.5-6zM10 21h4',
  download: 'M12 4v11M7.5 10.5L12 15l4.5-4.5M5 19.5h14',
  blocked: 'M4 12a8 8 0 1 0 16 0 8 8 0 1 0-16 0M8 8l8 8',
  pencil: 'M5 19l1-4L16 5l3 3L9 18zM14 7l3 3',
  trash: 'M6 7h12M9.5 7V5h5v2M7.5 7l.8 12h7.4l.8-12',
  up: 'M7 11v8H4.5v-8zM7 11l3.5-6.5c1.4 0 2 1 1.7 2.4L11.6 10H18c1.2 0 2 1 1.7 2.2l-1.4 5.6c-.3 1-1 1.2-1.8 1.2H7',
  down: 'M7 13V5H4.5v8zM7 13l3.5 6.5c1.4 0 2-1 1.7-2.4L11.6 14H18c1.2 0 2-1 1.7-2.2l-1.4-5.6c-.3-1-1-1.2-1.8-1.2H7',
};

export const chip = (text, kind) => h('span', { class: kind ? `m-chip ${kind}` : 'm-chip', text });
export const chips = (...list) => h('span', { class: 'm-chips' }, ...list);
export const card = (...children) => h('section', { class: 'm-card' }, ...children);
export const iconBox = (paths) => h('span', { class: 'm-icon' }, hand(paths));
export const meta = (text, props = {}) => h('span', { class: 'm-meta', text, ...props });

/** A row: an optional icon box, a text column (main line + children), then trailing controls. */
export function item({ icon, line, under = [], end = [] }) {
  return h('div', { class: 'm-item' }, icon ? iconBox(icon) : null,
    h('span', { class: 'm-text' }, typeof line === 'string' ? h('span', { class: 'label', text: line }) : line, ...[].concat(under)),
    ...[].concat(end));
}

/** The moon-colour progress bar (0–100), a real progressbar for screen readers. */
export function bar(percent, label) {
  const fill = h('span', {});
  const el = h('div', { class: 'm-bar', role: 'progressbar', 'aria-valuemin': '0', 'aria-valuemax': '100', 'aria-label': label }, fill);
  el.set = (p) => { const v = Math.max(0, Math.min(100, Math.round(p))); fill.style.inlineSize = `${v}%`; el.setAttribute('aria-valuenow', String(v)); };
  el.set(percent);
  return el;
}

/** Stat tiles: [{ num, label }]. */
export const stats = (list, label) => h('div', { class: 'm-stats', role: 'group', 'aria-label': label },
  ...list.map((s) => h('div', { class: 'm-stat' }, h('span', { class: 'm-stat-num tnum', text: s.num }), h('span', { class: 'm-stat-label', text: s.label }))));
