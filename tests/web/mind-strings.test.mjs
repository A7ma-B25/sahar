import { test } from 'node:test';
import assert from 'node:assert/strict';
import { strings } from '../../src/Sahar.Settings/web/js/i18n.js';

// 0.14: every Mind page's words load together without a key clash, both languages, no empties, arrays aligned.
test('mind strings: the three streams load side by side, ar and en mirror each other', async () => {
  for (const s of ['runtime', 'memory', 'autonomy']) await import(`../../src/Sahar.Settings/web/js/strings/mind-${s}.js`);
  assert.deepEqual(Object.keys(strings.ar).sort(), Object.keys(strings.en).sort());
  for (const k of Object.keys(strings.ar)) {
    const a = strings.ar[k], e = strings.en[k];
    assert.equal(Array.isArray(a), Array.isArray(e), k);
    if (Array.isArray(a)) assert.equal(a.length, e.length, k);
    for (const v of [].concat(a, e)) assert.ok(typeof v === 'string' && v.length > 0, `${k} is empty`);
  }
  assert.equal(strings.ar.dlBlockedTitle, 'ما قدرت وصل لـ Hugging Face');
  assert.match(strings.ar.dlBlockedNote, /VPN/);
});
