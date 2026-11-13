// «شو فهمت عنك» (plan 0.14 Task B4; mockup PSettingsYou, docs/design/brain/30-*): what she proposes (صح / عدّلي / لا),
// what he confirmed (edit, delete), how he talks (import a chat he previews first, examples he writes), what she
// remembers and for how long, and «انسي كل شي». Every source shows its kind and date, never his words from it.
// Its words live in strings/mind-memory.js; the shared parts in mind-ui.js; its own rules in css/mind-memory.css.
import { h, title, sub, section, group, row, segmented, switchRow, note, pri, ghost, dng, select } from '../controls.js';
import { chip, chips, meta, hand, HAND } from '../mind-ui.js';
import { toArabicIndic } from '../digits.js';
import { t, getLang } from '../i18n.js';
import '../strings/mind-memory.js';

const KEEP = [7, 30, 90];
const CATS = ['likes', 'underPressure', 'whatHelps', 'routines', 'values', 'style', 'people', 'goals'];
const CONF = { low: 0, medium: 1, high: 2 };
const MAX_FILE = 8 * 1024 * 1024;
const WEEK = 7 * 24 * 3600 * 1000;

// What stays across redraws of the page (a chat being previewed, an open editor).
const state = { editing: null, imp: null, examples: null, status: { import: '', examples: '' } };

export function leave() {
  state.editing = null;
  state.examples = null;
}

const num = (s) => (getLang() === 'ar' ? toArabicIndic(String(s)) : String(s));

export async function render({ S, tx, set, ask, render: rerender }) {
  const r = await ask('mind-profile');
  const data = r.ok ? r : { proposed: [], confirmed: [], style: { summary: '', samples: 0, fromImports: 0, fromExamples: 0 } };
  const redraw = () => rerender(true);

  function date(iso) {
    const d = new Date(iso);
    const months = t('youMonths');
    return getLang() === 'ar' ? `${num(d.getDate())} ${months[d.getMonth()]}` : `${months[d.getMonth()]} ${d.getDate()}`;
  }
  const kindText = (k) => tx({ chats: 'youSrcChats', imports: 'youSrcImports', examples: 'youSrcExamples', music: 'youSrcMusic' }[k] || 'youSrcChats');
  function mainKind(item) {
    const n = {};
    for (const s of item.sources) n[s.kind] = (n[s.kind] || 0) + 1;
    return Object.entries(n).sort((a, b) => b[1] - a[1])[0]?.[0] || 'chats';
  }
  /** «من حكينا · ٣ مرات هالأسبوع» (proposed). */
  function howOften(item) {
    const n = item.sources.length;
    const times = n === 1 ? tx('youOnce') : n === 2 ? tx('youTwice') : tx('youTimes', { n: num(n) });
    const week = n > 1 && item.sources.every((s) => Date.now() - new Date(s.at).getTime() <= WEEK);
    return `${kindText(mainKind(item))} · ${times}${week ? ' ' + tx('youThisWeek') : ''}`;
  }
  /** «من حكينا · ٢ تشرين التاني» (confirmed). */
  function when(item) {
    const last = item.sources.length ? item.sources[item.sources.length - 1].at : item.createdAt;
    return `${kindText(mainKind(item))} · ${date(last)}`;
  }

  async function act(type, body) {
    const res = await ask(type, body);
    state.editing = null;
    redraw();
    return res;
  }

  function editor(item, idPrefix) {
    const field = h('input', { class: 'input mm-field', id: `${idPrefix}-field`, type: 'text', value: item.text, 'aria-label': tx('youEditLabel'), autocomplete: 'off', spellcheck: 'false' });
    const save = () => field.value.trim() && act('mind-profile-edit', { id: item.id, text: field.value.trim() });
    field.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') save();
      if (e.key === 'Escape') { state.editing = null; redraw(); }
    });
    queueMicrotask(() => field.focus());
    return h('span', { class: 'mm-edit' }, field,
      h('span', { class: 'btn-row' },
        pri(tx('youSave'), save, { id: `${idPrefix}-save` }),
        ghost(tx('youCancel'), () => { state.editing = null; redraw(); }, { id: `${idPrefix}-cancel` })));
  }

  // ---- لاحظت… صح؟ ----
  const proposed = h('div', { class: 'mm-cards', id: 'you-proposed' });
  if (!data.proposed.length) proposed.append(h('p', { class: 'mm-empty', id: 'you-proposed-none', text: tx('youNoticedNone') }));
  data.proposed.forEach((p, i) => {
    const id = `prop-${i}`;
    const privateChip = p.sensitive !== 'none' ? chip('🔒 ' + tx('youPrivate'), 'moon') : null;
    const body = state.editing === p.id
      ? [editor(p, id)]
      : [
        h('span', { class: 'mm-soft-text', text: p.text }),
        chips(meta(howOften(p)), chip(tx('youConf')[CONF[p.confidence] ?? 0]), privateChip),
        p.sensitive !== 'none' ? meta(tx('youPrivateAsk')) : null,
        h('span', { class: 'btn-row' },
          pri(tx('youYes'), () => act('mind-profile-confirm', { id: p.id }), { id: `${id}-yes` }),
          ghost(tx('youEdit'), () => { state.editing = p.id; redraw(); }, { id: `${id}-edit` }),
          ghost(tx('youNo'), () => act('mind-profile-reject', { id: p.id }), { id: `${id}-no` })),
      ];
    proposed.append(h('div', { class: 'm-soft', id, 'data-id': p.id }, ...body));
  });

  // ---- اللي أكّدتلي ياه ----
  const confirmed = group();
  confirmed.id = 'you-confirmed';
  if (!data.confirmed.length) confirmed.append(note(tx('youConfirmedNone')));
  data.confirmed.forEach((c, i) => {
    const id = `conf-${i}`;
    if (state.editing === c.id) {
      confirmed.append(h('div', { class: 'm-item', id, 'data-id': c.id }, editor(c, id)));
      return;
    }
    const metaLine = c.sensitive !== 'none' && c.confirmedAt ? `${when(c)} · ${tx('youAgreed', { date: date(c.confirmedAt) })}` : when(c);
    confirmed.append(h('div', { class: 'm-item', id, 'data-id': c.id },
      h('span', { class: 'm-text' },
        h('span', { class: 'label', text: c.text }),
        chips(chip(tx('youCats')[CATS.indexOf(c.category)] ?? '', 'accent'), c.sensitive !== 'none' ? chip('🔒 ' + tx('youPrivate'), 'moon') : null, meta(metaLine))),
      h('button', { class: 'm-icon-btn hv', type: 'button', id: `${id}-edit`, 'aria-label': tx('youEdit'), onclick: () => { state.editing = c.id; redraw(); } }, hand(HAND.pencil)),
      h('button', { class: 'm-icon-btn hv', type: 'button', id: `${id}-del`, 'aria-label': tx('youDelete'), onclick: () => act('mind-profile-delete', { id: c.id }) }, hand(HAND.trash))));
  });

  // ---- طريقتك بالحكي ----
  const st = data.style;
  const styleRow = h('div', { class: 'm-item', id: 'style-card' },
    h('span', { class: 'm-text' },
      h('span', { class: 'label', id: 'style-summary', text: st.summary || tx('youStyleNone') }),
      meta(st.samples ? tx('youStyleMeta', { n: num(st.samples), imports: num(st.fromImports), examples: num(st.fromExamples) }) : tx('youStyleNoneMeta'), { id: 'style-meta' })));

  // A consent question in place, then the save again (owner: each source opt-in, his own yes).
  function consentAsk(id, textKey, key, retry) {
    return h('div', { class: 'mm-ask', id, role: 'group' },
      h('span', { class: 'desc', text: tx(textKey) }),
      h('span', { class: 'btn-row' },
        pri(tx('youConsentYes'), async () => { const s = await set(key, true); if (s.ok) await retry(); }, { id: `${id}-yes` }),
        ghost(tx('youCancel'), () => { state.imp = state.imp && { ...state.imp, consent: false }; state.examples = state.examples && { ...state.examples, consent: false }; redraw(); }, { id: `${id}-no` })));
  }

  // Import: a file he picks, previewed (who he is, a few of his lines scrubbed), saved only on his «احفظيهن».
  const fileInput = h('input', { type: 'file', id: 'import-file', accept: '.txt,.json,text/plain,application/json', hidden: true });
  fileInput.addEventListener('change', async () => {
    const f = fileInput.files?.[0];
    fileInput.value = '';
    if (!f) return;
    if (f.size > MAX_FILE) { state.imp = null; state.status.import = tx('youImportTooBig'); redraw(); return; }
    await preview(await f.text(), f.name);
  });
  async function preview(content, name, me) {
    const p = await ask('mind-import-preview', { content, name, me: me || undefined });
    state.status.import = '';
    if (!p.ok) {
      state.imp = null;
      state.status.import = p.error === 'too-big' ? tx('youImportTooBig') : p.error === 'not-a-chat' ? tx('youImportNotChat') : tx('youFailed');
    } else state.imp = { content, name, me: p.me, senders: p.senders, kept: p.kept, sample: p.sample, consent: false };
    redraw();
  }
  async function saveImport() {
    const imp = state.imp;
    if (!imp?.me) return;
    const res = await ask('mind-import-save', { content: imp.content, name: imp.name, me: imp.me });
    if (!res.ok && res.error === 'no-consent') { state.imp = { ...imp, consent: true }; redraw(); return; }
    state.imp = null;
    state.status.import = res.ok ? tx('youImportSaved', { n: num(res.kept) }) : tx('youFailed');
    redraw();
  }
  if (window.sahar) window.sahar.importText = (content, name) => preview(content, name || 'chat.txt');   // the UI test's way in (no file dialog)

  const importRow = row(tx('youImport'),
    ghost(tx('youImportPick'), () => fileInput.click(), { id: 'import-pick' }),
    { desc: state.status.import || tx('youImportHint') });
  importRow.id = 'import-row';
  importRow.querySelector('.desc').id = 'import-status';
  importRow.querySelector('.desc').setAttribute('aria-live', 'polite');
  let importPanel = null;
  if (state.imp) {
    const imp = state.imp;
    const who = select('import-me', [['', tx('youImportWhoNone')], ...imp.senders.map((s) => [s.name, `${s.name} (${num(s.count)})`])], imp.me || '',
      (v) => preview(imp.content, imp.name, v));
    importPanel = h('div', { class: 'mm-panel', id: 'import-preview' },
      row(tx('youImportWho'), who, { forId: 'import-me' }),
      imp.me ? h('p', { class: 'desc mm-pad', id: 'import-kept', text: imp.kept ? tx('youImportKept', { n: num(imp.kept) }) : tx('youImportNoLines') }) : null,
      imp.me && imp.sample.length ? h('ul', { class: 'mm-sample', id: 'import-sample' }, ...imp.sample.map((l) => h('li', { text: l, dir: 'auto' }))) : null,
      imp.consent ? consentAsk('import-consent', 'youImportConsent', 'mindLearnImports', saveImport) : null,
      h('span', { class: 'btn-row mm-pad' },
        pri(tx('youImportSave'), saveImport, { id: 'import-save', disabled: !imp.me || !imp.kept }),
        ghost(tx('youCancel'), () => { state.imp = null; redraw(); }, { id: 'import-cancel' })));
  }

  // Examples he writes, one per line.
  async function saveExamples() {
    const text = document.getElementById('examples-text')?.value ?? state.examples?.text ?? '';
    state.examples = { ...(state.examples || {}), text };
    if (!text.trim()) return;
    const res = await ask('mind-style-examples', { text });
    if (!res.ok && res.error === 'no-consent') { state.examples = { text, consent: true }; redraw(); return; }
    state.examples = null;
    state.status.examples = res.ok ? tx('youExamplesSaved', { n: num(res.kept) }) : tx('youFailed');
    redraw();
  }
  const examplesRow = row(tx('youExamples'),
    ghost(tx('youExamplesWrite'), () => { state.examples = { text: '' }; state.status.examples = ''; redraw(); }, { id: 'examples-open' }),
    { desc: state.status.examples || tx('youExamplesHint') });
  examplesRow.id = 'examples-row';
  examplesRow.querySelector('.desc').id = 'examples-status';
  let examplesPanel = null;
  if (state.examples) {
    const area = h('textarea', { class: 'input mm-area', id: 'examples-text', rows: '4', placeholder: tx('youExamplesPlaceholder'), 'aria-label': tx('youExamples'), dir: 'auto' });
    area.value = state.examples.text || '';
    area.addEventListener('input', () => { state.examples.text = area.value; });
    if (!state.examples.consent) queueMicrotask(() => area.focus());
    examplesPanel = h('div', { class: 'mm-panel', id: 'examples-panel' }, h('div', { class: 'mm-pad' }, area),
      state.examples.consent ? consentAsk('examples-consent', 'youExamplesConsent', 'mindLearnExamples', saveExamples) : null,
      h('span', { class: 'btn-row mm-pad' },
        pri(tx('youSave'), saveExamples, { id: 'examples-save' }),
        ghost(tx('youCancel'), () => { state.examples = null; redraw(); }, { id: 'examples-cancel' })));
  }

  // ---- شو بتذكّر ----
  const remember = switchRow(tx('youRememberChats'), tx('youRememberHint'), !!S.value.mindLearnChats, async (on, btn) => {
    const s = await set('mindLearnChats', on);
    if (!s.ok) btn.set(!on);
  }, 'mindLearnChats');
  remember.id = 'sw-remember';
  const keepIndex = Math.max(0, KEEP.indexOf(Number(S.value.mindEpisodeDays ?? 30)));
  const keep = segmented('keep-label', tx('youKeepDays'), keepIndex === -1 ? 1 : keepIndex, (i) => set('mindEpisodeDays', KEEP[i]), 'keep');
  const keepRow = row(tx('youKeep'), keep, { desc: tx('youKeepHint'), labelId: 'keep-label' });

  // ---- من الأول: «انسي كل شي» with «أكيد؟» in place ----
  const forget = h('div', { id: 'forget' });
  let forgetStatus = tx('youForgetHint');
  function forgetAsk() {
    forget.replaceChildren(h('div', { class: 'row' },
      h('span', { class: 'label-col' }, h('span', { class: 'm-danger-label', text: tx('youForget') }),
        h('span', { class: 'desc', id: 'forget-status', 'aria-live': 'polite', text: forgetStatus })),
      dng(tx('youForgetBtn'), forgetConfirm, { id: 'forget-ask' })));
  }
  function forgetConfirm() {
    const yes = dng(tx('youForgetBtn'), async () => {
      yes.disabled = true;
      const res = await ask('mind-forget', {});
      forgetStatus = res.ok ? tx('youForgetDone') : tx('youForgetFailed');
      state.imp = null;
      state.examples = null;
      if (res.ok) { await redraw(); document.getElementById('forget-status')?.replaceChildren(forgetStatus); }
      else forgetAsk();
      document.getElementById('forget-ask')?.focus();
    }, { id: 'forget-yes' });
    forget.replaceChildren(h('div', { class: 'row', role: 'group', 'aria-label': tx('youForget') },
      h('span', { class: 'label-col' }, h('span', { class: 'label', text: tx('confirmTitle') }), h('span', { class: 'desc pr', text: tx('youForgetConfirm') })),
      h('span', { class: 'btn-row' }, yes, ghost(tx('no'), () => { forgetAsk(); document.getElementById('forget-ask')?.focus(); }, { id: 'forget-no' }))));
    yes.focus();
  }
  forgetAsk();

  return [
    title(tx('youTitle')),
    sub(tx('youSub')),
    section(tx('youNoticed'), 'you-noticed-title'),
    proposed,
    section(tx('youConfirmed')),
    confirmed,
    section(tx('youStyle')),
    group(styleRow, h('div', { class: 'm-sep' }), importRow, fileInput, importPanel, examplesRow, examplesPanel),
    section(tx('youRemember')),
    group(remember, keepRow, h('p', { class: 'note pr', id: 'you-lock-note', text: tx('youLockNote') })),
    section(tx('youFresh')),
    group(forget),
  ];
}
