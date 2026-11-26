// 0.15 T7 «ابعت سجلّي» and T8 «اسمح بتصويرها»: their words load beside the others, ar and en mirror each other.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { strings, t, setLang } from '../../src/Sahar.Settings/web/js/i18n.js';

test('diag-capture strings: load without a clash, both languages, the owner\'s words', async () => {
  for (const s of ['runtime', 'memory', 'autonomy']) await import(`../../src/Sahar.Settings/web/js/strings/mind-${s}.js`);
  await import('../../src/Sahar.Settings/web/js/strings/diag-capture.js');
  assert.deepEqual(Object.keys(strings.ar).sort(), Object.keys(strings.en).sort());
  assert.equal(strings.ar.sendLog, 'ابعت سجلّي');
  assert.equal(strings.ar.swCapture, 'اسمح بتصويرها');
  setLang('ar');
  assert.equal(t('sendLogDone', { name: 'sahar-log-2026-11-20-1405.zip' }), 'حطّيتلك سجلّي عالديسكتوب: sahar-log-2026-11-20-1405.zip');
  for (const k of ['sendLog', 'sendLogHint', 'sendLogDone', 'sendLogDoneFolder', 'sendLogFailed', 'swCapture', 'swCaptureHint'])
    assert.doesNotMatch(strings.ar[k] + strings.en[k], /\p{Extended_Pictographic}/u, `${k} has an emoji`);
});
