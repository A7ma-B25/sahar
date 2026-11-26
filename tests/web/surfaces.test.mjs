// 0.16 surfaces: the panel and chat views are pure trees; fold.js agrees with Sahar.Core.Text.ArabicFold.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fold, matches } from '../../src/Sahar.Settings/web/surfaces/fold.js';
import { text, find, findAll, has } from '../../src/Sahar.Settings/web/surfaces/h.js';
import { listView, inputView, askView, trayView, drawerView, visibleRows } from '../../src/Sahar.Settings/web/surfaces/views.js';
import { chatView } from '../../src/Sahar.Settings/web/surfaces/chatviews.js';
import { PATHS } from '../../src/Sahar.Settings/web/surfaces/icons.js';

const SURAHS = ['الفاتحة', 'البقرة', 'آل عمران'];
const noop = () => {};

test('fold: the same fixtures as ArabicFoldTests', () => {
  assert.equal(fold('يٰسٓ'), 'يس');
  assert.equal(fold('الإسراء'), 'الاسراء');
  assert.equal(fold('آل عمران'), 'ال عمران');
  assert.equal(fold('فاطِمَة'), 'فاطمه');
  assert.equal(fold('الضحى'), 'الضحي');
  assert.equal(fold('المؤمنون'), 'المومنون');
  assert.equal(fold('  الكهف '), 'الكهف');
  assert.equal(fold('AbC'), 'abc');
  for (const [q, hit] of [['يس', true], ['يٰس', true], ['٣٦', true], ['36', true], ['3', false], ['يسن', false]]) assert.equal(matches(q, 'يس', 36), hit, q);
  assert.ok(matches('عمران', 'آل عمران', 3));
});

const surahModel = () => ({
  kind: 'list', title: 'شو بدك تسمع؟', sub: { text: 'بصوت مشاري العفاسي', action: 'reciter' }, search: { placeholder: 'دوّر على سورة أو رقمها' },
  enterHint: 'Enter لتشغّلها', listHeight: 302,
  rows: [{ id: 'resume', label: 'كمّل الكهف', meta: 'وقفت عند ١٢:٠٤', play: true, sel: true, pinned: true, strong: true },
    ...Array.from({ length: 114 }, (_, i) => ({ id: String(i + 1), label: i < 3 ? SURAHS[i] : (i === 35 ? 'يس' : 'سورة' + (i + 1)), num: i + 1, numText: String(i + 1) }))],
  none: { icon: 'book', title: 'ما لقيت سورة بهالاسم', hint: 'جرّب رقمها' },
  foot: { hint: 'Esc لسكّر' },
});

test('picker: the resume row is first, then the surahs; typing «يس» leaves one row with the Enter hint', () => {
  const m = surahModel();
  const t = listView(m, {}, noop);
  const rows = findAll(t, (n) => n.attrs.role === 'option');
  assert.equal(rows.length, 115);
  assert.equal(rows[0].attrs['data-id'], 'resume');
  const s = listView(m, { query: 'يس' }, noop);
  const hits = findAll(s, (n) => n.attrs.role === 'option');
  assert.equal(hits.length, 1);
  assert.equal(hits[0].attrs['data-id'], '36');
  assert.match(text(hits[0]), /Enter لتشغّلها/);
  assert.equal(visibleRows(m, '٣٦').length, 1);
  assert.equal(visibleRows(m, 'zz').length, 0);
});

test('picker: the list keeps its height while typing, and no result shows the hint', () => {
  const m = surahModel();
  const h1 = find(listView(m, {}, noop), (n) => n.attrs.role === 'listbox').attrs.style;
  const h2 = find(listView(m, { query: 'يس' }, noop), (n) => n.attrs.role === 'listbox').attrs.style;
  assert.equal(h1, h2);
  const none = listView(m, { query: 'الفرقانن' }, noop);
  assert.match(text(none), /ما لقيت سورة بهالاسم/);
  assert.match(find(none, (n) => has(n, 'empty')).attrs.style, /height:302px/);
});

test('clipboard: «امسح الكل» first asks «أكيد؟»; «لا» goes back', () => {
  const events = [];
  const m = { kind: 'list', title: 'آخر شي نسخته', rows: [{ id: '0', label: 'سلام', icon: 'copy', meta: 'هلق' }],
    foot: { action: { id: 'clear', label: 'امسح الكل', icon: 'trash', confirm: { q: 'أكيد؟', yes: 'إي، امسح', no: 'لا' } } } };
  const t = listView(m, {}, (k, v) => events.push([k, v]));
  find(t, (n) => n.attrs['data-id'] === 'clear').attrs.on.click();
  assert.deepEqual(events, [['confirm', undefined]]);
  const c = listView(m, { confirm: true }, (k, v) => events.push([k, v]));
  assert.match(text(c), /أكيد؟/);
  find(c, (n) => n.attrs['data-id'] === 'confirm-yes').attrs.on.click();
  assert.deepEqual(events.at(-1), ['action', 'clear']);
});

test('input: an error shows under the field with the warn ring, his text is kept; new typing hides it', () => {
  const m = { kind: 'input', title: 'قديش بدك المؤقت؟', field: { icon: 'clock', placeholder: 'دقيقة', value: 'شي ساعة ونص' }, error: 'ما فهمتها. اكتب الدقايق رقم، مثلاً ٩٠.',
    primary: { id: 'go', label: 'شغّل' } };
  const t = inputView(m, {}, noop);
  assert.ok(find(t, (n) => has(n, 'field') && has(n, 'err')));
  assert.equal(find(t, (n) => n.attrs['data-id'] === 'value').attrs.value, 'شي ساعة ونص');
  assert.match(text(t), /ما فهمتها/);
  const typed = inputView(m, { value: '٤٠', dirty: true }, noop);
  assert.equal(find(typed, (n) => has(n, 'err')), null);
});

test('ask bubble: buttons keep their order (1 = yes) and the tail is there', () => {
  const t = askView({ kind: 'ask', text: 'بدّك شغّل مؤقت ٢٥ دقيقة؟', buttons: [{ id: 'yes', label: 'إي', primary: true }, { id: 'no', label: 'لا' }] }, {}, noop);
  const bs = findAll(t, (n) => n.attrs['data-key']);
  assert.deepEqual(bs.map((b) => [b.attrs['data-key'], b.attrs['data-id']]), [['1', 'yes'], ['2', 'no']]);
  assert.ok(find(t, (n) => has(n, 'tail')));
});

test('tray: one language, weather is a toggle with the city, no «(اسمي)», quit last', () => {
  const m = { name: 'سحورة', status: 'صاحية، وهادية', seg: { label: 'نشاطها', value: 'calm', options: [{ id: 'calm', label: 'هادية' }, { id: 'medium', label: 'متوسطة' }, { id: 'lively', label: 'نشيطة' }] },
    items: [{ id: 'settings', label: 'إعداداتي', icon: 'gear' }, { id: 'weather', label: 'الطقس', icon: 'rain', meta: 'دمشق', toggle: true }, { id: 'who', label: 'مين أنا؟', icon: 'user' }],
    quit: { id: 'quit', label: 'سكّري سحورة' } };
  const t = trayView(m, {}, noop);
  const all = text(t);
  assert.doesNotMatch(all, /[A-Za-z]{3,}/);
  assert.doesNotMatch(all, /\(اسمي\)/);
  const w = find(t, (n) => n.attrs['data-id'] === 'weather');
  assert.equal(w.attrs.role, 'menuitemcheckbox');
  assert.equal(w.attrs['aria-checked'], 'true');
  const ids = findAll(t, (n) => n.attrs['data-id'] && !String(n.attrs['data-id']).startsWith('seg-')).map((n) => n.attrs['data-id']);
  assert.equal(ids.at(-1), 'quit');
});

test('drawer: live, shelf and what happened, each section only when it has rows', () => {
  const m = { kind: 'drawer', title: 'الدرج', sections: [{ title: 'شغّال هلق', rows: [{ icon: 'clock', label: 'مؤقت', meta: '٢٤:١٢', live: true }] }, { title: 'عالرف', rows: [] },
    { title: 'شو صار', rows: [{ icon: 'bell', label: 'خلص المؤقت', meta: '٩:١٢' }] }], empty: { title: 'ما في شي هلق' }, foot: {} };
  const t = drawerView(m, {}, noop);
  assert.deepEqual(findAll(t, (n) => has(n, 'sect')).map(text), ['شغّال هلق', 'شو صار']);
  assert.ok(find(t, (n) => has(n, 'live')));
});

const labels = { all: 'كل الحكي', new: 'حكي جديد', close: 'سكّر', typing: 'عم تكتب…', send: 'ابعت', composer: 'احكي مع سحورة', placeholder: 'احكي معي…', newBelow: 'جديد', back: 'رجوع', dialog: 'الحكي مع سحورة' };

test('chat: his lines are .me (left in RTL), hers .her (right); first of a group cuts its corner; a draft and an ask show their buttons', () => {
  const m = { view: 'thread', name: 'سحورة', status: 'صباح الخير', labels, max: 500, days: [{ label: 'اليوم', groups: [
    { who: 'me', time: '١٠:٤٠', msgs: [{ id: 'a', kind: 'text', text: 'صباح الخير 😊' }, { id: 'b', kind: 'text', text: 'شو أول صلاة جاية؟' }] },
    { who: 'her', time: '١٠:٤٠', msgs: [{ id: 'c', kind: 'text', text: 'الظهر بعد ٤٢ دقيقة.' },
      { id: 'd', kind: 'draft', text: 'أهلين أبو سليم', draft: { label: 'بصوتك', copy: 'انسخها', copiedLabel: 'نسختها', note: 'ما ببعتها لحدا، إنت بتلصقها' } },
      { id: 'e', kind: 'ask', text: 'بتسمحلي؟', ask: { yes: 'إي، احفظها', no: 'لا' } }] }] }] };
  const t = chatView(m, { draft: '' }, noop);
  const msgs = findAll(t, (n) => has(n, 'msg'));
  assert.deepEqual(msgs.map((x) => x.attrs['data-who']), ['me', 'me', 'her', 'her']);
  assert.ok(has(msgs[0], 'first') && !has(msgs[1], 'first'));
  assert.match(text(t), /😊/);   // her and his emoji stay (owner, 2026-11-26)
  assert.ok(find(t, (n) => n.attrs['data-id'] === 'copy-d'));
  assert.ok(find(t, (n) => n.attrs['data-id'] === 'yes-e'));
  assert.equal(find(t, (n) => n.attrs['data-id'] === 'send').attrs.disabled, true);
});

test('chat: typing shows three dots, a streamed partial replaces them', () => {
  const base = { view: 'thread', name: 'سحورة', status: '', labels, days: [] };
  assert.equal(findAll(chatView({ ...base, typing: true }, {}, noop), (n) => has(n, 'dot')).length, 3);
  const p = chatView({ ...base, typing: true, partial: 'عم' }, {}, noop);
  assert.equal(findAll(p, (n) => has(n, 'dot')).length, 0);
  assert.match(text(p), /عم/);
});

test('chat: the list has «حكي جديد» on top, groups, and the live retention footer', () => {
  const m = { view: 'list', labels, list: { title: 'حكينا', footer: 'محفوظ عندك بس، ومقفول. بينمسح لحاله بعد ٣٠ يوم.', sure: 'أكيد؟', deleteYes: 'إي، امسحه', no: 'لا', rename: 'سمّيه', del: 'امسحه',
    sections: [{ title: 'اليوم', rows: [{ id: 'x', title: 'صباح الخير', last: 'إي ياريت', time: '١٠:٤١', sel: true }] }] } };
  const t = chatView(m, {}, noop);
  assert.match(text(t), /حكي جديد/);
  assert.match(text(t), /٣٠ يوم/);
  assert.ok(find(t, (n) => n.attrs['data-id'] === 'open-x'));
});

test('icons: every icon the views name exists', () => {
  for (const n of ['search', 'play', 'pause', 'stop', 'book', 'copy', 'trash', 'clock', 'check', 'x', 'menu', 'compose', 'send', 'pen', 'lock', 'power', 'gear', 'rain', 'bars', 'chevEnd', 'chevStart', 'arrowDown'])
    assert.ok(PATHS[n], n);
});

test('drawer: a section the engine sent without rows (an empty list is left out of its JSON) does not break it', () => {
  const t = drawerView({ kind: 'drawer', title: 'الدرج', sections: [{ title: 'شغّال هلق' }, { title: 'شو صار اليوم', rows: [{ icon: 'bell', label: 'خلص المؤقت' }] }], empty: { title: 'ما في شي' }, foot: {} }, {}, noop);
  assert.match(text(t), /خلص المؤقت/);
  const none = drawerView({ kind: 'drawer', title: 'الدرج', sections: [{ title: 'a' }], empty: { title: 'ما في شي هلق' }, foot: {} }, {}, noop);
  assert.match(text(none), /ما في شي هلق/);
});
