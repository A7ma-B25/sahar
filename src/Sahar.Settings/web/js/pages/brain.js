// «عقلي» (0.14 stream A; mockup PSettingsBrain): her status, what she does by herself, what she learns from (each
// opt-in), her voice, and the PC (unload, GPU, backend + the optional CUDA pack). Every change saves at once.
import { h, title, sub, section, group, switchRow, row, segmented, note, ghost, newId } from '../controls.js';
import { mount } from '../sahar-preview.js';
import { getLang } from '../i18n.js';
import { toArabicIndic } from '../digits.js';
import { hand, HAND, chip, chips, card, meta, item } from '../mind-ui.js';
import '../strings/mind-runtime.js';

const lower = (v) => String(v ?? '').toLowerCase();
const LEVELS = ['often', 'normal', 'rare', 'off'];
const UNLOAD = [1, 5, 15, 30];
const BACKENDS = ['auto', 'vulkan', 'cuda'];
let preview = null;

export function leave() {
  if (preview) preview.destroy();
  preview = null;
}

const num = (n) => (getLang() === 'ar' ? toArabicIndic(String(n)).replace('.', '٫') : String(n));
const gb = (bytes) => num(Math.round(bytes / 1e7) / 100);

export async function render({ S, tx, set, ask, reduced, render: rerender }) {
  leave();
  const v = S.value;
  const st = await ask('mind-status');
  const models = st.ok && st.models ? st.models.models : [];
  const active = models.find((m) => m.id === (st.models && st.models.active));
  const stage = h('div', { class: 'mr-preview', id: 'brain-preview' });
  preview = await mount(stage, { size: 84, reduced });

  const line = active ? (v.mindEnabled ? tx('brainOn') : tx('brainPaused')) : tx('brainOff');
  const status = card(h('div', { class: 'mr-status' }, stage,
    h('div', { class: 'm-text' },
      h('span', { class: 'mr-status-line', id: 'brain-line', text: line }),
      active ? meta(`${active.name} · ${active.quant || ''}`.trim(), { dir: 'ltr' }) : null,
      active ? chips(chip(lower(v.mindBackend) === 'cpu' || !v.mindUseGpu ? tx('chipCpu') : tx('chipGpu'), 'accent'),
        active.bench ? chip(tx('chipTps', { n: num(Math.round(active.bench.tokPerSec)) })) : null) : null),
    ghost(active ? tx('brainChangeModel') : tx('brainGetModel'), () => window.sahar.nav('models'), { id: 'brain-models' })));

  const sw = (key, label, hint) => switchRow(tx(label), tx(hint), !!v[key], (on) => set(key, on), key);

  const initId = newId('ini');
  const level = Math.max(0, LEVELS.indexOf(lower(v.mindInitiative)));
  const rules = [['shield', 'ruleAsk', 'ruleAskHint'], ['terminal', 'ruleClaude', 'ruleClaudeHint'], ['blocked', 'ruleNever', 'ruleNeverHint']]
    .map(([icon, a, b]) => item({ icon: HAND[icon], line: tx(a), under: [h('span', { class: 'desc', text: tx(b) })] }));

  const voiceId = newId('vo');
  const voice = lower(v.mindVoice) === 'self' ? 1 : 0;
  const voiceHint = h('span', { class: 'desc', id: 'voice-hint', text: voice ? tx('voiceSelf') : tx('voiceComplementary') });

  const unloadId = newId('un');
  const backendId = newId('be');
  const backend = Math.max(0, BACKENDS.indexOf(lower(v.mindBackend)));
  const cudaReady = !!(st.ok && st.cudaInstalled);
  const backendHint = h('span', { class: 'desc', id: 'backend-hint' });
  const cudaBtn = ghost(tx('cudaGet'), async () => { await ask('mind-download', { op: 'start', model: 'runtime-cuda' }); }, { id: 'cuda-get' });
  function showBackend(i) {
    backendHint.textContent = i === 2 ? (cudaReady ? tx('backendCudaReady') : tx('backendCudaHint', { size: gb(st.cudaBytes || 0) })) : tx('backendHint');
    cudaBtn.hidden = !(i === 2 && !cudaReady);
  }
  showBackend(backend);

  return [
    title(tx('pageBrain')),
    sub(tx('brainSub')),
    status,
    section(tx('groupSelf')),
    group(
      sw('mindSelfActions', 'swSelf', 'swSelfHint'),
      row(tx('initLabel'), segmented(initId, tx('initLevels'), level, (i) => set('mindInitiative', LEVELS[i]), 'initiative'), { desc: tx('initHint'), labelId: initId }),
      h('div', { class: 'm-sep' }),
      ...rules),
    section(tx('groupLearn')),
    group(
      sw('mindLearnChats', 'swLearnChats', 'swLearnChatsHint'),
      sw('mindLearnImports', 'swLearnImports', 'swLearnImportsHint'),
      sw('mindLearnExamples', 'swLearnExamples', 'swLearnExamplesHint'),
      sw('mindLearnMusic', 'swLearnMusic', 'swLearnMusicHint'),
      note(tx('learnNote'))),
    section(tx('groupVoice')),
    group(h('div', { class: 'row' },
      h('span', { class: 'label-col' }, h('span', { class: 'label', id: voiceId, text: tx('voiceLabel') }), voiceHint),
      segmented(voiceId, tx('voices'), voice, (i) => { voiceHint.textContent = i ? tx('voiceSelf') : tx('voiceComplementary'); set('mindVoice', i ? 'self' : 'complementary'); }, 'voice'))),
    section(tx('brainGroupDevice')),
    group(
      row(tx('unloadLabel'), segmented(unloadId, tx('unloadChoices'), Math.max(0, UNLOAD.indexOf(v.mindUnloadMinutes)), (i) => set('mindUnloadMinutes', UNLOAD[i]), 'unload'),
        { desc: tx('unloadHint'), labelId: unloadId }),
      sw('mindUseGpu', 'swGpu', 'swGpuHint'),
      h('div', { class: 'row' },
        h('span', { class: 'label-col' }, h('span', { class: 'label', id: backendId, text: tx('backendLabel') }), backendHint),
        segmented(backendId, tx('backends'), backend, (i) => { showBackend(i); set('mindBackend', BACKENDS[i]); }, 'backend'),
        cudaBtn)),
  ];
}
