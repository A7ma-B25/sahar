// الخصوصية (plan 0.10c Task 7; mockup PSettingsPrivacy): clear what you copied, the one thing that uses the internet by
// itself (the weather), and «امسح كل شي عني» (Ruling 9: she forgets everything and starts again).
import { h, title, sub, section, group, switchRow, note, pri, dng, ghost } from '../controls.js';

// Which «أكيد؟» is open and what each action last said: kept across a redraw (the window coming back redraws).
const kept = { open: null, status: {} };

/** Going to another page closes an open «أكيد؟». */
export function away() { kept.open = null; }

export function render({ S, tx, set, ask, reload }) {
  // An action row that asks «أكيد؟» in place: [yes] [لا].
  function confirmable({ id, label, hint, danger, confirmText, yes, run }) {
    const holder = h('div', { id });
    function ask0() {
      kept.open = kept.open === id ? null : kept.open;
      const labelEl = h('span', { class: danger ? 'action-label danger' : 'action-label', text: label });
      const btn = h('button', { class: 'action-row hv', type: 'button', id: `${id}-ask` }, labelEl,
        h('span', { class: 'desc', 'aria-live': 'polite', id: `${id}-status`, text: kept.status[id] ?? hint }));
      btn.addEventListener('click', confirm);
      holder.replaceChildren(btn);
    }
    function confirm() {
      kept.open = id;
      const yesBtn = (danger ? dng : pri)(yes, async () => {
        yesBtn.disabled = true;
        kept.open = null;
        kept.status[id] = await run();
        ask0();
        document.getElementById(`${id}-ask`)?.focus();
      }, { id: `${id}-yes`, class: danger ? 'dng' : 'pri' });
      holder.replaceChildren(h('div', { class: 'row', role: 'group', 'aria-label': label },
        h('span', { class: 'label-col' }, h('span', { class: 'label', text: tx('confirmTitle') }), h('span', { class: 'desc pr', text: confirmText })),
        h('span', { class: 'btn-row' }, yesBtn, ghost(tx('no'), () => { kept.open = null; ask0(); document.getElementById(`${id}-ask`)?.focus(); }, { id: `${id}-no` }))));
      yesBtn.focus();
    }
    if (kept.open === id) confirm(); else ask0();
    return holder;
  }

  const clip = confirmable({
    id: 'clip', label: tx('clipClear'), hint: tx('clipHint'), confirmText: tx('clipConfirmText'), yes: tx('clipYes'),
    run: async () => ((await ask('clipboard-clear')).ok ? tx('clipDone') : tx('engineOff')),
  });
  const erase = confirmable({
    id: 'erase', label: tx('eraseAll'), hint: tx('eraseHint'), danger: true, confirmText: tx('eraseConfirmText'), yes: tx('eraseYes'),
    run: async () => {
      const wasRunning = !!S.caps.engine;
      const r = await ask('erase');
      if (!r.ok) return tx('engineOff');
      const left = r.left ?? [];
      let line = left.length ? tx('eraseLeft', { names: left.join('، ') }) : tx('eraseDone');
      if (wasRunning && r.restarted === false) line += ' ' + tx('eraseNoRestart');
      kept.status.erase = line;
      await reload();   // the page shows her as she is now: nothing about you
      return line;
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
