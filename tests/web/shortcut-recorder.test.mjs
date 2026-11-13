import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fromKeyEvent } from '../../src/Sahar.Settings/web/js/shortcut-recorder.js';

const k = (code, mods = {}, key = '') => ({ key, code, ctrlKey: !!mods.ctrl, altKey: !!mods.alt, shiftKey: !!mods.shift, metaKey: !!mods.meta });

test('recorder: Alt+KeyS → "Alt+S"', () => assert.equal(fromKeyEvent(k('KeyS', { alt: true }, 's')), 'Alt+S'));
test('recorder: Ctrl+Alt+KeyK → "Ctrl+Alt+K"', () => assert.equal(fromKeyEvent(k('KeyK', { ctrl: true, alt: true }, 'k')), 'Ctrl+Alt+K'));
test('recorder: Ctrl+Shift+F5 → "Ctrl+Shift+F5"', () => assert.equal(fromKeyEvent(k('F5', { ctrl: true, shift: true }, 'F5')), 'Ctrl+Shift+F5'));
test('recorder: a modifier alone → null', () => {
  assert.equal(fromKeyEvent(k('AltLeft', { alt: true }, 'Alt')), null);
  assert.equal(fromKeyEvent(k('ControlRight', { ctrl: true }, 'Control')), null);
});
test('recorder: an Arabic layout reads the key, not the letter', () => assert.equal(fromKeyEvent(k('KeyS', { alt: true }, 'س')), 'Alt+S'));
test('recorder: digits, Space and Win', () => {
  assert.equal(fromKeyEvent(k('Digit7', { ctrl: true, alt: true }, '7')), 'Ctrl+Alt+7');
  assert.equal(fromKeyEvent(k('Space', { ctrl: true }, ' ')), 'Ctrl+Space');
  assert.equal(fromKeyEvent(k('KeyK', { meta: true }, 'k')), 'Win+K');
});
test('recorder: Escape cancels; unknown keys are null', () => {
  assert.equal(fromKeyEvent(k('Escape', {}, 'Escape')), 'cancel');
  assert.equal(fromKeyEvent(k('Semicolon', { ctrl: true }, ';')), null);
});
