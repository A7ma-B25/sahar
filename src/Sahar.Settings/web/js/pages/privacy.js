// الخصوصية (plan 0.10c Task 7; mockup PSettingsPrivacy): clear what you copied, the one thing that uses the internet by
// itself (the weather), and «امسح كل شي عني» (Ruling 9: she forgets everything and starts again).
import { h, title, sub, section, group, switchRow, note, pri, dng, ghost } from '../controls.js';
import '../strings/mind-memory.js';

export function render({ S, tx, set, ask, render: rerender }) {
  // An action row that asks «أكيد؟» in place: [yes] [لا].
  function confirmable({ id, label, hint, danger, confirmText, yes, run, extra }) {
    const holder = h('div', { id });
    let status = hint;
    function ask0() {
      const labelEl = h('span', { class: danger ? 'action-label danger' : 'action-label', text: label });
      const btn = h('button', { class: 'action-row hv', type: 'button', id: `${id}-ask` }, labelEl,
        h('span', { class: 'desc', 'aria-live': 'polite', id: `${id}-status`, text: status }));
      btn.addEventListener('click', confirm);
      holder.replaceChildren(btn);
    }
    function confirm() {
      const yesBtn = (danger ? dng : pri)(yes, async () => {
        yesBtn.disabled = true;
        status = await run();
        ask0();
        document.getElementById(`${id}-ask`)?.focus();
      }, { id: `${id}-yes`, class: danger ? 'dng' : 'pri' });
      holder.replaceChildren(h('div', { class: 'row', role: 'group', 'aria-label': label },
        h('span', { class: 'label-col' }, h('span', { class: 'label', text: tx('confirmTitle') }), h('span', { class: 'desc pr', text: confirmText })),
        h('span', { class: 'btn-row' }, yesBtn, ghost(tx('no'), () => { ask0(); document.getElementById(`${id}-ask`)?.focus(); }, { id: `${id}-no` }))),
        ...(extra ? [extra()] : []));
      yesBtn.focus();
    }
    ask0();
    return holder;
  }

  const clip = confirmable({
    id: 'clip', label: tx('clipClear'), hint: tx('clipHint'), confirmText: tx('clipConfirmText'), yes: tx('clipYes'),
    run: async () => ((await ask('clipboard-clear')).ok ? tx('clipDone') : tx('engineOff')),
  });
  // 0.14: the Mind's data goes too (crypto-shredded: its key first); the downloaded model only if he ticks it (default
  // keep: it holds nothing about him).
  const erase = confirmable({
    id: 'erase', label: tx('eraseAll'), hint: tx('eraseHint'), danger: true, confirmText: tx('eraseConfirmMind'), yes: tx('eraseYes'),
    extra: () => h('label', { class: 'mm-check', for: 'erase-model' },
      h('input', { type: 'checkbox', id: 'erase-model' }), h('span', { text: tx('eraseModelToo') })),
    run: async () => {
      const model = !!document.getElementById('erase-model')?.checked;
      await ask('mind-forget', { model });
      const r = await ask('erase');
      return r.ok ? tx('eraseDone') : tx('engineOff');
    },
  });
  const weather = switchRow(tx('swWeatherNet'), tx('swWeatherNetHint'), !!S.value.weatherEnabled, async (on, btn) => {
    const r = await set('weatherEnabled', on);
    if (!r.ok) {
      btn.set(!on);
      if (r.error === 'needs-city') window.sahar.nav('sahar');   // she needs your city: it is asked on her own page
    }
  }, 'weatherEnabled');
  weather.id = 'sw-weather-net';
  return [
    title(tx('pagePrivacy')),
    sub(tx('privacySub')),
    section(tx('groupClip')),
    group(clip),
    section(tx('groupNet')),
    group(weather, note(tx('netNote'))),
    section(tx('groupAll')),
    group(erase),
  ];
}
