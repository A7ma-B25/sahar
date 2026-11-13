// «النماذج» (0.14 stream A; mockups PSettingsModels, PSettingsModelsBlocked): the download (reachability first, plain
// words + the VPN hint when Hugging Face is blocked, pause/resume, SHA-256), the recommended models, «زيدي نموذج»
// (a Hugging Face link or name, or a local .gguf), and his models with their benchmark and the «حكي بس» badge.
import { h, title, sub, section, group, note, ghost, pri, input } from '../controls.js';
import { getLang } from '../i18n.js';
import { toArabicIndic } from '../digits.js';
import { HAND, chip, chips, card, meta, item, bar, stats, iconBox } from '../mind-ui.js';
import '../strings/mind-runtime.js';

let timer = null;
export function leave() {
  if (timer) clearInterval(timer);
  timer = null;
}

const num = (n) => (getLang() === 'ar' ? toArabicIndic(String(n)).replace('.', '٫') : String(n));
const gb = (bytes) => num(Math.round(bytes / 1e7) / 100);
const ROLE = { default: ['roleDefault', 'recDefault'], challenger: ['roleChallenger', 'recChallenger'], light: ['roleLight', 'recLight'],
  gpu8: ['roleGpu8', 'recGpu8'], 'light-challenger': ['roleLightChallenger', 'recLightChallenger'] };

export async function render({ tx, ask, render: rerender }) {
  leave();
  let st = await ask('mind-status');
  const list = () => (st.ok && st.models ? st.models.models : []);
  const activeId = () => (st.ok && st.models ? st.models.active : null);

  // ---- the download card (polled while the page shows) ----
  const dlTitle = h('span', { class: 'mr-dl-title', id: 'dl-title' });
  const dlMeta = meta('', { id: 'dl-meta' });
  const dlIcon = h('span', { class: 'm-icon', id: 'dl-icon' });
  const dlButtons = h('span', { class: 'btn-row', id: 'dl-buttons' });
  const dlBar = bar(0, tx('dlTitle', { p: '' }));
  dlBar.id = 'dl-bar';
  const dlNote = h('p', { class: 'desc pr', id: 'dl-note', 'aria-live': 'polite' });
  const dlCard = card(h('div', { class: 'mr-dl-head' }, dlIcon, h('span', { class: 'm-text' }, dlTitle, dlMeta), dlButtons), dlBar, dlNote);
  dlCard.id = 'dl-card';
  dlCard.setAttribute('aria-label', tx('pageModels'));
  const def = () => list().find((m) => m.role === 'default');

  async function download(op, model) {
    const r = await ask('mind-download', { op, model: model ?? st.download?.model ?? def()?.id });
    if (!r.ok) { dlNote.textContent = tx('engineOffModels'); return; }
    await refresh();
  }

  function drawDownload() {
    const d = st.ok ? st.download : null;
    const installedAny = list().some((m) => m.installed);
    const icon = (paths) => dlIcon.replaceChildren(iconBox(paths).firstChild);
    dlButtons.replaceChildren();
    dlBar.hidden = true;
    dlCard.hidden = false;
    if (d && d.model !== 'runtime-cuda' && ['downloading', 'paused', 'checking', 'verifying', 'blocked', 'failed', 'mismatch', 'no-disk', 'reachable'].includes(d.state)) {
      const name = list().find((m) => m.id === d.model)?.name ?? d.model;
      const pct = d.total ? Math.floor((d.done * 100) / d.total) : 0;
      if (d.state === 'blocked') {
        icon(HAND.blocked);
        dlTitle.textContent = tx('dlBlockedTitle');
        dlMeta.textContent = tx('dlBlockedMeta');
        dlNote.textContent = tx('dlBlockedNote');
        dlButtons.append(pri(tx('dlRetry'), () => download('start', d.model), { id: 'dl-retry' }));
        return;
      }
      icon(HAND.download);
      dlBar.hidden = false;
      dlBar.set(pct);
      dlTitle.textContent = d.state === 'downloading' ? tx('dlTitle', { p: num(pct) }) : d.state === 'checking' ? tx('dlChecking')
        : d.state === 'verifying' ? tx('dlVerifying') : tx('dlPausedTitle');
      dlMeta.textContent = tx('dlMeta', { name, done: gb(d.done), total: gb(d.total), speed: num(Math.round((d.bps || 0) / 1e5) / 10) });
      dlNote.textContent = d.state === 'mismatch' ? tx('dlMismatch') : d.state === 'no-disk' ? tx('dlNoDisk') : d.state === 'failed' ? tx('dlFailed') : tx('dlNote');
      if (d.state === 'downloading' || d.state === 'checking') dlButtons.append(ghost(tx('dlPause'), () => download('pause', d.model), { id: 'dl-pause' }));
      else if (d.state !== 'verifying') dlButtons.append(ghost(tx('dlResume'), () => download('resume', d.model), { id: 'dl-resume' }),
        ghost(tx('dlCancel'), () => download('cancel', d.model), { id: 'dl-cancel' }));
      return;
    }
    if (installedAny) { dlCard.hidden = true; return; }
    icon(HAND.bulb);
    dlTitle.textContent = tx('dlNone');
    dlMeta.textContent = tx('dlNoneMeta', { size: gb(def()?.bytes ?? 0) });
    dlNote.textContent = tx('dlNote');
    // «نزّليه» is his yes (owner answer): nothing is fetched before this press.
    dlButtons.append(pri(tx('dlAsk'), () => download('start', def()?.id), { id: 'dl-start' }));
  }

  // ---- recommended ----
  const recGroup = group();
  recGroup.id = 'rec-list';
  function drawRecommended() {
    recGroup.replaceChildren(...list().filter((m) => m.origin === 'Recommended').map((m) => {
      const [role, desc] = ROLE[m.role] ?? ['roleDefault', 'recDefault'];
      const busy = st.download && st.download.model === m.id && ['downloading', 'checking', 'verifying'].includes(st.download.state);
      return item({
        icon: HAND.bulb,
        line: chips(h('span', { class: 'label', text: m.name, dir: 'ltr' }), chip(tx(role), m.role === 'default' ? 'accent' : undefined)),
        under: [meta(`${tx('statGb', { n: gb(m.bytes) })} · ${tx(desc)}`)],
        end: m.installed ? [h('span', { class: 'desc', text: tx('recHave') })]
          : [ghost(busy ? tx('dlTitle', { p: '' }).replace(/[\s…]+$/, '…') : tx('recGet'), () => download('start', m.id), { id: `rec-${m.id}`, disabled: busy })],
      });
    }));
  }

  // ---- add ----
  const field = input({ id: 'add-query', placeholder: 'unsloth/gemma-4-E4B-it-GGUF', label: tx('addLabel') });
  field.dir = 'ltr';
  const reason = h('p', { class: 'mr-reason', id: 'add-reason', 'aria-live': 'polite' });
  async function add(type, body) {
    reason.className = 'mr-reason';
    reason.textContent = tx('addChecking');
    const r = await ask(type, body);
    if (r.ok) { field.value = ''; reason.textContent = tx('addAdded'); await refresh(); }
    else { reason.className = 'mr-reason bad'; reason.textContent = r.reason ?? (r.error === 'cancelled' ? '' : tx('engineOffModels')); }
  }
  field.addEventListener('keydown', (e) => { if (e.key === 'Enter' && field.value.trim()) add('mind-model', { op: 'add', query: field.value.trim() }); });
  const addGroup = group(
    h('div', { class: 'mr-add' }, h('label', { class: 'mr-add-label', for: 'add-query', text: tx('addLabel') }), field,
      pri(tx('addFind'), () => field.value.trim() && add('mind-model', { op: 'add', query: field.value.trim() }), { id: 'add-find' })),
    reason,
    h('div', { class: 'm-sep' }),
    h('div', { class: 'row' }, h('span', { class: 'label-col' }, h('span', { class: 'label', text: tx('addLocalLabel') }), h('span', { class: 'desc', text: tx('addLocalHint') })),
      ghost(tx('addPick'), () => add('mind-pick'), { id: 'add-pick' })));

  // ---- his models ----
  const mine = h('section', { class: 'mr-mine', id: 'mine-list' });
  let benching = null;
  function drawMine() {
    const ms = list().filter((m) => m.installed || m.origin !== 'Recommended');
    if (!ms.length) { mine.replaceChildren(group(note(tx('mineEmpty')))); return; }
    mine.replaceChildren(...ms.map((m) => {
      const active = m.id === activeId();
      const b = m.bench;
      const badges = [active ? chip(tx('chipActive'), 'accent') : null, m.ability === 'ChatOnly' ? chip(tx('chipChatOnly'), 'moon') : null,
        m.origin === 'Local' ? chip(tx('chipFromFile')) : m.origin === 'HuggingFace' ? chip(tx('chipFromHf')) : null];
      const buttons = [];
      if (m.installed && !active) buttons.push(ghost(tx('mineUse'), async () => { await ask('mind-model', { op: 'use', model: m.id }); await refresh(); }, { id: `use-${m.id}` }));
      if (m.installed) buttons.push(ghost(tx('mineBench'), async () => { benching = m.id; drawMine(); await ask('mind-bench'); }, { id: `bench-${m.id}` }));
      if (!m.installed) buttons.push(ghost(tx('mineGet'), () => download('start', m.id), { id: `get-${m.id}` }));
      if (m.origin !== 'Recommended' || m.installed) {
        const del = ghost(tx('mineRemove'), async () => { await ask('mind-model', { op: 'remove', model: m.id }); await refresh(); }, { id: `remove-${m.id}` });
        del.classList.add('mr-danger');
        buttons.push(del);
      }
      return card(
        h('div', { class: 'mr-mine-head' }, h('span', { class: 'm-text' },
          chips(h('span', { class: 'mr-name', text: m.name, dir: 'ltr' }), ...badges),
          meta([m.quant, tx('statGb', { n: gb(m.bytes) })].filter(Boolean).join(' · '))), h('span', { class: 'btn-row' }, ...buttons)),
        b ? stats([
          { num: num(Math.round(b.tokPerSec)), label: tx('statTps') },
          { num: tx('statGb', { n: num(Math.round(b.vramMB / 100) / 10) }), label: tx('statVram') },
          { num: tx('statOf', { a: num(b.dialectPass), b: num(b.dialectTotal) }), label: tx('statDialect') },
          { num: tx('statOf', { a: num(b.toolPass), b: num(b.toolTotal) }), label: tx('statTools') },
        ], tx('mineBench')) : null,
        h('p', { class: 'desc pr', text: benching === m.id ? tx('benchRunning') : m.ability === 'ChatOnly' ? tx('chatOnlyNote') : b ? '' : tx('benchNone') }));
    }));
  }

  async function refresh() {
    const next = await ask('mind-status');
    if (next.ok) st = next;
    if (benching && list().find((m) => m.id === benching)?.bench && st.phase !== 'loading') benching = null;
    drawDownload();
    drawRecommended();
    drawMine();
  }
  drawDownload();
  drawRecommended();
  drawMine();
  timer = setInterval(() => { if (document.visibilityState === 'visible') refresh(); }, 1000);

  return [
    title(tx('pageModels')),
    sub(tx('modelsSub')),
    dlCard,
    section(tx('groupRecommended')),
    recGroup,
    section(tx('groupAdd')),
    addGroup,
    section(tx('groupMine')),
    mine,
    note(tx('keepMemoryNote')),
  ];
}
