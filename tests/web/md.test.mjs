// 0.17 spec §8: her long online words render as a tiny safe markdown subset — trees only, never HTML, never a link
// or an image.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { md } from '../../src/Sahar.Settings/web/surfaces/md.js';
import { h, text, findAll, walk } from '../../src/Sahar.Settings/web/surfaces/h.js';
import { chatView } from '../../src/Sahar.Settings/web/surfaces/chatviews.js';

const tree = (s) => h('div', null, md(s));
const tags = (s) => [...walk(tree(s))].map((n) => n.tag);

test('md: bold, italic, code, lists, headings, fences', () => {
  const t = tree('# عنوان\n\nهاد **مهم** و *مايل* و `x = 1`\n\n- أول\n- تاني\n\n1. واحد\n٢. اتنين\n\n```\nlet a = 1;\n**not bold**\n```');
  const all = [...walk(t)];
  assert.ok(all.some((n) => n.tag === 'strong' && text(n) === 'مهم'));
  assert.ok(all.some((n) => n.tag === 'em' && text(n) === 'مايل'));
  assert.ok(all.some((n) => n.tag === 'code' && text(n) === 'x = 1'));
  assert.equal(findAll(t, (n) => n.tag === 'ul')[0].kids.length, 2);
  assert.equal(findAll(t, (n) => n.tag === 'ol')[0].kids.length, 2);
  const pre = findAll(t, (n) => n.tag === 'pre')[0];
  assert.equal(text(pre), 'let a = 1;\n**not bold**');   // nothing is parsed inside a fence
  assert.ok(all.some((n) => n.tag === 'p' && n.attrs.cls === 'mdh' && text(n) === 'عنوان'));
});

test('md: links stay text, images never load, HTML is text', () => {
  const s = 'شوف [هون](https://evil.example/x) و ![صورة](https://evil.example/p.png) و <img src=x onerror=alert(1)> <a href="x">y</a> https://evil.example';
  const t = tree(s);
  for (const n of walk(t)) {
    assert.ok(!['a', 'img', 'script', 'iframe', 'svg'].includes(n.tag), n.tag);
    for (const k of Object.keys(n.attrs)) assert.ok(!['href', 'src', 'onerror', 'style'].includes(k), k);
  }
  assert.match(text(t), /هون \(https:\/\/evil\.example\/x\)/);
  assert.match(text(t), /<img src=x onerror=alert\(1\)>/);
});

test('md: plain lines keep their line breaks; empty input is empty', () => {
  assert.deepEqual(md(''), []);
  assert.equal(text(tree('سطر\nسطر تاني')), 'سطر\nسطر تاني');
  assert.ok(!tags('**not closed').includes('strong'));
});

const labels = { all: 'كل', new: 'جديد', close: 'سكّر', typing: 'عم تكتب…', send: 'ابعت', composer: 'c', placeholder: 'p', newBelow: 'جديد', back: 'رجوع', dialog: 'd' };

test('chat: her text renders markdown, his stays plain; the badge and the chip', () => {
  let emitted = null;
  const m = { view: 'thread', name: 'سحورة', status: '', labels, max: 500,
    brain: { show: true, value: 'online', online: 'أونلاين', local: 'محلي', hint: 'h' },
    days: [{ label: 'اليوم', groups: [
      { who: 'me', time: '١', msgs: [{ id: 'a', kind: 'text', text: '**هو**' }] },
      { who: 'her', time: '٢', msgs: [{ id: 'b', kind: 'text', text: '**هي**', brain: 'أونلاين', brainId: 'online' }] }] }] };
  const t = chatView(m, { draft: '' }, (k, v) => { emitted = [k, v]; });
  const me = findAll(t, (n) => n.attrs['data-who'] === 'me')[0];
  assert.equal(findAll(me, (n) => n.tag === 'strong').length, 0);
  const her = findAll(t, (n) => n.attrs['data-who'] === 'her')[0];
  assert.equal(text(findAll(her, (n) => n.tag === 'strong')[0]), 'هي');
  const badge = findAll(her, (n) => String(n.attrs.cls || '').includes('brainbadge'))[0];
  assert.equal(text(badge), 'أونلاين');
  const chip = findAll(t, (n) => n.attrs['data-id'] === 'brain')[0];
  assert.equal(text(chip), 'أونلاين');
  chip.attrs.on.click();
  assert.deepEqual(emitted, ['brain', 'local']);
});

test('chat: no chip when online is not set up', () => {
  const m = { view: 'thread', name: 'سحورة', status: '', labels, days: [], brain: { show: false, value: 'online' } };
  assert.equal(findAll(chatView(m, {}, () => {}), (n) => n.attrs['data-id'] === 'brain').length, 0);
  assert.equal(findAll(chatView({ ...m, brain: undefined }, {}, () => {}), (n) => n.attrs['data-id'] === 'brain').length, 0);
});
