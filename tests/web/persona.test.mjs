// 0.16 «شخصية سحر» (owner ruling 8): the page's words and place.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { strings } from '../../src/Sahar.Settings/web/js/i18n.js';
import '../../src/Sahar.Settings/web/js/strings/persona.js';
import { pages } from '../../src/Sahar.Settings/web/js/pages/registry.js';
import { ICONS } from '../../src/Sahar.Settings/web/js/icons.js';

const keys = (lang) => Object.keys(strings[lang]).filter((k) => k.startsWith('persona')).sort();

test('persona: every word in both languages, none empty', () => {
  assert.ok(keys('ar').length >= 20);
  assert.deepEqual(keys('ar'), keys('en'));
  for (const l of ['ar', 'en']) for (const k of keys(l)) assert.ok(strings[l][k].length > 0, `${l}.${k}`);
});

test('persona: the owner\'s words — the page title and the restore button', () => {
  assert.equal(strings.ar.personaTitle, 'شخصية سحر');
  assert.equal(strings.ar.personaRestore, 'رجّعها متل ما كانت');
  assert.equal(strings.ar.personaRestoreNo, 'لا');
  assert.match(strings.ar.personaHint, /\{بدو\|بدها\}/);   // the {his|hers} form PersonaFile.ForGender reads
});

test('persona: the page sits right after «عقلي», with an icon that exists', () => {
  const i = pages.findIndex((p) => p.id === 'persona');
  assert.equal(pages[i - 1].id, 'brain');
  assert.equal(pages[i].group, 2);
  assert.ok(ICONS[pages[i].icon]);
  assert.equal(strings.ar.pagePersona, 'شخصيتي');
});
