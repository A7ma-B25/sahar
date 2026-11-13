import { test } from 'node:test';
import assert from 'node:assert/strict';
import { pages, visiblePages } from '../../src/Sahar.Settings/web/js/pages/registry.js';

test('registry: visible pages (0.14: her brain is group 2) in four groups + foot', () => {
  const ids = ['sahar', 'menu', 'brain', 'models', 'you', 'today', 'prayer', 'listen', 'activities', 'general', 'privacy', 'about'];
  const v = visiblePages({ pages: ids });
  assert.deepEqual(v.map((p) => p.id), ids);
  assert.deepEqual(v.map((p) => p.group), [1, 1, 2, 2, 2, 2, 3, 3, 3, 4, 4, 5]);
});

test('registry: all fourteen pages are known; sounds and claude are not available', () => {
  assert.equal(pages.length, 14);
  for (const id of ['sounds', 'claude']) assert.equal(pages.find((p) => p.id === id).available, false);
  assert.ok(!visiblePages({ pages: ['sounds', 'claude', 'sahar'] }).some((p) => p.id === 'sounds' || p.id === 'claude'));
});

test('registry: a page the host does not list is not shown', () => {
  assert.deepEqual(visiblePages({ pages: ['sahar', 'about'] }).map((p) => p.id), ['sahar', 'about']);
});

test('registry: النشاطات (0.13) sits after listening in the prayer/listening group (third since 0.14), with the pulse icon', () => {
  const v = visiblePages({ pages: ['sahar', 'menu', 'prayer', 'listen', 'activities', 'general', 'privacy', 'about'] });
  assert.deepEqual(v.map((p) => p.id), ['sahar', 'menu', 'prayer', 'listen', 'activities', 'general', 'privacy', 'about']);
  const a = v.find((p) => p.id === 'activities');
  assert.equal(a.group, 3);
  assert.equal(a.icon, 'pulse');
});
