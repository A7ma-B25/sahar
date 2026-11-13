// سحورة (plan 0.10c Task 6; mockup PSettingsSoul): her preview, how she talks to you, her size and moves, what she
// does by herself, and her look. Every change saves at once, she hops in the preview and says a line.
import { h, title, sub, section, group, row, segmented, switchRow, input, newId } from '../controls.js';
import { mount } from '../sahar-preview.js';

const SIZES = ['small', 'medium', 'large'];
const GENDERS = ['male', 'female', 'unknown'];
const ACTIVITIES = ['calm', 'medium', 'lively'];
const PREVIEW_PX = [110, 160, 210];
const lower = (v) => String(v ?? '').toLowerCase();

let preview = null;
let nameTimer = 0;

export function leave() {
  if (preview) preview.destroy();
  preview = null;
  clearTimeout(nameTimer);
}

export async function render({ S, tx, set, ask, render: rerender, reduced }) {
  const v = S.value;
  const size = Math.max(0, SIZES.indexOf(lower(v.size)));
  const gender = Math.max(0, GENDERS.indexOf(lower(v.userGender)));
  const activity = Math.max(0, ACTIVITIES.indexOf(lower(v.activity)));
  const caption = h('span', { class: 'caption', id: 'preview-caption', 'aria-live': 'polite', text: tx('captionStart') });
  const stage = h('div', { class: 'preview-stage', id: 'preview-stage' });
  const box = h('section', { class: 'preview', 'aria-label': tx('previewLabel') }, stage, caption);

  // Every change: she hops and says a line in her voice (spec §6).
  const react = (line) => {
    caption.textContent = line;
    preview?.hop(1);
  };
  const save = async (key, value, line) => {
    const r = await set(key, value);
    if (r.ok) react(line ?? tx('sayOther'));
    return r;
  };

  // كيف بحكي معك
  const name = input({ id: 'name', placeholder: tx('namePlaceholder'), value: v.userName ?? '' });
  const commitName = async () => {
    clearTimeout(nameTimer);
    const typed = name.value;
    if ((v.userName ?? '') === typed.trim()) return;
    await save('userName', typed);
  };
  name.addEventListener('input', () => { clearTimeout(nameTimer); nameTimer = setTimeout(commitName, 400); });
  name.addEventListener('blur', commitName);
  name.addEventListener('keydown', (e) => { if (e.key === 'Enter') commitName(); });
  const genderId = newId('lg');
  const genderSeg = segmented(genderId, tx('genders'), gender, async (i) => {
    const r = await set('userGender', GENDERS[i]);
    if (!r.ok) return;
    await rerender(true);   // her words to you change («اكتب اسمك» → «اكتبي اسمك»)
    const c = document.getElementById('preview-caption');
    if (c) c.textContent = tx('sayGender')[i];
    preview?.hop(1);
  }, 'gender');

  // حجمي وحركتي
  const sizeId = newId('ls');
  const sizeSeg = segmented(sizeId, tx('sizes'), size, async (i) => {
    preview?.setSize(PREVIEW_PX[i]);
    await save('size', SIZES[i], tx('saySize'));
  }, 'size');
  const activityLine = h('span', { class: 'desc', id: 'activity-line', text: tx('activityLines')[activity] });
  const actId = newId('la');
  const actSeg = segmented(actId, tx('activities'), activity, async (i) => {
    activityLine.textContent = tx('activityLines')[i];
    await save('activity', ACTIVITIES[i], tx('sayActivity')[i]);
  }, 'activity');
  const activityRow = h('div', { class: 'row' },
    h('span', { class: 'label-col' }, h('span', { class: 'label', id: actId, text: tx('activityLabel') }), activityLine), actSeg);

  // شو بعمل لحالي
  const flip = (key) => async (on, btn) => {
    const r = await save(key, on, on ? tx('sayOn') : tx('sayOff'));
    if (!r.ok) btn.set(!on);
  };
  const cityRow = h('div', { class: 'row', id: 'weather-city-row', hidden: !(v.weatherEnabled && !v.weatherLatitude) || null });
  const cityInput = input({ id: 'weather-city', placeholder: tx('cityPlaceholder'), value: v.weatherCity ?? '' });
  const cityLine = h('span', { class: 'desc', id: 'weather-city-line', 'aria-live': 'polite', text: v.weatherCity ?? '' });
  cityRow.append(h('span', { class: 'label-col' }, h('label', { class: 'label', for: 'weather-city', text: tx('cityLabel') }), cityLine), cityInput);
  const weather = switchRow(tx('swWeather'), tx('swWeatherHint'), !!v.weatherEnabled, async (on, btn) => {
    if (on && (v.weatherLatitude == null || v.weatherLongitude == null)) {
      cityRow.hidden = false;   // she needs your city first: the switch turns on once a city is found
      btn.set(false);
      cityInput.focus();
      return;
    }
    const r = await save('weatherEnabled', on, on ? tx('sayOn') : tx('sayOff'));
    if (!r.ok) btn.set(!on);
    else cityRow.hidden = !on;
  }, 'weatherEnabled');
  weather.id = 'sw-weather';
  cityInput.addEventListener('keydown', async (e) => {
    if (e.key !== 'Enter' || !cityInput.value.trim()) return;
    cityLine.textContent = tx('searching');
    cityLine.className = 'desc';
    const r = await ask('city', { text: cityInput.value, for: 'weather' });
    if (r.ok) {
      cityLine.textContent = r.name;
      weather.set(true);
      react(tx('sayOn'));
    } else {
      cityLine.textContent = r.reason ?? tx('engineOff');
      cityLine.className = 'error-text';
    }
  });
  const switches = [
    ['awarenessEnabled', 'swComments', 'swCommentsHint', 'sw-comments'],
    ['autoMood', 'swMood', 'swMoodHint', 'sw-mood'],
    ['playsAlone', 'swPlay', 'swPlayHint', 'sw-play'],
  ].map(([key, label, hint, id]) => {
    const b = switchRow(tx(label), tx(hint), v[key] !== false, flip(key), key);
    b.id = id;
    return b;
  });
  const stars = switchRow(tx('swStars'), tx('swStarsHint'), v.innerStars !== false, flip('innerStars'), 'innerStars');
  stars.id = 'sw-stars';

  const out = [
    title(tx('pageSahar')),
    sub(tx('saharSub')),
    box,
    section(tx('groupTalk')),
    group(
      row(tx('nameLabel'), name, { forId: 'name' }),
      row(tx('talkingTo'), genderSeg, { labelId: genderId })),
    section(tx('groupBody')),
    group(row(tx('sizeLabel'), sizeSeg, { labelId: sizeId }), activityRow),
    section(tx('groupAlone')),
    group(...switches, weather, cityRow),
    section(tx('groupLook')),
    group(stars),
  ];
  if (preview) preview.destroy();
  preview = await mount(stage, { size: PREVIEW_PX[size], caption, reduced });
  return out;
}
