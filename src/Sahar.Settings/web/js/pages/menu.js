// القائمة (plan 0.10c Task 6; mockup PSettingsSoul, page «القائمة»): what her card shows, what its header says, how and
// where it opens, and the keyboard shortcut (recorded; refused ones say why under the row).
import { h, title, sub, section, group, row, segmented, switchRow, ghost, kbd, newId } from '../controls.js';
import { fromKeyEvent } from '../shortcut-recorder.js';

const HEADERS = ['urgent', 'prayer', 'none'];
const OPENS = ['double', 'right', 'both'];
const PLACES = ['beside', 'center'];
const lower = (v) => String(v ?? '').toLowerCase();

let recorder = null;
export function leave() {
  if (recorder) document.removeEventListener('keydown', recorder, true);
  recorder = null;
}

export function render({ S, tx, set, ask }) {
  const v = S.value;
  const items = S.caps.items || [];
  const chosen = () => (Array.isArray(S.value.menuItems) ? S.value.menuItems : items);

  const switches = items.map((id) => {
    const hint = tx(`itemHint_${id}`);
    const b = switchRow(tx(`item_${id}`), hint === `itemHint_${id}` ? '' : hint, chosen().includes(id), async (on, btn) => {
      const now = chosen();
      const next = items.filter((x) => (x === id ? on : now.includes(x)));
      const r = await set('menuItems', next);
      if (!r.ok) btn.set(!on);
    }, `item-${id}`);
    b.id = `item-${id}`;
    return b;
  });

  const segRow = (labelKey, optionsKey, values, key, id) => {
    const labelId = newId('m');
    const seg = segmented(labelId, tx(optionsKey), Math.max(0, values.indexOf(lower(v[key]))), (i) => set(key, values[i]), id);
    return row(tx(labelKey), seg, { labelId });
  };

  // اختصار الكيبورد: «غيّر» → «اكبس الاختصار الجديد…» (Esc cancels) → the bridge (Core check, then her hotkey).
  const value = kbd(v.menuShortcut || 'Ctrl+Alt+S');
  value.id = 'shortcut-value';
  const message = h('span', { class: 'desc', id: 'shortcut-message', 'aria-live': 'polite' });
  const change = ghost(tx('shortcutChange'), () => start(), { id: 'shortcut-change' });
  function stop() {
    leave();
    change.textContent = tx('shortcutChange');
    change.setAttribute('aria-pressed', 'false');
  }
  function start() {
    if (recorder) { stop(); return; }
    message.textContent = tx('shortcutRecording');
    message.className = 'desc';
    change.setAttribute('aria-pressed', 'true');
    recorder = async (e) => {
      const got = fromKeyEvent(e);
      if (got === null) return;   // a modifier on its own: wait for the key
      e.preventDefault();
      e.stopPropagation();
      stop();
      if (got === 'cancel') { message.textContent = ''; return; }
      const r = await ask('shortcut', { value: got });
      if (r.ok) {
        value.textContent = S.value.menuShortcut || got;
        message.className = 'desc';
        message.textContent = tx('shortcutOk', { value: value.textContent });
      } else {
        message.className = 'error-text';
        message.textContent = r.reason || r.error || '';
      }
    };
    document.addEventListener('keydown', recorder, true);
  }
  const shortcutRow = h('div', { class: 'row' },
    h('span', { class: 'label-col' }, h('span', { class: 'label', text: tx('shortcutLabel') }), h('span', { class: 'desc', text: tx('shortcutHint') })),
    h('span', { class: 'kbd-row' }, value, change));

  return [
    title(tx('pageMenu')),
    sub(tx('menuSub')),
    section(tx('groupItems')),
    group(...switches),
    section(tx('groupHeader')),
    group(segRow('headerLabel', 'headers', HEADERS, 'menuHeader', 'menu-header')),
    section(tx('groupOpen')),
    group(
      segRow('openLabel', 'opens', OPENS, 'menuOpen', 'menu-open'),
      segRow('placeLabel', 'places', PLACES, 'menuPlace', 'menu-place'),
      shortcutRow,
      h('div', { class: 'row-error' }, message)),
  ];
}
