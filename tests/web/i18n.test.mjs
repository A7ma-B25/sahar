import { test } from 'node:test';
import assert from 'node:assert/strict';
import { strings, t, setLang } from '../../src/Sahar.Settings/web/js/i18n.js';

test('i18n: every ar key has an en key and back, no empty strings', () => {
  const ar = Object.keys(strings.ar).sort();
  const en = Object.keys(strings.en).sort();
  assert.deepEqual(ar, en);
  for (const lang of ['ar', 'en'])
    for (const [k, v] of Object.entries(strings[lang])) {
      const ok = Array.isArray(v) ? v.length > 0 && v.every((x) => typeof x === 'string' && x.length > 0) : typeof v === 'string' && v.length > 0;
      assert.ok(ok, `${lang}.${k} is empty`);
    }
});

test('i18n: arrays have the same length in both languages', () => {
  for (const k of Object.keys(strings.ar))
    if (Array.isArray(strings.ar[k])) assert.equal(strings.ar[k].length, strings.en[k].length, k);
});

test('i18n: t() fills {vars} and follows the language', () => {
  setLang('ar');
  assert.equal(t('shortcutOk', { value: 'Ctrl+Alt+K' }), 'تمام، صار Ctrl+Alt+K.');
  setLang('en');
  assert.match(t('shortcutOk', { value: 'Ctrl+Alt+K' }), /Ctrl\+Alt\+K/);
  setLang('ar');
});

test('i18n: the owner rulings (2026-11-13) — «عن سحر», «نبلّش من جديد», «أكيد؟»', () => {
  assert.equal(strings.ar.aboutTitle, 'عن سحر');
  assert.equal(strings.ar.resetStart, 'نبلّش من جديد');
  assert.equal(strings.ar.resetConfirmTitle, 'أكيد؟');
  assert.equal(strings.ar.resetNo, 'لا');
});
