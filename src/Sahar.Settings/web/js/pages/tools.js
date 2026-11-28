// «الأدوات» (0.19 spec §6, §8): the two ready tools (pictures, voice clips: «جرّب» once, then a grant), his MCP servers —
// each tool with its full description, its inputs and its state before anything is on; on/off, «اسألني / دايماً / بس
// بتقرا», «حساسة», a new approval for a changed tool or program — and the add form («رابط» | «برنامج», an optional
// secret that is sent once and never shown again). The Mind does the work; this page asks and polls its status.
// Text via textContent only; ids/paths/links left-to-right.
import { h, title, sub, section, note, ghost, pri, dng, segmented, switchRow, select } from '../controls.js';
import { getLang } from '../i18n.js';
import { toArabicIndic } from '../digits.js';
import { card, chip } from '../mind-ui.js';
import '../strings/mind-tools.js';
import { startPoll, stopPoll } from '../poll.js';

const MAX = 12;
let gen = 0;
export function leave() {
  gen++;
  stopPoll('tools');
}

const num = (n) => (getLang() === 'ar' ? toArabicIndic(String(n)) : String(n));
const STATE = { ok: 'tlStateOk', paused: 'tlStatePaused', 'exe-changed': 'tlStatePaused', oauth: 'tlStateOauth', missing: 'tlStateMissing',
  error: 'tlStateError', offline: 'tlStateError', timeout: 'tlStateError', unknown: 'tlStateUnknown' };
const RESULT = { added: 'tlResultAdded', listed: 'tlResultListed', 'bad-url': 'tlResultBadUrl', 'bad-exe': 'tlResultBadExe', 'bad-name': 'tlResultBadName',
  'bad-secret': 'tlResultBadSecret', limit: 'tlLimit', oauth: 'tlStateOauth', timeout: 'tlResultTimeout', error: 'tlStateError', offline: 'tlStateError',
  'exe-changed': 'tlStatePaused', missing: 'tlStateMissing' };
const CHECK = { ok: 'tlCheckOk', offline: 'tlCheckOffline', failed: 'tlCheckFailed', 'key-bad': 'tlCheckKeyBad', quota: 'tlCheckQuota', 'no-key': 'tlVoiceNeedsKey', timeout: 'tlCheckOffline' };
const lower = (g) => String(g || 'ask').toLowerCase();

export async function render({ S, tx, ask }) {
  leave();
  const myGen = gen;
  let st = await ask('mind-tools-status', { refresh: 'true' });
  let seq = st.seq;
  const results = {};   // server id (or 'image' | 'voice') → the line under it, kept across a redraw
  const info = () => (st.ok && st.info ? st.info : null);
  const local = () => !!S.value.mindLocalOnly;
  const enabledCount = () => (info()?.servers || []).reduce((n, s) => n + (s.tools || []).filter((t) => t.enabled).length, 0);
  const checkLine = (code) => (code && code.startsWith('wait:') ? tx('tlCheckWait', { s: num(code.slice(5)) }) : CHECK[code] ? tx(CHECK[code]) : '');

  async function op(key, body) {
    results[key] = tx('tlWorking');
    draw();
    const r = await ask('mind-tools-op', body);
    if (!r.ok) { results[key] = tx('tlEngineOff'); draw(); }
  }

  const live = h('div', { id: 'tl-live', class: 'tl-live' });

  function grantSelect(id, value, options, onChange) {
    const el = select(id, options.map((o) => [o, tx(o === 'ask' ? 'tlGrantAsk' : o === 'always' ? 'tlGrantAlways' : 'tlGrantRead')]), lower(value), onChange);
    el.setAttribute('aria-label', tx('tlGrantLabel'));
    el.classList.add('tl-grant');
    return el;
  }

  function mediaCard(kind) {
    const m = info()?.media || {};
    const checked = kind === 'image' ? m.imageChecked : m.voiceChecked;
    const needsKey = kind === 'voice' && !m.groqKey;
    const result = h('p', { class: 'desc tl-result', id: `tl-${kind}-result`, 'aria-live': 'polite', text: results[kind] ?? checkLine(kind === 'image' ? m.imageTest : m.voiceTest) });
    const check = ghost(tx('tlCheck'), async () => {
      results[kind] = tx('tlChecking');
      draw();
      const r = await ask('mind-tools-op', { op: kind === 'image' ? 'check-image' : 'check-voice' });
      if (!r.ok) { results[kind] = tx('tlEngineOff'); draw(); }
    }, { id: `tl-${kind}-check` });
    check.hidden = needsKey;
    const grant = grantSelect(`tl-${kind}-grant`, kind === 'image' ? m.imageGrant : m.voiceGrant, ['ask', 'always'],
      (v) => op(kind, { op: 'media-grant', server: kind, value: v }));
    grant.hidden = !checked || needsKey;
    const head = h('div', { class: 'tl-head' },
      h('span', { class: 'label tl-name', text: tx(kind === 'image' ? 'tlImage' : 'tlVoice') }),
      chip(checked && !needsKey ? tx('tlReady') : tx('tlNotChecked'), checked && !needsKey ? 'accent' : null));
    const el = card(head,
      h('p', { class: 'desc tl-hint', text: tx(kind === 'image' ? 'tlImageHint' : 'tlVoiceHint') }),
      needsKey ? h('p', { class: 'desc tl-need', text: tx('tlVoiceNeedsKey') }) : null,
      h('div', { class: 'btn-row tl-actions' }, check, grant),
      result);
    el.id = `tl-${kind}`;
    el.classList.add('tl-card');
    return el;
  }

  function argsLine(args) {
    if (!args) return null;
    if (!args.length) return tx('tlNoArgs');
    const kind = (a) => (a.type === 'Int' ? tx('tlArgInt') : a.type === 'Bool' ? tx('tlArgBool') : a.type === 'Enum' ? tx('tlArgEnum', { v: (a.values || []).join(' / ') }) : tx('tlArgString'));
    return tx('tlArgs', { list: args.map((a) => `${a.name} (${kind(a)})`).join('، ') });
  }

  function toolRow(s, t, i) {
    const chips = [];
    if (t.state === 'new') chips.push(chip(tx('tlNew')));
    if (t.state === 'changed') chips.push(chip(tx('tlChanged'), 'warn'));
    if (t.state === 'unfit') chips.push(chip(tx('tlUnfit')));
    if (t.sensitive) chips.push(chip(tx('tlSensitive'), 'warn'));
    const controls = [];
    if (t.state === 'changed') {
      controls.push(pri(tx('tlApproveTool'), () => op(s.id, { op: 'enable', server: s.id, tool: t.name }), { id: `tl-approve-tool-${s.id}-${i}` }));
    } else if (t.state !== 'unfit') {
      const sw = switchRow(tx('tlOn'), null, !!t.enabled, (on) => {
        if (on && enabledCount() >= MAX) { sw.set(false); results[s.id] = tx('tlLimit'); resultEl(s.id).textContent = results[s.id]; return; }
        op(s.id, { op: on ? 'enable' : 'disable', server: s.id, tool: t.name });
      }, `tool-${i}`);
      sw.id = `tl-on-${s.id}-${i}`;
      sw.classList.add('tl-switch');
      controls.push(sw);
    }
    if (t.state !== 'unfit' && !t.sensitive) {
      controls.push(grantSelect(`tl-grant-${s.id}-${i}`, t.grant, ['ask', 'always', 'read'], (v) => op(s.id, { op: 'grant', server: s.id, tool: t.name, value: v })));
    }
    if (t.state !== 'unfit' && !t.autoSensitive) {
      const mark = switchRow(tx('tlMarkSensitive'), null, !!t.sensitive, (on) => op(s.id, { op: 'sensitive', server: s.id, tool: t.name, value: on ? 'true' : 'false' }), `sens-${i}`);
      mark.id = `tl-sens-${s.id}-${i}`;
      mark.classList.add('tl-switch');
      controls.push(mark);
    }
    return h('div', { class: 'tl-tool', 'data-tool': t.name },
      h('div', { class: 'tl-toolhead' }, h('span', { class: 'label tl-ltr', dir: 'ltr', text: t.name }), ...chips),
      h('p', { class: 'tl-desc', text: t.description || '—' }),
      t.args !== undefined ? h('p', { class: 'desc tl-args', text: argsLine(t.args) ?? '' }) : null,
      controls.length ? h('div', { class: 'tl-controls' }, ...controls) : null);
  }

  const resultEl = (id) => live.querySelector?.(`#tl-result-${id}`) ?? findIn(live, `tl-result-${id}`);

  function serverCard(s) {
    const stateKey = STATE[s.state] || 'tlStateUnknown';
    const result = h('p', { class: 'desc tl-result', id: `tl-result-${s.id}`, 'aria-live': 'polite',
      text: results[s.id] ?? (info()?.resultServer === s.id && RESULT[info()?.result] ? tx(RESULT[info().result]) : '') });
    const approve = s.state === 'paused' || s.state === 'exe-changed'
      ? pri(tx('tlApproveExe'), () => op(s.id, { op: 'approve', server: s.id }), { id: `tl-approve-${s.id}` }) : null;
    const refresh = ghost(tx('tlRefresh'), () => op(s.id, { op: 'refresh', server: s.id }), { id: `tl-refresh-${s.id}` });
    let sure = false;
    const remove = dng(tx('tlRemove'), () => {
      if (!sure) { sure = true; remove.textContent = tx('tlRemoveSure'); return; }
      op(s.id, { op: 'remove', server: s.id });
    }, { id: `tl-remove-${s.id}` });
    const el = card(
      h('div', { class: 'tl-head' },
        h('span', { class: 'label tl-name', text: s.name }),
        chip(tx(s.kind === 'url' ? 'tlKindUrl' : 'tlKindExe')),
        chip(tx(stateKey), s.state === 'ok' ? 'accent' : s.state === 'unknown' ? null : 'warn')),
      h('p', { class: 'desc tl-target tl-ltr', id: `tl-server-target-${s.id}`, dir: 'ltr', text: [s.target, ...(s.args || [])].join(' ') }),
      h('div', { class: 'tl-tools' }, ...(s.tools || []).map((t, i) => toolRow(s, t, i))),
      h('div', { class: 'btn-row tl-actions' }, approve, refresh, remove),
      result);
    el.id = `tl-server-${s.id}`;
    el.classList.add('tl-card', 'tl-server');
    return el;
  }

  function build() {
    if (st.ok && st.engine === false) return [note(tx('tlEngineOff'))];
    const out = [];
    if (local()) {
      const n = note(tx('tlLocalNote'));
      n.id = 'tl-local-note';
      out.push(n);
    } else out.push(section(tx('tlBuiltins')), h('div', { class: 'tl-list' }, mediaCard('image'), mediaCard('voice')));
    out.push(section(tx('tlServers')), note(tx('tlServersNote', { n: num(enabledCount()) })));
    const servers = info()?.servers || [];
    if (!servers.length) {
      const empty = card(h('p', { class: 'desc tl-emptyline', text: tx('tlEmpty') }));
      empty.id = 'tl-empty';
      out.push(empty);
    } else out.push(h('div', { class: 'tl-list' }, ...servers.map(serverCard)));
    return out;
  }
  function draw() { live.replaceChildren(...build()); }
  draw();

  // ---- the add form (stays as he typed it while the status redraws above) ----
  let kind = 'url';
  const field = (labelText, forId, ...controls) => h('div', { class: 'tl-field' }, h('label', { class: 'label tl-flabel', for: forId, text: labelText }), h('div', { class: 'tl-fctl' }, ...controls));
  const name = h('input', { class: 'input', id: 'tl-add-name', maxlength: '40', autocomplete: 'off', spellcheck: 'false' });
  const url = h('input', { class: 'input tl-ltr', id: 'tl-add-url', type: 'url', dir: 'ltr', placeholder: 'https://…/mcp', autocomplete: 'off', spellcheck: 'false' });
  const exe = h('input', { class: 'input tl-ltr', id: 'tl-add-exe', dir: 'ltr', placeholder: 'C:\\…\\server.exe', autocomplete: 'off', spellcheck: 'false' });
  const pick = ghost(tx('tlAddPick'), async () => { const r = await ask('mind-tools-pick'); if (r.ok && r.path) exe.value = r.path; }, { id: 'tl-add-pick' });
  const args = h('input', { class: 'input tl-ltr', id: 'tl-add-args', dir: 'ltr', autocomplete: 'off', spellcheck: 'false' });
  const secretKind = select('tl-add-secret-kind', [['', tx('tlSecretNone')], ['header', tx('tlSecretHeader')], ['env', tx('tlSecretEnv')]], '', (v) => { secretRow.hidden = !v; });
  const secretName = h('input', { class: 'input tl-ltr', id: 'tl-add-secret-name', dir: 'ltr', autocomplete: 'off', spellcheck: 'false' });
  const secret = h('input', { class: 'input tl-ltr', id: 'tl-add-secret', type: 'password', dir: 'ltr', autocomplete: 'off', spellcheck: 'false' });
  const urlRow = field(tx('tlAddUrl'), 'tl-add-url', url);
  const exeRow = field(tx('tlAddExe'), 'tl-add-exe', h('div', { class: 'tl-line' }, exe, pick));
  const argsRow = field(tx('tlAddArgs'), 'tl-add-args', args);
  const secretRow = h('div', { class: 'tl-secret' }, field(tx('tlAddSecretName'), 'tl-add-secret-name', secretName), field(tx('tlAddSecret'), 'tl-add-secret', secret, h('span', { class: 'desc', text: tx('tlSecretNote') })));
  secretRow.hidden = true;
  const showKind = () => { urlRow.hidden = kind !== 'url'; exeRow.hidden = kind !== 'exe'; argsRow.hidden = kind !== 'exe'; };
  const kindSeg = segmented('tl-add-kind-label', [tx('tlKindUrl'), tx('tlKindExe')], 0, (i) => { kind = i === 0 ? 'url' : 'exe'; showKind(); }, 'tl-add-kind');
  showKind();
  const addResult = h('p', { class: 'desc tl-result', id: 'tl-add-result', 'aria-live': 'polite' });
  const go = pri(tx('tlAddGo'), async () => {
    const body = { name: name.value.trim(), kind, url: kind === 'url' ? url.value.trim() : '', exe: kind === 'exe' ? exe.value.trim() : '', args: kind === 'exe' ? args.value.trim() : '',
      secretKind: secretKind.value, secretName: secretKind.value ? secretName.value.trim() : '', secret: secretKind.value ? secret.value : '' };
    secret.value = '';   // never kept in the page once sent
    addResult.textContent = tx('tlAdding');
    const r = await ask('mind-tools-add', body);
    if (!r.ok) { addResult.textContent = RESULT[r.error] ? tx(RESULT[r.error]) : tx('tlEngineOff'); return; }
    waitAdd = true;
  }, { id: 'tl-add-go' });
  const form = card(
    field(tx('tlAddName'), 'tl-add-name', name),
    h('div', { class: 'tl-field' }, h('span', { class: 'label tl-flabel', id: 'tl-add-kind-label', text: tx('tlAddKind') }), h('div', { class: 'tl-fctl' }, kindSeg)),
    urlRow, exeRow, argsRow,
    field(tx('tlAddSecretKind'), 'tl-add-secret-kind', secretKind),
    secretRow,
    h('p', { class: 'desc tl-addnote', text: tx('tlAddNote') }),
    h('div', { class: 'btn-row tl-actions' }, go),
    addResult);
  form.id = 'tl-add';
  form.classList.add('tl-card', 'tl-form');

  let waitAdd = false;
  async function refresh() {
    if (myGen !== gen) return;
    const next = await ask('mind-tools-status');
    if (myGen !== gen || !next.ok || next.seq === seq) return;
    st = next;
    seq = next.seq;
    const i = info();
    for (const k of Object.keys(results)) delete results[k];   // the Mind answered: its own result lines now
    if (i && waitAdd && i.result) {
      addResult.textContent = RESULT[i.result] ? tx(RESULT[i.result]) : '';
      if (i.result === 'added') { name.value = ''; url.value = ''; exe.value = ''; args.value = ''; secretName.value = ''; }
      waitAdd = false;
    }
    draw();
  }
  if (myGen === gen) startPoll('tools', refresh);

  return [title(tx('pageTools')), sub(tx('tlSub')), live, section(tx('tlAdd')), form];
}

function findIn(root, id) {
  const stack = [root];
  while (stack.length) {
    const n = stack.pop();
    if (n && n.id === id) return n;
    for (const c of (n && n.children) || []) stack.push(c);
  }
  return { textContent: '' };
}
