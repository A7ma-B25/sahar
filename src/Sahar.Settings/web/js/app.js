// The page shell (plan 0.10c Task 4): settings from the host, light/dark, Arabic/English, the sidebar, the router,
// the unreadable-settings note (owner ruling 2026-11-13), and window.sahar for the test pipe.
import { request, onPush, tell } from './bridge.js';
import { t, setLang, getLang } from './i18n.js';
import { toYou } from './address.js';
import { visiblePages } from './pages/registry.js';
import { h, ghost, pri } from './controls.js';
import { icon, brandMark, fileMark } from './icons.js';

const S = { value: {}, caps: { pages: [] }, pairs: [], page: 'sahar', reset: 0, loaded: false };
const root = document.documentElement;
const darkQuery = matchMedia('(prefers-color-scheme: dark)');
const motionQuery = matchMedia('(prefers-reduced-motion: reduce)');
const modules = {};
let current = null;   // the page module on screen (it may have cleanup)

const lower = (v) => String(v ?? '').toLowerCase();
export const reduced = () => motionQuery.matches;

/** Her words to you: the string for key, in the page's language; Arabic agrees with your gender. */
export function tx(key, vars) {
  const v = t(key, vars);
  if (getLang() !== 'ar') return v;
  const g = lower(S.value.userGender);
  return Array.isArray(v) ? v.map((x) => toYou(x, g, S.pairs)) : toYou(v, g, S.pairs);
}

function applyLook() {
  const look = lower(S.value.appearance);
  const dark = look === 'dark' || (look !== 'light' && darkQuery.matches);
  root.dataset.theme = dark ? 'dark' : 'light';
  tell('theme', { dark });
  const en = lower(S.value.language) === 'english';
  setLang(en ? 'en' : 'ar');
  root.lang = en ? 'en' : 'ar';
  root.dir = en ? 'ltr' : 'rtl';
  document.title = t('windowTitle');
}

/** Saves one setting; the reply carries the settings as saved. */
export async function set(key, value) {
  const r = await request('set', { key, value });
  if (r.ok && r.value) S.value = r.value;
  return r;
}

/** Any other bridge request; settings in the reply are kept. */
export async function ask(type, body) {
  const r = await request(type, body);
  if (r.ok && r.value) S.value = r.value;
  return r;
}

async function load() {
  const r = await request('get');
  if (r.type === 'settings') {
    S.value = r.value || {};
    S.caps = r.caps || S.caps;
    S.pairs = r.pairs || [];
    S.loaded = true;
    if (!S.caps.broken && S.reset !== 2) S.reset = 0;
  }
}

function nav() {
  const pages = visiblePages(S.caps);
  const groups = [1, 2, 3, 4].map((g) => pages.filter((p) => p.group === g)).filter((g) => g.length);
  const about = pages.find((p) => p.group === 5);
  const item = (p) => h('button', {
    class: 'nav-item hv', type: 'button', id: `nav-${p.id}`, 'data-page': p.id, 'aria-current': S.page === p.id ? 'page' : null,
    onclick: () => go(p.id),
  }, icon(document, p.icon), h('span', { text: t(p.title) }));
  return h('nav', { class: 'nav', 'aria-label': t('navLabel') },
    h('div', { class: 'brand' }, brandMark(document), h('span', { class: 'brand-name', text: t('brand') })),
    ...groups.map((g) => h('div', { class: 'nav-group' }, ...g.map(item))),
    about ? h('button', {
      class: 'nav-about hv', type: 'button', id: 'nav-about', 'data-page': 'about', 'aria-current': S.page === 'about' ? 'page' : null,
      onclick: () => go('about'),
    }, h('span', {}, t(about.title) + ' · ', h('bdi', { class: 'tnum', text: S.caps.version || '' }))) : null);
}

/** «نبلّش من جديد»: a calm note above any page while settings.json can't be read. */
function banner() {
  if (!S.caps.broken && S.reset !== 2) return null;
  const step = S.reset;
  const titleKey = ['resetTitle', 'resetConfirmTitle', 'resetDoneTitle'][step];
  const textKey = ['resetText', 'resetConfirmText', 'resetDoneText'][step];
  const actions = step === 0
    ? ghost(tx('resetStart'), () => { S.reset = 1; render(true); }, { id: 'reset-start' })
    : step === 1
      ? h('span', { class: 'btn-row' },
        pri(tx('resetStart'), async () => {
          const r = await ask('reset');
          if (r.ok) { S.reset = 2; await load(); applyLook(); render(); }
        }, { id: 'reset-yes' }),
        ghost(tx('resetNo'), () => { S.reset = 0; render(true); }, { id: 'reset-no' }))
      : null;
  return h('section', { class: 'banner', role: 'status', 'aria-live': 'polite', id: 'reset-note' },
    h('span', { class: 'banner-icon', 'aria-hidden': 'true' }, fileMark(document)),
    h('div', { class: 'banner-text' },
      h('span', { class: 'label', text: tx(titleKey) }),
      h('span', { class: 'desc pr', text: tx(textKey) })),
    actions);
}

async function pageModule(id) {
  if (!modules[id]) modules[id] = await import(`./pages/${id}.js`);
  return modules[id];
}

let renderSeq = 0;
/** Draws the sidebar and the page. Keeps the focused control (by id) and the scroll; fades a new page in. */
export async function render(keepFocus = true, entering = false) {
  const seq = ++renderSeq;
  const focusId = keepFocus ? document.activeElement?.id : null;
  const mod = await pageModule(S.page);
  if (seq !== renderSeq) return;
  const scroller = document.querySelector('.main');
  const scroll = scroller && !entering ? scroller.scrollTop : 0;
  if (current?.leave) current.leave();
  current = mod;
  const page = h('div', { class: entering && !reduced() ? 'page enter' : 'page', id: 'page', 'data-page': S.page });
  const b = banner();
  if (b) page.append(b);
  page.append(...[].concat(await mod.render({ S, tx, set, ask, render, reduced })));
  const main = h('main', { class: 'main' }, page);
  document.body.replaceChildren(h('div', { class: 'app' }, nav(), main));
  main.scrollTop = scroll;
  if (focusId) document.getElementById(focusId)?.focus({ preventScroll: true });
}

export function go(id) {
  if (!visiblePages(S.caps).some((p) => p.id === id)) return false;
  if (id === S.page) return true;
  S.page = id;
  render(false, true);
  return true;
}

window.sahar = {
  nav: go,
  state: () => ({
    page: S.page,
    theme: root.dataset.theme,
    dir: root.dir,
    title: document.title,
    h1: document.querySelector('h1')?.textContent ?? '',
    focus: (() => {
      const a = document.activeElement;
      if (!a || a === document.body) return 'body';
      return a.id || `${a.tagName.toLowerCase()}:${(a.textContent || a.getAttribute('aria-label') || '').trim().slice(0, 40)}`;
    })(),
    broken: S.caps.broken ? 'true' : 'false',
    visible: document.visibilityState,
  }),
  settings: () => S.value,
  /** What the window does when it comes to the front: ask the host again (the engine may have saved meanwhile). */
  reload: async () => { await load(); applyLook(); await render(); },
  /** Light/dark and motion asked again (after the test pipe emulates them: no change event comes then). */
  relook: async () => { applyLook(); await render(); },
};

onPush(async (msg) => {
  if (msg?.type !== 'focus') return;
  // The window came to the front: the engine may have saved meanwhile (two writers).
  if (document.activeElement?.tagName === 'INPUT') { await load(); applyLook(); return; }   // never under your typing
  await window.sahar.reload();
});
darkQuery.addEventListener('change', () => { applyLook(); });
// عام changed light/dark or the language: the whole page follows (a pure mirror for English).
window.addEventListener('sahar-look', () => { applyLook(); render(); });
motionQuery.addEventListener('change', () => render());

await load();
applyLook();
await render(false);
tell('ready');
