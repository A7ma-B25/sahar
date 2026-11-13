// الصلاة (plan 0.10c Task 7; mockup PSettingsSoul, page «الصلاة»): today's five times (worked out on the PC), the next
// one with a countdown, the city (searched through her) or Windows location, the method, Asr, and the reminder.
import { h, title, sub, section, group, row, segmented, switchRow, input, select, note, newId } from '../controls.js';
import { cells, toArabicIndic } from '../digits.js';
import { getLang } from '../i18n.js';

const ORDER = ['fajr', 'dhuhr', 'asr', 'maghrib', 'isha'];
const MINUTES = [5, 10, 15, 20];
const WINDOWS_LOCATION = 'موقع الويندوز';

let tick = 0;
export function leave() {
  clearInterval(tick);
  tick = 0;
}

const num = (s) => (getLang() === 'ar' ? toArabicIndic(s) : s);

/** 14:41 → 2:41 (12-hour, as the mockup). */
function clock(at) {
  const [hh, mm] = at.split(':').map(Number);
  return `${hh % 12 || 12}:${String(mm).padStart(2, '0')}`;
}

/** 2530 s → 42:10 (minutes:seconds under an hour, else hours:minutes). */
function left(seconds) {
  const s = Math.max(0, Math.round(seconds));
  const hh = Math.floor(s / 3600), mm = Math.floor((s % 3600) / 60), ss = s % 60;
  return hh > 0 ? `${hh}:${String(mm).padStart(2, '0')}` : `${mm}:${String(ss).padStart(2, '0')}`;
}

export async function render({ S, tx, set, ask, render: rerender }) {
  const v = S.value;
  const names = Object.fromEntries(ORDER.map((p, i) => [p, getLang() === 'ar' ? (S.caps.prayerNames?.[p] ?? tx('prayers')[i]) : tx('prayers')[i]]));

  // Today: five fixed cells and the next one counting down (every second while the page is visible).
  const today = group();
  today.id = 'prayer-today';
  async function drawToday() {
    leave();
    const r = await ask('prayer-today');
    if (!r.ok) {
      today.replaceChildren(h('p', { class: 'note', id: 'prayer-no-city', text: tx('noCity') }));
      today.querySelector('.note').style.paddingBlockStart = '12px';
      return;
    }
    const nextValue = h('span', { class: 'next-value', id: 'prayer-next' });
    const due = Date.now() + (r.next?.seconds ?? 0) * 1000;
    const nextName = r.next ? names[r.next.prayer] : '';
    // The countdown in its own left-to-right run: digit cells are boxes, and boxes in a right-to-left line reverse.
    const [pre, post = ''] = tx('nextIn', { name: nextName }).split('{left}');
    const time = h('span', { dir: 'ltr', class: 'tnum' });
    nextValue.append(pre, time, post);
    const draw = () => cells(time, num(left((due - Date.now()) / 1000)));
    draw();
    const list = h('ol', { class: 'times', 'aria-label': tx('todayLabel') },
      ...r.times.map((x) => {
        const at = h('span', { class: 'time-at' });
        cells(at, num(clock(x.at)));
        return h('li', { class: r.next?.prayer === x.prayer ? 'time-cell next' : 'time-cell', 'data-prayer': x.prayer },
          h('span', { class: 'time-name', text: names[x.prayer] }), at);
      }));
    today.replaceChildren(row(tx('nextLabel'), nextValue), list);
    tick = setInterval(() => {
      if (document.hidden) return;
      if (Date.now() >= due) drawToday();
      else draw();
    }, 1000);
  }

  // المكان والطريقة
  const cityInput = input({ id: 'prayer-city', placeholder: tx('cityPlaceholder'), value: v.prayerCity && v.prayerCity !== WINDOWS_LOCATION ? v.prayerCity : '' });
  const cityLine = h('span', { class: 'desc', id: 'prayer-city-line', 'aria-live': 'polite' });   // search results and refusals
  const cityLabel = h('span', { class: 'label-col' }, h('label', { class: 'label', for: 'prayer-city', text: tx('prayerCityLabel') }), cityLine);
  cityInput.addEventListener('keydown', async (e) => {
    if (e.key !== 'Enter' || !cityInput.value.trim()) return;
    cityLine.className = 'skeleton';
    cityLine.textContent = '';
    cityLine.setAttribute('aria-label', tx('searching'));
    const r = await ask('city', { text: cityInput.value, for: 'prayer' });
    cityLine.removeAttribute('aria-label');
    if (r.ok) {
      cityLine.className = 'desc';
      cityLine.textContent = r.name;
      winLoc.set(false);
      drawToday();
    } else {
      cityLine.className = 'error-text';
      cityLine.textContent = r.reason || tx('engineOff');
    }
  });
  const winLoc = switchRow(tx('swWinLoc'), '', v.prayerCity === WINDOWS_LOCATION, async (on, btn) => {
    if (!on) return;   // switched off: the city search stays the way to set it
    const r = await ask('locate');
    if (r.ok) { cityLine.className = 'desc'; cityLine.textContent = ''; cityInput.value = ''; drawToday(); }
    else { btn.set(false); cityLine.className = 'error-text'; cityLine.textContent = tx('winLocFailed'); }
  }, 'winloc');
  winLoc.id = 'sw-winloc';
  const methods = (S.caps.methods || []).map((m, i) => [m.id, getLang() === 'ar' ? m.name : (['Umm al-Qura', 'Muslim World League', 'Egyptian authority', 'Karachi', 'North America'][i] ?? m.name)]);
  const methodValue = (methods.find(([id]) => id.toLowerCase() === String(v.prayerMethod ?? '').toLowerCase()) ?? methods[0] ?? [''])[0];
  const method = select('prayer-method', methods, methodValue, async (id) => { await set('prayerMethod', id); drawToday(); });
  const asrId = newId('as');
  const asr = segmented(asrId, tx('asrs'), v.prayerHanafiAsr ? 1 : 0, async (i) => { await set('prayerHanafiAsr', i === 1); drawToday(); }, 'prayer-asr');

  // التذكير
  const minId = newId('mn');
  const minutes = segmented(minId, MINUTES.map((m) => num(String(m))), Math.max(0, MINUTES.indexOf(v.prayerReminderMinutes)), (i) => set('prayerReminderMinutes', MINUTES[i]), 'prayer-minutes');
  const minutesRow = row(tx('minutesLabel'), minutes, { labelId: minId });
  minutesRow.id = 'prayer-minutes-row';
  minutesRow.hidden = v.prayerReminderOn === false;
  const remind = switchRow(tx('swRemind'), '', v.prayerReminderOn !== false, async (on, btn) => {
    const r = await set('prayerReminderOn', on);
    if (!r.ok) btn.set(!on);
    else minutesRow.hidden = !on;
  }, 'prayerReminderOn');
  remind.id = 'sw-remind';

  await drawToday();
  return [
    title(tx('pagePrayer')),
    sub(tx('prayerSub')),
    today,
    section(tx('groupPlace')),
    group(h('div', { class: 'row' }, cityLabel, cityInput), winLoc, row(tx('methodLabel'), method, { forId: 'prayer-method' }), row(tx('asrLabel'), asr, { labelId: asrId })),
    section(tx('groupRemind')),
    group(remind, minutesRow, note(tx('adhanNote'))),
  ];
}
