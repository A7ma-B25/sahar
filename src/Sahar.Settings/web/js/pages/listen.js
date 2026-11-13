// الاستماع (plan 0.10c Task 7; mockup PSettingsListening): who recites when you play the Quran from her menu.
import { h, title, sub, section, radioRow, note } from '../controls.js';
import { getLang } from '../i18n.js';

const LATIN = {
  'ar.alafasy': 'Mishary Alafasy', 'ar.abdulbasitmurattal': 'Abdul Basit Abdul Samad', 'ar.husary': 'Mahmoud Khalil Al-Husary',
  'ar.minshawi': 'Mohamed Siddiq Al-Minshawi', 'ar.mahermuaiqly': 'Maher Al-Muaiqly', 'ar.abdurrahmaansudais': 'Abdul Rahman Al-Sudais',
};

export function render({ S, tx, set }) {
  const rows = (S.caps.reciters || []).map((r) => {
    const b = radioRow(getLang() === 'ar' ? r.name : (LATIN[r.id] ?? r.name), S.value.quranReciter === r.id, async () => {
      const res = await set('quranReciter', r.id);
      if (res.ok) for (const x of group.querySelectorAll('[role=radio]')) x.setAttribute('aria-checked', x.dataset.value === r.id ? 'true' : 'false');
    }, r.id);
    b.id = `reciter-${r.id.replace('.', '-')}`;
    b.tabIndex = S.value.quranReciter === r.id ? 0 : -1;
    return b;
  });
  if (!rows.some((b) => b.tabIndex === 0) && rows[0]) rows[0].tabIndex = 0;
  const group = h('section', { class: 'group', role: 'radiogroup', 'aria-labelledby': 'reciter-title' }, ...rows, note(tx('listenNote')));
  // Arrow keys move between the reciters (a radio group).
  group.addEventListener('keydown', (e) => {
    const list = [...group.querySelectorAll('[role=radio]')];
    const i = list.indexOf(document.activeElement);
    if (i < 0) return;
    const step = e.key === 'ArrowDown' ? 1 : e.key === 'ArrowUp' ? -1 : 0;
    if (!step) return;
    e.preventDefault();
    const next = list[(i + step + list.length) % list.length];
    list.forEach((x) => { x.tabIndex = x === next ? 0 : -1; });
    next.focus();
    next.click();
  });
  return [title(tx('pageListen')), sub(tx('listenSub')), section(tx('reciterGroup'), 'reciter-title'), group];
}
