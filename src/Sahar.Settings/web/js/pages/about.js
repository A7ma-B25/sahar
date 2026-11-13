// عن سحر (owner ruling 2026-11-13; mockup PSettingsAbout): who she is, her version, and who helped her.
import { h, title, sub, section, group } from '../controls.js';
import { mount } from '../sahar-preview.js';
import '../strings/mind-runtime.js';

let face = null;
export function leave() {
  face?.destroy();
  face = null;
}

export async function render({ S, tx, reduced }) {
  const stage = h('div', { class: 'preview-stage', id: 'about-face' });
  const id = h('div', { class: 'id-row' }, stage,
    h('span', { class: 'label-col' },
      h('span', { class: 'id-name', text: tx('aboutName') }),
      h('span', { class: 'desc' }, h('bdi', { class: 'tnum', text: S.caps.version || '' }), ` · ${tx('testBuild')}`)));
  const credit = (label, value) => h('div', { class: 'row' }, h('span', { class: 'label', text: label }), h('bdi', { dir: 'ltr', class: 'value', text: value }));
  leave();
  face = await mount(stage, { size: 96, reduced });
  return [
    title(tx('aboutTitle')),
    sub(tx('aboutSub')),
    group(id),
    section(tx('helpers')),
    group(credit(tx('fontLabel'), 'Rubik · SIL Open Font License'), credit(tx('iconsLabel'), 'Phosphor · MIT'),
      credit(tx('aboutRuntime'), 'llama.cpp b12207 · MIT'), credit(tx('aboutModels'), 'Gemma 4 · Jais 3 · Apache 2.0')),
  ];
}
