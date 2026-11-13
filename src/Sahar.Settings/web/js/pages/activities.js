// النشاطات (0.13; mockup PSettingsActivities): what she carries on her tag, the PC extras, music (lyrics, paused music),
// the weather's city (the prayer place when it has none: owner ruling 4), Claude Code's link (notify only: owner
// ruling 3 — no answering from her, so no answer switch and no waiting time), and the shelf.
import { h, title, sub, section, group, switchRow, segmented, row, note, ghost, input, newId } from '../controls.js';

const KEEP = ['tenMinutes', 'hour', 'always'];
const WINDOWS_LOCATION = 'موقع الويندوز';
const lower = (v) => String(v ?? '').toLowerCase();

export function render({ S, tx, set, ask, render: rerender }) {
  const v = S.value;
  const sw = (key, label, hint, on = v[key] !== false) => {
    const b = switchRow(tx(label), tx(hint), on, async (next, btn) => {
      const r = await set(key, next);
      if (!r.ok) btn.set(!next);
    }, key);
    b.id = `sw-${key}`;
    return b;
  };

  // الموسيقى: the lyrics switch is the consent too (its line says what goes out).
  const lyrics = switchRow(tx('swLyrics'), tx('swLyricsHint'), !!v.lyricsEnabled, async (on, btn) => {
    const r = await set('lyricsEnabled', on);
    if (!r.ok) { btn.set(!on); return; }
    if (on && !v.lyricsConsented) await set('lyricsConsented', true);
  }, 'lyricsEnabled');
  lyrics.id = 'sw-lyricsEnabled';
  const keepId = newId('kp');
  const keep = segmented(keepId, tx('keeps'), Math.max(0, KEEP.indexOf(lower(v.musicPausedKeep).replace('tenminutes', 'tenMinutes'))),
    (i) => set('musicPausedKeep', KEEP[i]), 'music-keep');

  // الطقس: its own city, or the prayer place's.
  const place = v.weatherCity
    ? v.weatherCity
    : v.prayerLatitude != null ? tx('weatherSamePrayer', { city: v.prayerCity || WINDOWS_LOCATION }) : tx('weatherNoCity');
  const cityLine = h('span', { class: 'desc', id: 'weather-city-line', 'aria-live': 'polite', text: place });
  const cityBox = h('div', { id: 'weather-city-box', hidden: true });
  const cityInput = input({ id: 'weather-city', placeholder: tx('cityPlaceholder'), label: tx('weatherCityLabel') });
  cityInput.addEventListener('keydown', async (e) => {
    if (e.key === 'Escape') { cityBox.hidden = true; change.focus(); return; }
    if (e.key !== 'Enter' || !cityInput.value.trim()) return;
    cityLine.className = 'skeleton';
    const r = await ask('city', { text: cityInput.value, for: 'weather' });
    if (r.ok) { cityLine.className = 'desc'; cityLine.textContent = r.name; cityBox.hidden = true; rerender(); }
    else { cityLine.className = 'error-text'; cityLine.textContent = r.reason || tx('engineOff'); }
  });
  cityBox.append(cityInput);
  const change = ghost(tx('weatherChange'), () => { cityBox.hidden = false; cityInput.focus(); }, { id: 'weather-change' });
  const back = v.weatherCity
    ? ghost(tx('weatherUsePrayer'), async () => { const r = await ask('weather-prayer'); if (r.ok) rerender(); }, { id: 'weather-prayer' })
    : null;
  const cityRow = h('div', { class: 'row' },
    h('span', { class: 'label-col' }, h('span', { class: 'label', text: tx('weatherCityLabel') }), cityLine),
    h('span', { class: 'btn-row' }, back, change));

  // Claude Code: linked or not is read from its own file each time.
  const linkLabel = h('span', { class: 'label', id: 'claude-link-label', text: tx('claudeUnlinked') });
  const linkHint = h('span', { class: 'desc', id: 'claude-link-hint', 'aria-live': 'polite', text: tx('claudeUnlinkedHint') });
  const linkBtn = ghost(tx('claudeLink'), toggle, { id: 'claude-link' });
  let linked = false;
  function show(on) {
    linked = on;
    linkLabel.textContent = tx(on ? 'claudeLinked' : 'claudeUnlinked');
    linkHint.textContent = tx(on ? 'claudeLinkedHint' : 'claudeUnlinkedHint');
    linkBtn.textContent = tx(on ? 'claudeUnlink' : 'claudeLink');
  }
  async function toggle() {
    linkBtn.disabled = true;
    const r = await ask(linked ? 'claude-unlink' : 'claude-link');
    linkBtn.disabled = false;
    if (r.ok) show(!!r.linked);
    else { linkHint.className = 'error-text'; linkHint.textContent = tx(r.error === 'invalid' ? 'claudeInvalid' : 'claudeFailed'); }
  }
  ask('claude-status').then((r) => { if (r.ok) show(!!r.linked); else { linkBtn.disabled = true; linkHint.textContent = tx('claudeNoEngine'); } });
  const linkRow = h('div', { class: 'row' }, h('span', { class: 'label-col' }, linkLabel, linkHint), linkBtn);

  return [
    title(tx('pageActivities')),
    sub(tx('activitiesSub')),
    section(tx('groupCarry')),
    group(sw('actMusic', 'swMusic', 'swMusicHint'), sw('actWeather', 'swWeather', 'swWeatherHint'),
      sw('actClaude', 'swClaude', 'swClaudeHint'), sw('actShelf', 'swShelf', 'swShelfHint')),
    section(tx('groupDevice')),
    group(sw('actSystem', 'swSystem', 'swSystemHint'), sw('actMicCam', 'swMicCam', 'swMicCamHint'), sw('actCopy', 'swCopy', 'swCopyHint'),
      sw('actAway', 'swAway', 'swAwayHint'), sw('actScreenTime', 'swTime', 'swTimeHint', !!v.actScreenTime)),
    section(tx('groupMusic')),
    group(lyrics, row(tx('keepLabel'), keep, { labelId: keepId })),
    section(tx('groupWeather')),
    group(cityRow, cityBox, note(tx('weatherNetNote'))),
    section(tx('pageClaude')),
    group(linkRow, note(tx('claudeNote'))),
    section(tx('groupShelf')),
    group(sw('shelfScreenshots', 'swShots', 'swShotsHint')),
  ];
}
