// عام (plan 0.10c Task 7; mockup PSettingsGeneral): light or dark (this window, its caption and her menu), and the
// page's language (Arabic or English; she still speaks Arabic).
import { h, title, sub, group, segmented, newId } from '../controls.js';

const LOOKS = ['system', 'light', 'dark'];
const LANGS = ['auto', 'arabic', 'english'];
const lower = (v) => String(v ?? '').toLowerCase();

export function render({ S, tx, set, render: rerender }) {
  const v = S.value;
  const line = (labelKey, hintKey, seg, labelId) => h('div', { class: 'row' },
    h('span', { class: 'label-col' }, h('span', { class: 'label', id: labelId, text: tx(labelKey) }), h('span', { class: 'desc', text: tx(hintKey) })),
    seg);
  const lookId = newId('ap'), langId = newId('lg');
  const look = segmented(lookId, tx('looks'), Math.max(0, LOOKS.indexOf(lower(v.appearance))), async (i) => {
    const r = await set('appearance', LOOKS[i]);
    if (r.ok) window.dispatchEvent(new Event('sahar-look'));
  }, 'appearance');
  const lang = segmented(langId, tx('langs'), Math.max(0, LANGS.indexOf(lower(v.language))), async (i) => {
    const r = await set('language', LANGS[i]);
    if (r.ok) window.dispatchEvent(new Event('sahar-look'));
  }, 'language');
  return [title(tx('pageGeneral')), sub(tx('generalSub')), group(line('lookLabel', 'lookHint', look, lookId), line('langLabel', 'langHint', lang, langId))];
}
