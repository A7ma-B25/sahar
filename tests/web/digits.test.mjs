import { test } from 'node:test';
import assert from 'node:assert/strict';
import { toArabicIndic, cells } from '../../src/Sahar.Settings/web/js/digits.js';

test('digits: toArabicIndic("4:52") = «٤:٥٢»', () => assert.equal(toArabicIndic('4:52'), '٤:٥٢'));
test('digits: other characters stay', () => assert.equal(toArabicIndic('بعد 42:10 · 0.12.0'), 'بعد ٤٢:١٠ · ٠.١٢.٠'));

// A tiny DOM stand-in: cells() uses only createElement / createTextNode / append / replaceChildren.
function fakeDoc() {
  const node = (tag) => ({ tag, children: [], className: '', textContent: '', append(...c) { this.children.push(...c); }, replaceChildren(...c) { this.children = c; } });
  return { createElement: (tag) => node(tag), createTextNode: (text) => ({ text }) };
}

test('digits: cells wraps each digit in a .d span', () => {
  const doc = fakeDoc();
  const el = doc.createElement('span');
  el.ownerDocument = doc;
  cells(el, '٤:٥٢');
  const kinds = el.children.map((c) => (c.tag ? `${c.tag}.${c.className}:${c.textContent}` : `#${c.text}`));
  assert.deepEqual(kinds, ['span.d:٤', '#:', 'span.d:٥', 'span.d:٢']);
});
