// «شو عملت اليوم» (0.14 stream C): the meta line, the counts in her words, the icons, and the page's strings.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { strings, t, setLang } from '../../src/Sahar.Settings/web/js/i18n.js';
import { metaLine, countText, iconFor } from '../../src/Sahar.Settings/web/js/pages/today.js';
import { HAND } from '../../src/Sahar.Settings/web/js/mind-ui.js';

const e = (o) => ({ id: 'x', time: '10:12', day: '2026-11-13', tool: 'timer.start', summary: 's', source: 'user', outcome: 'done', undo: false, feedback: 0, ...o });

test('today: the meta lines are the mockup\'s', () => {
  setLang('ar');
  assert.equal(metaLine(t, e({ why: 'ركّزني شي ربع ساعة' })), '١٠:١٢ · إنت طلبت: «ركّزني شي ربع ساعة»');
  assert.equal(metaLine(t, e({ time: '9:40', source: 'self', tool: 'music.control', why: 'بلّشت مكالمة' })), '٩:٤٠ · لحالي: بلّشت مكالمة');
  assert.equal(metaLine(t, e({ time: '9:05', tool: 'note.save' })), '٩:٠٥ · إنت طلبت');
  assert.equal(metaLine(t, e({ time: '8:20', source: 'self', tool: 'settings.set', outcome: 'asked-no' })), '٨:٢٠ · قلت لا، فما غيّرت شي');
  assert.equal(metaLine(t, e({ time: '8:01', source: 'self', tool: 'claude.permission', outcome: 'refused' })), '٨:٠١ · خبّرتك بس. الجواب إلك من نافذة Claude.');
  assert.equal(metaLine(t, e({}), true), '٢٠٢٦-١١-١٣ ١٠:١٢ · إنت طلبت');
  setLang('en');
  assert.equal(metaLine(t, e({ why: 'focus' })), '10:12 · you asked: “focus”');
  setLang('ar');
});

test('today: counts in her words', () => {
  setLang('ar');
  const keys = ['todayCount1', 'todayCount2', 'todayCountFew', 'todayCountMany'];
  assert.equal(countText(t, 1, keys), 'شغلة وحدة');
  assert.equal(countText(t, 2, keys), 'شغلتين');
  assert.equal(countText(t, 6, keys), '٦ شغلات');
  assert.equal(countText(t, 14, keys), '١٤ شغلة');
  assert.equal(countText(t, 1, ['todayAsked1', 'todayAsked2', 'todayAskedN']), 'سألتك مرة');
  assert.equal(countText(t, 1, ['todayRefused1', 'todayRefused2', 'todayRefusedN']), 'رفضت مرة');
});

test('today: icons follow the mockup', () => {
  assert.equal(iconFor(e({})), HAND.timer);
  assert.equal(iconFor(e({ tool: 'music.control' })), HAND.mic);
  assert.equal(iconFor(e({ tool: 'note.save' })), HAND.note);
  assert.equal(iconFor(e({ tool: 'settings.set' })), HAND.plus);
  assert.equal(iconFor(e({ tool: 'settings.set', outcome: 'asked-no' })), HAND.shield);
  assert.equal(iconFor(e({ tool: 'claude.permission', outcome: 'refused' })), HAND.terminal);
});

test('today: every page word has its English mirror', () => {
  const mine = Object.keys(strings.ar).filter((k) => k.startsWith('today'));
  assert.ok(mine.length > 30);
  for (const k of mine) {
    assert.ok(k in strings.en, k);
    if (Array.isArray(strings.ar[k])) assert.equal(strings.ar[k].length, strings.en[k].length, k);
  }
  assert.equal(strings.ar.todayNote, 'بحفظ هالسجل ٣٠ يوم، مقفول على جهازك. «منيح» و«مش منيح» بيعلّموني شو بتحب.');
});
