// «العقل أونلاين» (0.17 spec §9): which brain answers in the chat, «محلي فقط», the one-time consent, the providers in
// chain order (model, key — sealed by her Mind, never shown back —, «جرّب المفتاح», today's requests, up/down), and
// «جرّب عقلي» with a one-click way back to local. Text via textContent only; the key field is cleared once sent.
// 0.17.1 layout (owner: «كتير متداخل»): the page's blocks spaced as the other pages, a provider is one header (order,
// name, key chip, up/down icons) over its fields in a label/field grid, ids/URLs/keys left-to-right, «مخصّص» folded.
import { h, title, sub, section, group, switchRow, note, ghost, pri } from '../controls.js';
import { getLang } from '../i18n.js';
import { toArabicIndic } from '../digits.js';
import { icon } from '../icons.js';
import { card, meta, chip, hand } from '../mind-ui.js';
import { TEST_LINES } from '../strings/mind-online.js';
import { startPoll, stopPoll } from '../poll.js';

let gen = 0;
let customOpen = null;   // «مزوّد مخصّص (متقدّم)» unfolded by him (kept across a redraw); null: folded unless set up
export function leave() {
  gen++;
  stopPoll('online');
}

const num = (n) => (getLang() === 'ar' ? toArabicIndic(String(n)) : String(n));
const NAMES = { groq: 'Groq', openrouter: 'OpenRouter' };
const line = (code) => { const t = TEST_LINES[getLang() === 'ar' ? 'ar' : 'en']; return t[code] ?? t.other; };
const CHEVRON_UP = 'M6.5 14.5L12 9l5.5 5.5', CHEVRON_DOWN = 'M6.5 9.5L12 15l5.5-5.5';
/** A label on the start side of the provider's field grid, and its field(s). */
const field = (labelText, forId, ...controls) => [
  h('label', { class: 'label on-flabel', for: forId, text: labelText }),
  h('div', { class: 'on-fctl' }, ...controls)];

export async function render({ S, tx, set, ask, render: rerender }) {
  leave();
  const myGen = gen;
  const v = S.value;
  let st = await ask('mind-online-status', { refresh: 'true' });
  const info = () => (st.ok && st.info ? st.info : null);
  const prov = (id) => (info()?.providers || []).find((p) => p.id === id) || { id, key: (v.mindOnlineKeys || []).includes(id), used: 0 };
  const usable = () => !v.mindLocalOnly && v.mindOnlineConsent && (v.mindOnlineKeys || []).length > 0;

  const nowLine = h('span', { class: 'mr-status-line', id: 'on-now' });
  const drawNow = () => { nowLine.textContent = !usable() ? tx('onNowOff') : v.mindChatBrain === 'local' ? tx('onNowLocal') : tx('onNowOnline'); };
  drawNow();
  const status = card(h('div', { class: 'mr-status' }, h('span', { class: 'm-icon on-globe', 'aria-hidden': 'true' }, icon(document, 'globe-simple', 20)),
    h('div', { class: 'm-text' }, nowLine)));
  status.classList.add('on-status');

  const local = switchRow(tx('onLocalOnly'), tx('onLocalOnlyHint'), !!v.mindLocalOnly, async (on) => { await set('mindLocalOnly', on); rerender(); }, 'mindLocalOnly');
  local.id = 'on-localonly';
  const localGroup = group(local);
  localGroup.classList.add('on-local');
  const top = [title(tx('pageOnline')), sub(tx('onSub')), status, localGroup];
  if (v.mindLocalOnly) return top;

  if (!v.mindOnlineConsent) {
    const ok = pri(tx('onConsentOk'), async () => { await set('mindOnlineConsent', true); rerender(); }, { id: 'on-consent-ok' });
    const consent = card(
      h('p', { class: 'on-consent-line', text: tx('onConsentSent') }),
      h('p', { class: 'on-consent-line', text: tx('onConsentNever') }),
      h('p', { class: 'on-consent-line', text: tx('onConsentTrain') }),
      h('div', { class: 'btn-row on-consent-actions' }, ok));
    consent.id = 'on-consent';
    consent.setAttribute('aria-labelledby', 'on-consent-title');
    return [...top, section(tx('onConsentTitle'), 'on-consent-title'), consent];
  }

  // ---- the providers, in his order ----
  const chain = (v.mindOnlineChain && v.mindOnlineChain.length ? v.mindOnlineChain : ['groq', 'openrouter', 'custom']).filter((x, i, a) => a.indexOf(x) === i);
  for (const id of ['groq', 'openrouter', 'custom']) if (!chain.includes(id)) chain.push(id);
  const rows = new Map();
  const list = h('div', { id: 'on-providers', class: 'on-list' });

  function quotaText(p) {
    if (p.exhausted) return tx('onOut');
    if (!p.used && !p.limit) return '';
    return p.limit ? tx('onToday', { used: num(p.used || 0), limit: num(p.limit) }) : tx('onTodayCount', { used: num(p.used || 0) });
  }

  function row(id, i) {
    const p = prov(id);
    const name = id === 'custom' ? tx('onCustom') : NAMES[id];
    const result = h('span', { class: 'desc', id: `on-result-${id}`, 'aria-live': 'polite' });
    const quota = meta(quotaText(p), { id: `on-quota-${id}`, class: 'm-meta on-quota' });
    const keyState = chip(p.key ? tx('onKeySaved') : tx('onKeyNone'), p.key ? 'accent' : null);
    keyState.id = `on-keystate-${id}`;
    const keyBox = h('input', { class: 'input on-ltr on-ar-ph', id: `on-key-${id}`, type: 'password', placeholder: tx('onKeyPlaceholder'), 'aria-label': `${tx('onKey')} ${name}`,
      autocomplete: 'off', spellcheck: 'false', dir: 'ltr' });
    const save = ghost(tx('onKeySave'), async () => {
      const key = keyBox.value.trim();
      keyBox.value = '';   // never kept in the page once sent
      if (!key) return;
      result.textContent = tx('onTesting');
      const r = await ask('mind-online-key', { provider: id, key });
      if (!r.ok) { result.textContent = r.error === 'engine-off' ? tx('onEngineOff') : line('key-bad'); return; }
      waitTest = id;
    }, { id: `on-save-${id}` });
    const test = ghost(tx('onKeyTest'), async () => {
      result.textContent = tx('onTesting');
      const r = await ask('mind-online-test', { provider: id });
      if (!r.ok) { result.textContent = tx('onEngineOff'); return; }
      waitTest = id;
    }, { id: `on-test-${id}` });
    const forget = ghost(tx('onKeyForget'), async () => { await ask('mind-online-forget', { provider: id }); result.textContent = ''; }, { id: `on-forget-${id}` });
    test.hidden = !p.key;
    forget.hidden = !p.key;
    const move = (d) => async () => {
      const c = chain.slice();
      const j = i + d;
      if (j < 0 || j >= c.length) return;
      [c[i], c[j]] = [c[j], c[i]];
      await set('mindOnlineChain', c);
      rerender();
    };
    const moveBtn = (d, label, path) => h('button', { class: 'm-icon-btn hv on-move-btn', type: 'button', id: `on-${d < 0 ? 'up' : 'down'}-${id}`,
      'aria-label': `${label} ${name}`, title: label, onclick: move(d) }, hand(path, 16));
    const up = moveBtn(-1, tx('onUp'), CHEVRON_UP);
    const down = moveBtn(1, tx('onDown'), CHEVRON_DOWN);
    up.hidden = i === 0;
    down.hidden = i === chain.length - 1;

    // the model: suggestions + «غيره…» (any id the provider has)
    const key = id === 'groq' ? 'mindGroqModel' : id === 'openrouter' ? 'mindOpenRouterModel' : 'mindCustomModel';
    const models = (st.models && st.models[id]) || [];
    const current = v[key] || models[0] || '';
    const modelText = h('input', { class: 'input on-ltr', id: `on-modeltext-${id}`, value: current, dir: 'ltr', spellcheck: 'false', autocomplete: 'off', 'aria-label': tx('onModel') });
    modelText.addEventListener('change', async () => { const r = await set(key, modelText.value.trim()); if (!r.ok) modelText.value = v[key] || current; });
    let modelPick = null;
    if (models.length) {
      modelPick = h('select', { class: 'input on-ltr', id: `on-model-${id}`, 'aria-label': tx('onModel'), dir: 'ltr' },
        ...models.map((m) => h('option', { value: m, text: m })), h('option', { value: '', text: tx('onModelOther') }));
      modelPick.value = models.includes(current) ? current : '';
      modelText.hidden = models.includes(current);
      modelPick.addEventListener('change', async () => {
        if (!modelPick.value) { modelText.hidden = false; modelText.focus(); return; }
        modelText.hidden = true;
        modelText.value = modelPick.value;
        await set(key, modelPick.value);
      });
    }
    let url = null;
    if (id === 'custom') {
      url = h('input', { class: 'input on-ltr', id: 'on-url-custom', type: 'url', value: v.mindCustomUrl || '', placeholder: 'https://…/v1', dir: 'ltr', spellcheck: 'false', autocomplete: 'off',
        'aria-label': tx('onUrl'), 'aria-describedby': 'on-url-hint' });
      url.addEventListener('change', async () => { const r = await set('mindCustomUrl', url.value.trim()); result.textContent = r.ok ? '' : tx('onBadUrl'); });
    }
    const fields = h('div', { class: 'on-fields' },
      url ? field(tx('onUrl'), url.id, url, h('span', { class: 'desc', id: 'on-url-hint', text: tx('onUrlHint') })) : null,
      field(tx('onModel'), (modelPick || modelText).id, modelPick, modelText),
      field(tx('onKey'), keyBox.id, h('div', { class: 'on-keyline' }, keyBox, h('span', { class: 'btn-row' }, save, test, forget))));
    result.classList.add('on-result');

    // The header: order, name, the key's chip, and up/down (each in its own column, so a hidden one moves nothing).
    let nameEl = h('span', { class: 'label on-name', text: name });
    let body = h('div', { class: 'on-body' }, fields, result);
    if (id === 'custom') {
      // An advanced feature: folded behind «مزوّد مخصّص (متقدّم)» unless he opened it or set it up.
      const open = customOpen ?? !!(p.key || v.mindCustomUrl);
      body.id = 'on-custom-body';
      body.hidden = !open;
      nameEl = h('button', { class: 'on-disclose hv', type: 'button', id: 'on-custom-toggle', 'aria-expanded': open ? 'true' : 'false', 'aria-controls': body.id },
        h('span', { class: 'label', text: tx('onCustomAdvanced') }), h('span', { class: 'on-chev', 'aria-hidden': 'true' }, hand(CHEVRON_DOWN, 16)));
      nameEl.addEventListener('click', () => {
        const now = nameEl.getAttribute('aria-expanded') !== 'true';
        customOpen = now;
        nameEl.setAttribute('aria-expanded', now ? 'true' : 'false');
        body.hidden = !now;
      });
    }
    const head = h('div', { class: 'on-head' },
      h('span', { class: 'on-num tnum', text: num(i + 1) }),
      h('span', { class: 'on-title' }, nameEl, keyState),
      h('span', { class: 'on-move' }, up, down));
    const el = h('section', { class: 'm-card on-row', id: `on-row-${id}`, 'data-provider': id, 'aria-label': `${num(i + 1)}. ${name}` },
      h('div', { class: 'on-headwrap' }, head, quota), body);
    rows.set(id, { quota, keyState, result, test, forget });
    return el;
  }
  chain.forEach((id, i) => list.append(row(id, i)));

  // ---- «جرّب عقلي» ----
  const benchOut = h('p', { class: 'desc on-result', id: 'on-bench-result', 'aria-live': 'polite' });
  const benchBtn = ghost(tx('onBenchRun'), async () => {
    if (waitBench) return;   // review: one bench at a time (each costs today's requests)
    benchBtn.disabled = true;
    benchOut.textContent = tx('onBenchRunning');
    const r = await ask('mind-online-bench');
    if (!r.ok) { benchOut.textContent = tx('onEngineOff'); benchBtn.disabled = false; return; }
    waitBench = true;
  }, { id: 'on-bench' });
  const back = ghost(tx('onBackLocal'), async () => { await set('mindChatBrain', 'local'); back.textContent = tx('onBackDone'); drawNow(); }, { id: 'on-back-local' });
  const benchCard = card(h('p', { class: 'on-consent-line', text: tx('onBenchHint') }), benchOut, h('div', { class: 'btn-row on-consent-actions' }, benchBtn, back));
  benchCard.id = 'on-bench-card';
  // review 0.17.1: the heading goes with its card (no «جرّب عقلي» over nothing before a key is saved).
  const benchBlock = h('div', { id: 'on-bench-block' }, section(tx('onGroupBench')), benchCard);
  benchBlock.hidden = !usable();

  // ---- the poll: the Mind's status (keys, quota, the last test, the bench) ----
  let waitTest = null, waitBench = false, seq = st.seq;
  async function refresh() {
    if (myGen !== gen) return;
    st = await ask('mind-online-status');
    if (myGen !== gen || !st.ok) return;
    const fresh = st.seq !== seq;
    seq = st.seq;
    for (const [id, r] of rows) {
      const p = prov(id);
      r.quota.textContent = quotaText(p);
      r.keyState.textContent = p.key ? tx('onKeySaved') : tx('onKeyNone');
      r.keyState.classList.toggle('accent', !!p.key);
      r.test.hidden = !p.key;
      r.forget.hidden = !p.key;
    }
    const i = info();
    if (fresh && waitTest && i && i.testProvider === waitTest && i.test) { rows.get(waitTest).result.textContent = line(i.test); waitTest = null; }
    if (fresh && waitBench && i && i.bench) {
      const b = i.bench;
      const ms = b.firstMs && b.firstMs.length ? Math.min(...b.firstMs) : 0;
      benchOut.textContent = b.ok ? tx('onBenchResult', { ok: num(b.ok), total: num(b.total), ms: num(ms) }) + (b.sample ? '\n«' + b.sample + '»' : '') : tx('onBenchFailed', { why: line(b.error) });
      waitBench = false;
      benchBtn.disabled = false;
    }
    benchBlock.hidden = !(!S.value.mindLocalOnly && S.value.mindOnlineConsent && (i?.providers || []).some((p) => p.key));
  }
  if (myGen === gen) startPoll('online', refresh);

  return [...top,
    section(tx('onGroupProviders')),
    note(tx('onProvidersNote')),
    list,
    note(tx('onKeyNote')),
    benchBlock];
}
