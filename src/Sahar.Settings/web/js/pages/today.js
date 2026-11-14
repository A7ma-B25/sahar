// «شو عملت اليوم» (0.14 stream C, mockup PSettingsToday): every action she ran, asked about or refused — today,
// yesterday or the 30 days she keeps — with «رجّعيها» while the undo still applies, and «منيح» / «مش منيح».
// The lines come from her (mind-log); when she is off the settings app reads the encrypted log itself.
import { h, title, sub, group, segmented, ghost, newId } from '../controls.js';
import { chip, chips, item, meta, hand, HAND } from '../mind-ui.js';
import { toArabicIndic } from '../digits.js';
import { getLang } from '../i18n.js';
import '../strings/mind-autonomy.js';

const DAYS = ['today', 'yesterday', 'all'];
let day = 0;   // the chosen tab survives a re-render (the page is drawn again after a save)

/** A count in her words: «٦ شغلات», «سألتك مرتين». */
export function countText(tx, n, keys) {
  const num = getLang() === 'ar' ? toArabicIndic(n) : String(n);
  if (n === 1) return tx(keys[0]);
  if (n === 2) return tx(keys[1]);
  return tx(n > 10 && keys[3] ? keys[3] : keys[2], { n: num });
}

/** The line under an entry: when, and who asked or why (mockup: «١٠:١٢ · إنت طلبت: «…»»). */
export function metaLine(tx, e, showDay = false) {
  const ar = getLang() === 'ar';
  const when = (showDay ? `${e.day} ` : '') + e.time;
  let why;
  if (e.tool === 'claude.permission') why = tx('todayClaude');
  else if (e.outcome === 'asked-no') why = tx('todaySaidNo');
  else if (e.outcome === 'asked-yes') why = tx('todaySaidYes');
  else if (e.outcome === 'asked') why = tx('todayWaiting');
  else if (e.outcome === 'refused') why = tx(e.source === 'user' ? 'todayRefusedYou' : 'todayRefusedSelf');
  else if (e.outcome === 'failed') why = tx('todayFailed');
  else if (e.outcome === 'undone') why = tx('todayUndone');
  else if (e.source === 'user') why = e.why ? tx('todayYouAskedQ', { why: e.why }) : tx('todayYouAsked');
  else why = e.why ? tx('todaySelfWhy', { why: e.why }) : tx('todaySelf');
  return `${ar ? toArabicIndic(when) : when} · ${why}`;
}

/** The icon for an entry (the mockup's set): asks wear the shield, Claude Code the terminal. */
export function iconFor(e) {
  if (e.tool === 'claude.permission') return HAND.terminal;
  if (e.outcome.startsWith('asked')) return HAND.shield;
  if (e.tool.startsWith('timer') || e.tool.startsWith('reminder')) return HAND.timer;
  if (e.tool.startsWith('music')) return HAND.mic;
  if (e.tool.startsWith('note')) return HAND.note;
  if (e.tool === 'settings.set') return HAND.plus;
  if (e.outcome === 'refused') return HAND.blocked;
  return HAND.bulb;
}

export async function render({ tx, ask, render: rerender }) {
  const r = await ask('mind-log', { day: DAYS[day] });
  const log = (r.ok && r.log) || { entries: [], counts: { all: 0, asked: 0, refused: 0 } };
  const engine = !!r.engine;
  const labelId = newId('days');
  const days = segmented(labelId, tx('todayDays'), day, (i) => { day = i; rerender(true); }, 'today-days');
  days.removeAttribute('aria-labelledby');
  days.setAttribute('aria-label', tx('todayDaysLabel'));

  const c = log.counts;
  const counts = chips(
    chip(c.all === 0 ? tx('todayCount0') : countText(tx, c.all, ['todayCount1', 'todayCount2', 'todayCountFew', 'todayCountMany']), 'accent'),
    c.asked > 0 ? chip(countText(tx, c.asked, ['todayAsked1', 'todayAsked2', 'todayAskedN'])) : null,
    c.refused > 0 ? chip(countText(tx, c.refused, ['todayRefused1', 'todayRefused2', 'todayRefusedN'])) : null);
  counts.setAttribute('role', 'group');
  counts.setAttribute('aria-label', tx('todayCountLabel'));
  counts.id = 'today-counts';

  const status = h('p', { class: 'desc t-status', role: 'status', 'aria-live': 'polite', id: 'today-status', text: engine ? '' : tx('todayOff') });

  function feedbackButton(e, value) {
    const on = e.feedback === value;
    const btn = h('button', {
      class: 'm-icon-btn hv', type: 'button', 'aria-label': tx(value > 0 ? 'todayGood' : 'todayBad'), 'aria-pressed': on ? 'true' : 'false',
      id: `fb-${value > 0 ? 'up' : 'down'}-${e.id}`,
    }, hand(value > 0 ? HAND.up : HAND.down, 17));
    btn.addEventListener('click', async () => {
      const next = e.feedback === value ? 0 : value;
      const res = await ask('mind-feedback', { entry: e.id, value: next });
      if (res.ok) { e.feedback = next; rerender(true); }
    });
    return btn;
  }

  const rows = log.entries.map((e) => {
    const end = [];
    if (e.undo) {
      end.push(ghost(tx(e.tool === 'note.save' ? 'todayUndoNote' : 'todayUndo'), async () => {
        const res = await ask('mind-undo', { entry: e.id });
        if (res.ok) rerender(true);
        else status.textContent = tx(res.error === 'engine-off' ? 'todayOff' : 'todayTooLate');
      }, { id: `undo-${e.id}` }));
    }
    end.push(feedbackButton(e, 1), feedbackButton(e, -1));
    return item({
      icon: iconFor(e), line: e.summary,
      under: meta(metaLine(tx, e, DAYS[day] === 'all')),
      end: h('span', { class: 't-actions' }, ...end),
    });
  });
  const list = group(...(rows.length ? rows : [h('p', { class: 'note', text: tx('todayEmpty') })]));
  list.setAttribute('aria-label', tx('todayLogLabel'));
  list.id = 'today-log';

  return [
    title(tx('pageToday')),
    sub(tx('todaySub')),
    h('div', { class: 't-bar' }, counts, days),
    list,
    status,
    h('p', { class: 'note pr t-note', text: tx('todayNote') }),
  ];
}
