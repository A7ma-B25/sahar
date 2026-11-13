import { test } from 'node:test';
import assert from 'node:assert/strict';
import { pages, visiblePages } from '../../src/Sahar.Settings/web/js/pages/registry.js';

test('registry: visible pages are sahar,menu,prayer,listen,general,privacy,about in three groups + foot', () => {
  const caps = { pages: ['sahar', 'menu', 'prayer', 'listen', 'general', 'privacy', 'about'] };
  const v = visiblePages(caps);
  assert.deepEqual(v.map((p) => p.id), ['sahar', 'menu', 'prayer', 'listen', 'general', 'privacy', 'about']);
  assert.deepEqual(v.map((p) => p.group), [1, 1, 2, 2, 3, 3, 4]);
});

test('registry: all nine pages are known; sounds and claude are not available', () => {
  assert.equal(pages.length, 9);
  for (const id of ['sounds', 'claude']) assert.equal(pages.find((p) => p.id === id).available, false);
  assert.ok(!visiblePages({ pages: ['sounds', 'claude', 'sahar'] }).some((p) => p.id === 'sounds' || p.id === 'claude'));
});

test('registry: a page the host does not list is not shown', () => {
  assert.deepEqual(visiblePages({ pages: ['sahar', 'about'] }).map((p) => p.id), ['sahar', 'about']);
});
