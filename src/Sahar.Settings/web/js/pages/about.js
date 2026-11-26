// عن سحر (owner ruling 2026-11-13; mockup PSettingsAbout): who she is, her version, and who helped her.
import { h, title, sub, section, group } from '../controls.js';
import { mount } from '../sahar-preview.js';
import '../strings/mind-runtime.js';
import '../strings/diag-capture.js';

let face = null;
// 0.15 T7: what «ابعت سجلّي» last said, kept across a redraw (the window coming back redraws).
let sent = null;
export function leave() {
  face?.destroy();
  face = null;
}

export async function render({ S, tx, ask, reduced }) {
  const stage = h('div', { class: 'preview-stage', id: 'about-face' });
  const id = h('div', { class: 'id-row' }, stage,
    h('span', { class: 'label-col' },
      h('span', { class: 'id-name', text: tx('aboutName') }),
      h('span', { class: 'desc' }, h('bdi', { class: 'tnum', text: S.caps.version || '' }), ` · ${tx('testBuild')}`)));
  const credit = (label, value) => h('div', { class: 'row' }, h('span', { class: 'label', text: label }), h('bdi', { dir: 'ltr', class: 'value', text: value }));
  // «ابعت سجلّي»: she makes the zip (or the app, when she is not running) and says where it is; the page gets its name only.
  const sendDesc = h('span', { class: 'desc', 'aria-live': 'polite', id: 'send-log-status', text: sent ?? tx('sendLogHint') });
  const send = h('button', { class: 'action-row hv', type: 'button', id: 'send-log' },
    h('span', { class: 'action-label', text: tx('sendLog') }), sendDesc);
  send.addEventListener('click', async () => {
    send.disabled = true;
    const r = await ask('diag');
    sent = r.ok ? tx(r.desktop === false ? 'sendLogDoneFolder' : 'sendLogDone', { name: r.name }) : tx('sendLogFailed');
    sendDesc.textContent = sent;
    send.disabled = false;
  });
  leave();
  face = await mount(stage, { size: 96, reduced });
  return [
    title(tx('aboutTitle')),
    sub(tx('aboutSub')),
    group(id),
    section(tx('groupSupport')),
    group(send),
    section(tx('helpers')),
    group(credit(tx('fontLabel'), 'Rubik · SIL Open Font License'), credit(tx('iconsLabel'), 'Phosphor · MIT'),
      credit(tx('aboutRuntime'), 'llama.cpp b12207 · MIT'), credit(tx('aboutModels'), 'Gemma 4 · Jais 3 · Apache 2.0')),
  ];
}
