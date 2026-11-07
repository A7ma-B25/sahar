# Sahar v1 Plan 5: prayer times and listening (music controls and a Quran player)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two more menu buttons work: 🕌 أوقات الصلاة (the next prayer and time left, today's times, a reminder before each prayer, quiet at adhan time) and 🎧 استماع (control whatever music plays in Windows, or play the Quran with a chosen reciter and surah, with Sahar calm and quiet while it plays); test build 0.6.0.

**Architecture:** Prayer times are computed on the PC in Core (the standard astronomical method used by PrayTimes/adhan libraries, implemented from the published formulas; no new dependency), with unit tests against published timetables. The city comes from the same Open-Meteo geocoding as the weather (typed once). Music control uses the Windows media session manager already used for sensing. The Quran plays with `Windows.Media.Playback.MediaPlayer` from the islamic.network CDN. Both tools reuse the list bubble and speech bubble.

**Tech Stack:** C# .NET 10 Native AOT, Windows.Media.Control and Windows.Media.Playback (CsWinRT), HttpClient (geocoding only), xUnit, PowerShell UI tests.

**Spec:** `docs/superpowers/specs/2026-11-06-sahar-windows-v1-design.md` — §7.1 (🎧 استماع, 🕌 أوقات الصلاة), §7.2 (prayer calculation, Quran source), §5 #19 (reminder), §8.2 (quiet: Quran, adhan), §14. Builds on Plan 4 (branch v1/plan-4).

## Global Constraints

- §7.2 prayer: "بتنحسب على الجهاز بلا نت … الموقع: من خدمة الموقع بالويندوز إذا سمحت، أو مدينة بتختارها. طريقة الحساب بتختارها (أم القرى، رابطة العالم الإسلامي، المصرية، …). النسخة 1 ما بتشغّل صوت أذان." Reminder before each prayer: default 10 minutes (§7.1). Adhan time: "سكوت تام ووقف التأثيرات".
- §7.1 listening: music = control any player (play/pause, next, previous) and show the song; Quran = built-in player, choose reciter and surah; "وقت القرآن سَحَر ما بتتمايل، وبتطفي تأثيراتها وأصواتها".
- §7.2 Quran source: free, with terms that allow use in other programs; otherwise open the website. Chosen: the islamic.network audio CDN, whose terms (alquran.cloud) say recitations are licensed "for free, non-commercial redistribution" and "You may stream, embed and download them for personal and educational use"; the CDN has no per-client throttle. Sahar is free and personal → allowed. No internet → a gentle message.
- The menu shows only working buttons: 🎧 and 🕌 join now. Order (spec §7.1, working ones only): timer, listen, prayer, note, clipboard, activity, dnd, hide, play.
- City for prayer: typed (the Windows location service needs a capability prompt; ruling: typed city only in v1, cost: one extra step). The PC's own time zone is used for the times (ruling: a city in another time zone shows that city's times in the PC's clock).
- Nothing leaves the PC except: the city name once to Open-Meteo geocoding; the Quran audio stream while playing.
- RAM at rest < 60 MB; CPU at rest < 0.5 %: prayer checks run once a minute; no timer faster at rest.
- Version 0.6.0.

## Review Focus

1. **Prayer times for far-north/far-south cities or dates where twilight never ends** (e.g. Oslo in June): no NaN or crash; Fajr/Isha fall back to the "angle-based / one-seventh of the night" rule; the list still shows. → Task 1 `PrayerTimes_HighLatitude_NoNaN`.
2. **Crossing midnight and the next day:** after Isha, the "next prayer" is tomorrow's Fajr with the right time left; reminders fire once per prayer per day, also after sleep. → Task 1 `NextPrayer_AfterIsha_IsTomorrowsFajr`, `PrayerReminders_OncePerPrayer`.
3. **Quran while music plays, or music starts during the Quran:** starting the Quran pauses the playing session; Sahar never dances during the Quran; stopping the Quran restores normal life. → Task 4 UI step.
4. **No internet for the Quran, or a reciter missing a surah:** a gentle message, no stuck "playing" state, no endless retry. → Task 4 UI step with an unreachable URL in test mode.
5. **Ramadan Isha for Umm al-Qura (120 min instead of 90):** honoured when the Hijri month is Ramadan (Umm al-Qura calendar from .NET `UmAlQuraCalendar`). → Task 1 `UmmAlQura_RamadanIsha120`.

---

### Task 1: Core prayer times

**Files:** Create `src/Sahar.Core/Prayer/PrayerTimes.cs`, `PrayerMethod.cs`, `PrayerSchedule.cs`; modify `SaharSettings.cs` (PrayerCity, PrayerLatitude, PrayerLongitude, PrayerMethod, PrayerAsrHanafi, PrayerReminderMinutes = 10); tests `tests/Sahar.Core.Tests/Prayer/PrayerTests.cs`.

**Interfaces (Produces):**
- `enum PrayerMethod { UmmAlQura, MuslimWorldLeague, Egyptian, Karachi, NorthAmerica }` with Fajr/Isha angles (18.5/90 min; 18/17; 19.5/17.5; 18/18; 15/15).
- `enum Prayer { Fajr, Sunrise, Dhuhr, Asr, Maghrib, Isha }`.
- `PrayerTimes.For(DateOnly date, double lat, double lon, TimeSpan utcOffset, PrayerMethod m, bool hanafiAsr) -> IReadOnlyDictionary<Prayer, DateTimeOffset>`.
- `PrayerSchedule` with `(Prayer Prayer, DateTimeOffset At)? Next(DateTimeOffset now)` (Sunrise excluded), `bool TakeReminder(DateTimeOffset now, int minutesBefore, out Prayer p)` (once per prayer per day), `bool TakeAdhan(DateTimeOffset now, out Prayer p)` (once, within 5 min after the time), `bool InAdhanQuiet(DateTimeOffset now)` (5 minutes from each prayer time).
- Arabic names: الفجر، الشروق، الظهر، العصر، المغرب، العشاء.

- [ ] **Step 1:** Failing tests (tolerance ±3 min): `UmmAlQura_Makkah_2025_07_05` (21.4151, 39.8782, UTC+3: Fajr 04:16, Sunrise 05:43, Dhuhr 12:25, Asr 15:43, Maghrib 19:07, Isha 20:37 — eSalah timetable), `UmmAlQura_Makkah_2026_01_01` (21.4225, 39.8262: 05:37, 06:59, 12:24, 15:29, 17:50, 19:20 — pray.zone); `MWL_Damascus_SensibleOrder` (all times increasing, Dhuhr within 10 min of solar noon); `Hanafi_AsrLater`; `UmmAlQura_RamadanIsha120`; `PrayerTimes_HighLatitude_NoNaN` (Oslo, 21 June); `NextPrayer_AfterIsha_IsTomorrowsFajr`; `PrayerReminders_OncePerPrayer`; `Adhan_QuietFiveMinutes`.
- [ ] **Step 2:** RED. **Step 3:** implement (Julian day → sun declination and equation of time; hour angle for an angle below the horizon; Asr by shadow factor 1 or 2; Maghrib = sunset; Umm al-Qura Isha = Maghrib + 90/120 min; high latitudes: night-fraction fallback when the angle never occurs). **Step 4:** GREEN. **Step 5:** commit `feat(core): prayer times`.

### Task 2: 🕌 Prayer in the menu, reminders and adhan quiet

**Files:** Create `src/Sahar.Engine/Tools/PrayerTool.cs`; modify `Core/Menu/MenuItems.cs` (prayer, listen), `App.cs`, `Host/Director.cs` (quiet `Adhan`), `tests/ui/run.ps1` (stage `senses` → a new stage `prayer`, in both workflows).
- 🕌 opens the list bubble: first line "الجاية: العصر بعد ساعة و12 دقيقة", then today's six times, and a button "المدينة: {city} — غيّر" (asks via the text bubble, geocoding like the weather; first use asks right away). Method: a second list from the button long… (ruling: the method is chosen in a list opened by a line "طريقة الحساب: أم القرى" in the prayer list).
- Every minute: `TakeReminder` → `Reminder` event (face #19) + Attention (Plan 3 pacing) + speech bubble "بعد 10 دقايق {الصلاة}"; `TakeAdhan` → quiet `Adhan` for 5 min (no comments, small things only, no attention).
- Test mode: `prayer-at {iso}` sets a fake clock; `debug-prayer` → `next=… minutes=… quiet=…`; `--prayer-stub` reuses `--weather-stub` geocode.json.
- [ ] **Step 1:** UI steps: first press asks the city → Damascus (stub) → list shows 6 times + next; fake clock 10 min before Asr → reminder face + bubble text; at Asr → `quiet=true`, life small things only; 6 min later quiet ends. Push → RED.
- [ ] **Step 2:** Implement → GREEN. **Step 3:** commit `feat(tools): prayer times, reminders, adhan quiet`.

### Task 3: 🎧 Music controls

**Files:** Create `src/Sahar.Engine/Tools/MusicTool.cs`; modify `Senses/Media.cs` (expose the current session's title/artist, play/pause/next/previous), `App.cs`, `tests/ui/run.ps1`.
- 🎧 → sub-ring 🎵 موسيقى / 📖 قرآن / ↩️. 🎵 → a sub-ring ⏮️ السابق / ⏯️ تشغيل-إيقاف / ⏭️ التالي / ↩️ and a speech bubble "🎵 {title} — {artist}" (or "ما في شي عم يشتغل"). Song title/artist are shown, never logged.
- Test mode: `debug-music` → `session=true|false`; the step uses a small PowerShell `MediaPlayer` playing a bundled silent WAV through SMTC? (Ruling: CI has no media session; the UI step checks the sub-ring and the "nothing playing" bubble only; real control is a manual check in the README.)
- [ ] **Step 1:** UI steps: 🎧 → ids `music,quran,back`; 🎵 → `music-prev,music-toggle,music-next,back`; bubble says nothing is playing. Push → RED. **Step 2:** implement → GREEN. **Step 3:** commit `feat(tools): music controls`.

### Task 4: 📖 Quran player

**Files:** Create `src/Sahar.Core/Quran/Quran.cs` (114 surah names in Arabic, reciters `ar.alafasy` مشاري العفاسي, `ar.abdulbasitmurattal` عبد الباسط, `ar.husary` الحصري, `ar.minshawi` المنشاوي, `ar.mahermuaiqly` ماهر المعيقلي, `ar.abdurrahmaansudais` السديس; `Url(reciter, surah) => https://cdn.islamic.network/quran/audio-surah/128/{reciter}/{surah}.mp3`), `src/Sahar.Engine/Tools/QuranTool.cs`; modify `App.cs`, `Director.cs` (quiet `Quran`), `Life/LifeDirector.cs` (no dance, small things only), expressions (a "quran" face: calm, eyes closed, headphones, 📖 orb — from the listening board), `tests/ui/run.ps1`; Core tests `QuranTests`.
- 📖 → list of 114 surahs (the last one played first), button "القارئ: {name}" → reciter list. Play: pause any playing music session first; quiet `Quran`; the quran face; she stays still (no sway, no dance, no attention, no comments). Menu 🎧 while playing → `quran-stop` (⏹️ وقّف القرآن). End of surah → back to normal. Errors (no network, 404) → speech bubble "ما قدرت شغّل التلاوة هلق" and normal state.
- Test mode: `quran-play {surah} {reciter}`, `debug-quran` → `playing=… surah=… state=…`; `--quran-base <url>` overrides the CDN base (an unreachable URL for the failure step).
- [ ] **Step 1:** Core `Quran_114Surahs`, `Quran_Url`. UI: play with an unreachable base → message bubble, `playing=false`, quiet off; play with the real CDN? (Ruling: CI does not stream from the CDN — the network test uses the unreachable base only; streaming is a manual check.) Quiet + face + no dance while "playing" is checked with `--quran-base` pointing to a local file URL of a bundled 3 s silent mp3 under `tests/ui/assets/`. Push → RED.
- [ ] **Step 2:** Implement → GREEN. **Step 3:** commit `feat(tools): Quran player`.

### Task 5: Test build 0.6.0

`CoreInfo_Version_Is_0_6_0`; README what's new (prayer, listening; the Quran source and its terms; manual checks: your city's times vs your mosque, a reminder, a real song's controls, a Quran surah with headphones on); all stages green; zip check. Commit `feat: test build 0.6.0`. Publishing waits for the user's OK.
