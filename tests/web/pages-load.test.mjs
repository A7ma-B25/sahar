// 0.14.1: every settings page module loads (run 44752306846: a duplicate `const` in models.js stopped the whole app —
// «ready=false» on every page — while `node --check` saw nothing).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync } from 'node:fs';

globalThis.document ??= { createElement: () => ({}), visibilityState: 'visible', documentElement: { dataset: {} } };
globalThis.window ??= { matchMedia: () => ({ matches: false, addEventListener() {} }), sahar: {} };

test('settings pages: every module parses and loads', async () => {
  const dir = new URL('../../src/Sahar.Settings/web/js/pages/', import.meta.url);
  for (const f of readdirSync(dir).filter((n) => n.endsWith('.js'))) {
    try { await import(new URL(f, dir)); }
    catch (e) { if (e instanceof SyntaxError) assert.fail(`${f}: ${e.message}`); }   // a missing browser API at load is not this test's business
  }
});
