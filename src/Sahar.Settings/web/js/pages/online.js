// «العقل أونلاين» (0.17 spec §9): which brain answers in the chat, «محلي فقط», the one-time consent, the providers in
// chain order (model, key — sealed by her Mind, never shown back —, «جرّب المفتاح», today's requests, up/down), and
// «جرّب عقلي» with a one-click way back to local. Text via textContent only; the key field is cleared once sent.
import { h, title, sub, section, group, switchRow, note, ghost, pri } from '../controls.js';
import { getLang } from '../i18n.js';
import { toArabicIndic } from '../digits.js';
import { card, meta, item, chip, chips } from '../mind-ui.js';
import { TEST_LINES } from '../strings/mind-online.js';
import { startPoll, stopPoll } from '../poll.js';

let gen = 0;
export function leave() {
  gen++;
  stopPoll('online');
}

const num = (n) => (getLang() === 'ar' ? toArabicIndic(String(n)) : String(n));
const NAMES = { groq: 'Groq', openrouter: 'OpenRouter' };
const line = (code) => { const t = TEST_LINES[getLang() === 'ar' ? 'ar' : 'en']; return t[code] ?? t.other; };

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
  const status = card(h('div', { class: 'm-text' }, nowLine));

  const local = switchRow(tx('onLocalOnly'), tx('onLocalOnlyHint'), !!v.mindLocalOnly, async (on) => { await set('mindLocalOnly', on); rerender(); }, 'mindLocalOnly');
  local.id = 'on-localonly';
  const top = [title(tx('pageOnline')), sub(tx('onSub')), status, group(local)];
  if (v.mindLocalOnly) return top;

  if (!v.mindOnlineConsent) {
    const ok = pri(tx('onConsentOk'), async () => { await set('mindOnlineConsent', true); rerender(); }, { id: 'on-consent-ok' });
    const consent = card(h('div', { class: 'm-text' },
      h('span', { class: 'label', text: tx('onConsentTitle') }),
      h('p', { class: 'desc', text: tx('onConsentSent') }),
      h('p', { class: 'desc', text: tx('onConsentNever') }),
      h('p', { class: 'desc', text: tx('onConsentTrain') })), h('div', { class: 'btn-row' }, ok));
    consent.id = 'on-consent';
    return [...top, consent];
  }

  // ---- the providers, in his order ----
  const chain = (v.mindOnlineChain && v.mindOnlineChain.length ? v.mindOnlineChain : ['groq', 'openrouter', 'custom']).filter((x, i, a) => a.indexOf(x) === i);
  for (const id of ['groq', 'openrouter', 'custom']) if (!chain.includes(id)) chain.push(id);
  const rows = new Map();
  const list = h('div', { id: 'on-providers' });

  function quotaText(p) {
    if (p.exhausted) return tx('onOut');
    if (!p.used && !p.limit) return '';
    return p.limit ? tx('onToday', { used: num(p.used || 0), limit: num(p.limit) }) : tx('onTodayCount', { used: num(p.used || 0) });
  }

  function row(id, i) {
    const p = prov(id);
    const name = id === 'custom' ? tx('onCustom') : NAMES[id];
    const result = h('span', { class: 'desc', id: `on-result-${id}`, 'aria-live': 'polite' });
    const quota = meta(quotaText(p), { id: `on-quota-${id}` });
    const keyState = meta(p.key ? tx('onKeySaved') : tx('onKeyNone'), { id: `on-keystate-${id}` });
    const keyBox = h('input', { class: 'input', id: `on-key-${id}`, type: 'password', placeholder: tx('onKeyPlaceholder'), 'aria-label': `${tx('onKey')} ${name}`,
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
    const up = ghost(tx('onUp'), move(-1), { id: `on-up-${id}`, 'aria-label': `${tx('onUp')} ${name}` });
    const down = ghost(tx('onDown'), move(1), { id: `on-down-${id}`, 'aria-label': `${tx('onDown')} ${name}` });
    up.hidden = i === 0;
    down.hidden = i === chain.length - 1;

    // the model: suggestions + «غيره…» (any id the provider has)
    const key = id === 'groq' ? 'mindGroqModel' : id === 'openrouter' ? 'mindOpenRouterModel' : 'mindCustomModel';
    const models = (st.models && st.models[id]) || [];
    const current = v[key] || models[0] || '';
    const modelText = h('input', { class: 'input', id: `on-modeltext-${id}`, value: current, dir: 'ltr', spellcheck: 'false', autocomplete: 'off', 'aria-label': tx('onModel') });
    modelText.addEventListener('change', async () => { const r = await set(key, modelText.value.trim()); if (!r.ok) modelText.value = v[key] || current; });
    let modelPick = null;
    if (models.length) {
      modelPick = h('select', { class: 'input', id: `on-model-${id}`, 'aria-label': tx('onModel') },
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
      url = h('input', { class: 'input', id: 'on-url-custom', value: v.mindCustomUrl || '', placeholder: tx('onUrlHint'), dir: 'ltr', spellcheck: 'false', autocomplete: 'off', 'aria-label': tx('onUrl') });
      url.addEventListener('change', async () => { const r = await set('mindCustomUrl', url.value.trim()); result.textContent = r.ok ? '' : tx('onBadUrl'); });
    }
    const el = h('div', { class: 'm-card on-row', id: `on-row-${id}`, 'data-provider': id },
      item({ line: name, under: [chips(chip(num(i + 1), 'accent')), keyState, quota], end: [h('span', { class: 'btn-row' }, up, down)] }),
      url ? h('div', { class: 'row' }, h('span', { class: 'label-col' }, h('span', { class: 'label', text: tx('onUrl') })), url) : null,
      h('div', { class: 'row' }, h('span', { class: 'label-col' }, h('span', { class: 'label', text: tx('onModel') })), modelPick, modelText),
      h('div', { class: 'row' }, h('span', { class: 'label-col' }, h('span', { class: 'label', text: tx('onKey') })), keyBox, h('span', { class: 'btn-row' }, save, test, forget)),
      result);
    rows.set(id, { quota, keyState, result, test, forget });
    return el;
  }
  chain.forEach((id, i) => list.append(row(id, i)));

  // ---- «جرّب عقلي» ----
  const benchOut = h('p', { class: 'desc', id: 'on-bench-result', 'aria-live': 'polite' });
  const benchBtn = ghost(tx('onBenchRun'), async () => {
    benchOut.textContent = tx('onBenchRunning');
    const r = await ask('mind-online-bench');
    if (!r.ok) { benchOut.textContent = tx('onEngineOff'); return; }
    waitBench = true;
  }, { id: 'on-bench' });
  const back = ghost(tx('onBackLocal'), async () => { await set('mindChatBrain', 'local'); back.textContent = tx('onBackDone'); drawNow(); }, { id: 'on-back-local' });
  const benchCard = card(h('div', { class: 'm-text' }, h('p', { class: 'desc', text: tx('onBenchHint') }), benchOut), h('div', { class: 'btn-row' }, benchBtn, back));
  benchCard.hidden = !usable();

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
    }
    benchCard.hidden = !(!S.value.mindLocalOnly && S.value.mindOnlineConsent && (i?.providers || []).some((p) => p.key));
  }
  if (myGen === gen) startPoll('online', refresh);

  return [...top,
    section(tx('onGroupProviders')),
    note(tx('onProvidersNote')),
    list,
    note(tx('onKeyNote')),
    section(tx('onGroupBench')),
    benchCard];
}
