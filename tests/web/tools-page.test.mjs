// 0.19 «الأدوات»: the built-in picture and voice tools, his MCP servers with every tool's full description, args and
// state before anything is on, the 12-tool cap, and the add form — on a tiny DOM (the same ids the UI stage drives).
import { test } from 'node:test';
import assert from 'node:assert/strict';

class El {
  constructor(tag) { this.tagName = tag.toUpperCase(); this.attrs = {}; this.children = []; this.listeners = {}; this.hidden = false; this.className = ''; this.textContent = ''; this.value = ''; this.disabled = false; }
  setAttribute(k, v) { this.attrs[k] = String(v); if (k === 'id') this.id = String(v); if (k === 'value') this.value = String(v); }
  getAttribute(k) { return k === 'id' ? this.id ?? null : this.attrs[k] ?? null; }
  removeAttribute(k) { delete this.attrs[k]; }
  append(...c) { this.children.push(...c); }
  replaceChildren(...c) { this.children = [...c]; }
  addEventListener(t, f) { (this.listeners[t] ||= []).push(f); }
  click() { for (const f of this.listeners.click || []) f(); }
  dispatch(t) { for (const f of this.listeners[t] || []) f(); }
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
const page = await import('../../src/Sahar.Settings/web/js/pages/tools.js');

const walk = function* (n) { if (!n || typeof n !== 'object' || !('children' in n)) return; yield n; for (const c of n.children) yield* walk(c); };
const all = (roots) => roots.flatMap((r) => [...walk(r)]);
const byId = (roots, id) => all(roots).find((e) => e.id === id);
const text = (e) => [...walk(e)].map((x) => x.textContent ?? x.text ?? '').join('');
const tx = (k, vars) => { let s = strings.ar[k] ?? k; for (const [a, b] of Object.entries(vars || {})) s = s.replace(`{${a}}`, b); return s; };

const media = { imageChecked: false, voiceChecked: false, groqKey: false, imageGrant: 'Ask', voiceGrant: 'Ask' };
const tool = (name, over = {}) => ({ name, qualified: `mcp.files.${name}`, description: `${name} does things`, args: [{ name: 'path', type: 'String', required: true, maxLength: 200 }],
  enabled: false, grant: 'Ask', sensitive: false, autoSensitive: false, state: 'new', ...over });

async function draw(info, value = {}) {
  const asks = [];
  const S = { value: { ...value } };
  const ask = async (type, body) => { asks.push([type, body]); return type === 'mind-tools-status' ? { ok: true, seq: '1', engine: true, info } : { ok: true }; };
  const roots = await page.render({ S, tx, set: async () => ({ ok: true }), ask, render: async () => {} });
  page.leave();
  return { roots, asks };
}

test('tools page renders empty', async () => {
  const { roots } = await draw({ servers: [], announced: [], media });
  assert.equal(roots[0].tagName, 'H1');
  assert.equal(roots[0].textContent, strings.ar.pageTools);
  assert.ok(byId(roots, 'tl-empty'));
  assert.ok(byId(roots, 'tl-image'));
  assert.equal(byId(roots, 'tl-image-check').hidden, false);
  assert.equal(byId(roots, 'tl-voice-check').hidden, true, 'no Groq key: no «جرّب» for the voice (hidden, not greyed)');
  assert.match(text(byId(roots, 'tl-voice')), new RegExp(strings.ar.tlVoiceNeedsKey));
  assert.ok(byId(roots, 'tl-add-go'));
  assert.equal(byId(roots, 'tl-image-grant').hidden, true, 'the grant shows once the check passed');
  assert.ok(!all(roots).some((e) => /فيديو|video/i.test(e.textContent || '')), 'no video anywhere');
});

test('server card lists tools with full description', async () => {
  const long = 'Reads one file. '.repeat(30);
  const info = { servers: [{ id: 's1', name: 'ملفاتي', slug: 'files', kind: 'exe', target: 'C:\\t\\files.exe', args: [], state: 'ok', hasSecret: false, tools: [
    tool('read_file', { description: long, enabled: true, state: 'ok' }),
    tool('write_file', { enabled: true, state: 'changed' }),
    tool('sum', { args: null, state: 'unfit' }),
    tool('delete_file', { sensitive: true, autoSensitive: true }),
  ] }], announced: [], media };
  const { roots } = await draw(info);
  const card = byId(roots, 'tl-server-s1');
  assert.ok(card);
  assert.ok(text(card).includes(long.trim()), 'the whole description, not cut');
  assert.match(text(card), /path/);
  assert.match(text(card), new RegExp(strings.ar.tlChanged));
  assert.match(text(card), new RegExp(strings.ar.tlUnfit));
  assert.ok(byId(roots, 'tl-on-s1-0'));
  assert.ok(byId(roots, 'tl-approve-tool-s1-1'), 'a changed tool has «وافق» instead of its switch');
  assert.ok(!byId(roots, 'tl-on-s1-2'), 'an unfit tool cannot be turned on');
  assert.ok(!byId(roots, 'tl-grant-s1-3'), 'a sensitive tool has no «دايماً»');
  assert.equal(byId(roots, 'tl-server-target-s1').attrs.dir, 'ltr');
});

test('13th enable is refused in the page', async () => {
  const tools = Array.from({ length: 13 }, (_, i) => tool('t' + i, i < 12 ? { enabled: true, state: 'ok' } : {}));
  const { roots, asks } = await draw({ servers: [{ id: 's1', name: 'x', slug: 'x', kind: 'url', target: 'https://x.example/mcp', args: [], state: 'ok', hasSecret: false, tools }], announced: [], media });
  byId(roots, 'tl-on-s1-12').click();
  await new Promise((r) => setTimeout(r, 0));
  assert.ok(!asks.some(([t, b]) => t === 'mind-tools-op' && b.op === 'enable'), 'no enable was sent');
  assert.equal(byId(roots, 'tl-result-s1').textContent, strings.ar.tlLimit);
});

test('tools page: «محلي فقط» hides the picture and voice tools', async () => {
  const { roots } = await draw({ servers: [], announced: [], media: { ...media, imageChecked: true } }, { mindLocalOnly: true });
  assert.ok(!byId(roots, 'tl-image'));
  assert.ok(byId(roots, 'tl-local-note'));
});

test('tools page: adding a «برنامج» sends its fields once, the secret never stays in the page', async () => {
  const { roots, asks } = await draw({ servers: [], announced: [], media });
  byId(roots, 'tl-add-name').value = 'ملفاتي';
  byId(roots, 'tl-add-kind-1').click();
  byId(roots, 'tl-add-exe').value = 'C:\\t\\files.exe';
  byId(roots, 'tl-add-args').value = '--root "C:\\My Files"';
  byId(roots, 'tl-add-secret-kind').value = 'env';
  byId(roots, 'tl-add-secret-name').value = 'API_KEY';
  byId(roots, 'tl-add-secret').value = 'sekret';
  byId(roots, 'tl-add-go').click();
  await new Promise((r) => setTimeout(r, 0));
  const [, body] = asks.find(([t]) => t === 'mind-tools-add');
  assert.deepEqual(body, { name: 'ملفاتي', kind: 'exe', url: '', exe: 'C:\\t\\files.exe', args: '--root "C:\\My Files"', secretKind: 'env', secretName: 'API_KEY', secret: 'sekret' });
  assert.equal(byId(roots, 'tl-add-secret').value, '');
});
