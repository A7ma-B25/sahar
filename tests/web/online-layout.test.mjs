// 0.17.1 «العقل أونلاين» (owner: «التصميم كتير متداخل»): the page's structure on a tiny DOM — blocks spaced as the
// other pages, each provider one header (order, name, key chip, up/down icon buttons with names) over a label/field
// grid, ids/URLs/keys left-to-right with a readable placeholder, «مخصّص» folded, «جرّب عقلي» only with a key — and the
// same wiring (ids the UI tests drive, the chain moves, the consent).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

class El {
  constructor(tag) { this.tagName = tag.toUpperCase(); this.attrs = {}; this.children = []; this.listeners = {}; this.hidden = false; this.className = ''; this.textContent = ''; this.value = ''; }
  setAttribute(k, v) { this.attrs[k] = String(v); if (k === 'id') this.id = String(v); if (k === 'value') this.value = String(v); }
  getAttribute(k) { return k === 'id' ? this.id ?? null : this.attrs[k] ?? null; }
  append(...c) { this.children.push(...c); }
  addEventListener(t, f) { (this.listeners[t] ||= []).push(f); }
  click() { for (const f of this.listeners.click || []) f(); }
  focus() {}
  get classList() {
    const self = this;
    const list = () => self.className.split(/\s+/).filter(Boolean);
    return {
      add: (c) => { if (!list().includes(c)) self.className = [...list(), c].join(' '); },
      contains: (c) => list().includes(c),
      toggle: (c, on) => { const has = list().includes(c); const want = on ?? !has; self.className = want ? [...new Set([...list(), c])].join(' ') : list().filter((x) => x !== c).join(' '); },
    };
  }
}
globalThis.document = { createElement: (t) => new El(t), createElementNS: (_, t) => new El(t), createTextNode: (t) => ({ text: t }), visibilityState: 'hidden', documentElement: { dataset: {} } };
globalThis.window ??= { matchMedia: () => ({ matches: false, addEventListener() {} }), sahar: {} };

const { strings } = await import('../../src/Sahar.Settings/web/js/i18n.js');
const page = await import('../../src/Sahar.Settings/web/js/pages/online.js');

const walk = function* (n) { if (!n || typeof n !== 'object' || !('children' in n)) return; yield n; for (const c of n.children) yield* walk(c); };
const all = (roots) => roots.flatMap((r) => [...walk(r)]);
const byId = (roots, id) => all(roots).find((e) => e.id === id);
const has = (e, c) => e.className.split(/\s+/).includes(c);
const text = (e) => [...walk(e)].map((x) => x.textContent).join('');
const tx = (k, vars) => { let s = strings.ar[k] ?? k; for (const [a, b] of Object.entries(vars || {})) s = s.replace(`{${a}}`, b); return s; };

async function draw(value, keys = []) {
  const sets = [];
  const S = { value: { ...value } };
  const ask = async (type) => (type === 'mind-online-status'
    ? { ok: true, seq: '1', info: { providers: ['groq', 'openrouter', 'custom'].map((id) => ({ id, key: keys.includes(id), used: 0, limit: id === 'groq' ? 1000 : 0 })) },
      models: { groq: ['openai/gpt-oss-120b', 'llama-3.3-70b-versatile'], openrouter: ['google/gemma-4-31b-it:free', 'openrouter/free'] } }
    : { ok: true });
  const set = async (k, v) => { sets.push([k, v]); S.value[k] = v; return { ok: true }; };
  const roots = await page.render({ S, tx, set, ask, render: async () => {} });
  page.leave();
  return { roots, sets };
}

test('online page: status, «محلي فقط» and the consent are spaced blocks, the consent under its own heading', async () => {
  const { roots } = await draw({});
  assert.equal(roots[0].tagName, 'H1');
  assert.ok(has(roots[2], 'm-card') && has(roots[2], 'on-status'), 'the status card');
  assert.ok(has(roots[3], 'group') && has(roots[3], 'on-local'), '«محلي فقط» is its own group, a step below');
  assert.equal(roots[4].tagName, 'H2');
  assert.equal(roots[4].textContent, strings.ar.onConsentTitle);
  assert.equal(roots[5].id, 'on-consent');
  assert.equal(roots[5].attrs['aria-labelledby'], roots[4].id);
  assert.ok(byId(roots, 'on-consent-ok'));
  assert.ok(!byId(roots, 'on-key-groq'), 'no key field before consent');
});

test('online page: «محلي فقط» hides everything below', async () => {
  const { roots } = await draw({ mindLocalOnly: true });
  assert.equal(roots.length, 4);
});

test('online page: a provider is one header — order, name, key chip, named up/down icon buttons — then its quota', async () => {
  const { roots, sets } = await draw({ mindOnlineConsent: true, mindOnlineKeys: ['groq'] }, ['groq']);
  const rows = all(roots).filter((e) => has(e, 'on-row'));
  assert.deepEqual(rows.map((r) => r.attrs['data-provider']), ['groq', 'openrouter', 'custom']);
  for (const [i, r] of rows.entries()) {
    const head = all([r]).find((e) => has(e, 'on-head'));
    const [n, title, move] = head.children;
    assert.ok(has(n, 'on-num') && n.textContent === '١٢٣'[i], 'the order badge leads the header');
    assert.ok(has(title, 'on-title'));
    assert.ok(all([title]).some((e) => e.id === `on-keystate-${r.attrs['data-provider']}` && has(e, 'm-chip')), 'the key state is a chip beside the name');
    assert.ok(has(move, 'on-move'));
    for (const b of move.children) {
      assert.equal(b.tagName, 'BUTTON');
      assert.ok(has(b, 'm-icon-btn'));
      assert.match(b.attrs['aria-label'], /^(لفوق|لتحت) /);
    }
    const quota = byId([r], `on-quota-${r.attrs['data-provider']}`);
    assert.ok(has(quota, 'on-quota'));
  }
  assert.equal(byId(roots, 'on-up-groq').hidden, true);
  assert.equal(byId(roots, 'on-down-custom').hidden, true);
  assert.ok(has(byId(roots, 'on-keystate-groq'), 'accent'), 'a saved key reads as an accent chip');
  assert.ok(!has(byId(roots, 'on-keystate-openrouter'), 'accent'));
  // the wiring is the same: «لتحت» on Groq swaps it with the next
  byId(roots, 'on-down-groq').click();
  await new Promise((r) => setTimeout(r, 0));
  assert.deepEqual(sets.at(-1), ['mindOnlineChain', ['openrouter', 'groq', 'custom']]);
});

test('online page: model ids, URLs and keys are left-to-right; the URL placeholder has no Arabic in it', async () => {
  const { roots } = await draw({ mindOnlineConsent: true, mindOnlineKeys: [] });
  for (const id of ['on-model-groq', 'on-model-openrouter', 'on-modeltext-custom', 'on-url-custom', 'on-key-groq', 'on-key-openrouter', 'on-key-custom']) {
    const e = byId(roots, id);
    assert.ok(e, id);
    assert.equal(e.attrs.dir, 'ltr', id);
    assert.ok(has(e, 'on-ltr'), id);
  }
  const url = byId(roots, 'on-url-custom');
  assert.doesNotMatch(url.attrs.placeholder, /[؀-ۿ]/);
  assert.equal(url.attrs['aria-describedby'], 'on-url-hint');
  assert.equal(byId(roots, 'on-url-hint').textContent, strings.ar.onUrlHint);
  // every LTR run inside the Arabic hint and error is isolated (no «v1/…» junk)
  for (const k of ['onUrlHint', 'onBadUrl']) assert.match(strings.ar[k], /⁦https:\/\/⁩/, k);
  // the fields have visible labels tied to them
  const labels = all(roots).filter((e) => e.tagName === 'LABEL' && has(e, 'on-flabel'));
  for (const id of ['on-model-groq', 'on-key-groq', 'on-url-custom']) assert.ok(labels.some((l) => l.attrs.for === id), id);
});

test('online page: «مخصّص» folds behind «مزوّد مخصّص (متقدّم)» until opened (or set up)', async () => {
  const { roots } = await draw({ mindOnlineConsent: true, mindOnlineKeys: [] });
  const toggle = byId(roots, 'on-custom-toggle');
  const body = byId(roots, 'on-custom-body');
  assert.equal(text(toggle), strings.ar.onCustomAdvanced);
  assert.equal(toggle.attrs['aria-expanded'], 'false');
  assert.equal(toggle.attrs['aria-controls'], 'on-custom-body');
  assert.equal(body.hidden, true);
  assert.ok(byId([body], 'on-url-custom'), 'the URL is inside the fold');
  toggle.click();
  assert.equal(body.hidden, false);
  assert.equal(toggle.attrs['aria-expanded'], 'true');
  toggle.click();   // folded again for the next tests (the page keeps his choice across a redraw)
  const set = await draw({ mindOnlineConsent: true, mindOnlineKeys: [], mindCustomUrl: 'https://api.example.test/v1' });
  assert.equal(byId(set.roots, 'on-custom-body').hidden, true, 'his last choice (folded) is kept across a redraw');
});

test('online page: «جرّب عقلي» — heading and card together, hidden until a key is saved', async () => {
  const none = await draw({ mindOnlineConsent: true, mindOnlineKeys: [] });
  const block = byId(none.roots, 'on-bench-block');
  assert.equal(block.hidden, true);
  assert.ok(block.children.some((c) => c.tagName === 'H2' && c.textContent === strings.ar.onGroupBench), 'the heading lives in the block');
  assert.ok(!none.roots.some((r) => r.tagName === 'H2' && r.textContent === strings.ar.onGroupBench), 'no loose heading');
  const one = await draw({ mindOnlineConsent: true, mindOnlineKeys: ['groq'] }, ['groq']);
  const b = byId(one.roots, 'on-bench-block');
  assert.equal(b.hidden, false);
  assert.ok(byId([b], 'on-bench') && byId([b], 'on-back-local') && text(b).includes(strings.ar.onBenchHint));
});

test('online page: its stylesheet is linked; LTR fields are LTR; the sidebar fits the window\'s own height', () => {
  const web = new URL('../../src/Sahar.Settings/web/', import.meta.url);
  const html = readFileSync(new URL('index.html', web), 'utf8');
  assert.match(html, /href="css\/mind-online\.css"/);
  const css = readFileSync(new URL('css/mind-online.css', web), 'utf8');
  assert.match(css, /\.on-ltr \{[^}]*direction: ltr/);
  assert.match(css, /\.on-fields \{[^}]*grid-template-columns: max-content minmax\(0, 1fr\)/);
  assert.doesNotMatch(css, /(?<![-\w])(left|right)\s*:/, 'logical properties only (text-align aside)');
  const app = readFileSync(new URL('css/app.css', web), 'utf8');
  assert.match(app, /@media \(max-height: 760px\)/);
});
