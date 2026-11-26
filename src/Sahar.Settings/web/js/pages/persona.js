// «شخصية سحر» (0.16 Task 7, owner ruling 8): her personality as he can read and change it — one editor (persona.md
// beside settings.json, read by her brain each turn), «احفظ», and «رجّعها متل ما كانت» behind a «أكيد؟». Under it, what
// never changes: the safety rules live in her code, not in the file. No mockup exists for this page: it follows the
// approved settings pages (title, sub, a card, a group of rule rows). Text via textContent only; nothing is logged.
import { h, title, sub, section, group, note, pri, ghost } from '../controls.js';
import { card, chip, item, HAND } from '../mind-ui.js';
import { toArabicIndic } from '../digits.js';
import { getLang } from '../i18n.js';
import '../strings/persona.js';

// What stays across redraws: the text being written (not yet saved), the open «أكيد؟», the last status line.
const state = { draft: null, asking: false, status: null, statusKind: '' };
let flushSave = null;

/** Going to another page (not a redraw of this one: app.js calls leave() on every render): the question closes. */
export function away() {
  state.asking = false;
  state.status = null;
}

/** The window is closing: a text he changed and did not save is saved now. */
export async function flush() { if (flushSave) await flushSave(); }

const num = (n) => (getLang() === 'ar' ? toArabicIndic(String(n)) : String(n));

export async function render({ tx, ask, render: rerender }) {
  const r = await ask('persona-get');
  const saved = r.ok ? r : { text: '', default: '', edited: false, max: 2000 };
  const max = saved.max || 2000;
  const text = state.draft ?? saved.text;
  const redraw = () => rerender(true);

  const area = h('textarea', {
    class: 'input ps-area', id: 'persona-text', dir: 'auto', rows: '12', spellcheck: 'false',
    'aria-labelledby': 'persona-label', 'aria-describedby': 'persona-hint persona-count',
  });
  area.value = text;
  const count = h('span', { class: 'm-meta', id: 'persona-count', 'aria-live': 'off' });
  const showCount = () => {
    const n = area.value.trim().length;
    count.textContent = tx('personaCount', { n: num(n), max: num(max) });
    count.classList.toggle('ps-over', n > max);
  };
  showCount();
  area.addEventListener('input', () => { state.draft = area.value; showCount(); });

  const status = h('p', { class: `ps-status ${state.statusKind}`, id: 'persona-status', role: 'status', 'aria-live': 'polite', text: state.status || '' });

  async function save() {
    const res = await ask('persona-save', { text: area.value });
    if (res.ok) { state.draft = null; state.status = tx('personaSaved'); state.statusKind = ''; }
    else {
      state.statusKind = 'error';
      state.status = res.error === 'empty' ? tx('personaEmpty') : res.error === 'too-long' ? tx('personaTooLong', { max: num(max) }) : tx('personaFailed');
    }
    redraw();
    return res;
  }
  flushSave = async () => { if (state.draft !== null && state.draft !== saved.text && state.draft.trim()) await ask('persona-save', { text: state.draft }); };

  async function restore() {
    const res = await ask('persona-restore');
    state.asking = false;
    state.draft = null;
    state.status = res.ok ? tx('personaRestored') : tx('personaFailed');
    state.statusKind = res.ok ? '' : 'error';
    redraw();
  }

  const head = h('div', { class: 'ps-head' },
    h('span', { class: 'label', id: 'persona-label', text: tx('personaLabel') }),
    chip(saved.edited ? tx('personaEdited') : tx('personaOriginal'), saved.edited ? 'accent' : null));
  head.lastChild.id = 'persona-state';

  const actions = state.asking
    ? h('div', { class: 'ps-ask', id: 'persona-ask', role: 'group', 'aria-labelledby': 'persona-ask-text' },
      h('span', { class: 'desc', id: 'persona-ask-text', text: tx('personaRestoreAsk') }),
      h('span', { class: 'btn-row' },
        pri(tx('personaRestoreYes'), restore, { id: 'persona-restore-yes' }),
        ghost(tx('personaRestoreNo'), () => { state.asking = false; redraw(); }, { id: 'persona-restore-no' })))
    : h('div', { class: 'ps-actions' },
      pri(tx('personaSave'), save, { id: 'persona-save' }),
      ghost(tx('personaRestore'), () => { state.asking = true; state.status = null; redraw(); }, { id: 'persona-restore', disabled: !saved.edited && state.draft === null }));
  // After app.js has put the page in the document (a microtask would run before that).
  if (state.asking) setTimeout(() => document.getElementById('persona-restore-no')?.focus(), 0);

  const rule = (line, hint) => item({ icon: HAND.shield, line: tx(line), under: [h('span', { class: 'desc', text: tx(hint) })] });

  return [
    title(tx('personaTitle')),
    sub(tx('personaSub')),
    card(head, area,
      h('div', { class: 'ps-foot' }, h('span', { class: 'desc', id: 'persona-hint', text: tx('personaHint') }), count),
      actions,
      status),
    section(tx('personaFixed')),
    group(
      rule('personaRuleDiagnose', 'personaRuleDiagnoseHint'),
      rule('personaRuleDistress', 'personaRuleDistressHint'),
      rule('personaRuleSelf', 'personaRuleSelfHint'),
      rule('personaRuleClaude', 'personaRuleClaudeHint'),
      note(tx('personaFixedSub'))),
  ];
}
