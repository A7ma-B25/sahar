# Sahar v1 Plan 2a — Daily Life and Local Smarts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sahar lives on her own: she picks something to do every so often (by activity level), keeps her eyes on the mouse, stays out of the way while you type or watch, notices music, long work and late nights, and learns which of her actions you like — all on the machine, no network.

**Architecture:** Pure decision logic in `Sahar.Core/Life` (timing, choosing, preferences, routine, typing, media edges, persistence) with xUnit tests on Linux. A `LifeDirector` in the engine feeds it senses once a second and turns its decisions into the existing motions (Plan 1 Task 11) and brain events (Plan 1 Task 6). Activity level lives in `settings.json` and is chosen from the tray.

**Tech Stack:** C# .NET 10, Native AOT, Windows.UI.Composition (existing), `GlobalSystemMediaTransportControlsSessionManager` (CsWinRT projection), `GetGUIThreadInfo`, System.Text.Json source generation.

**Spec:** `docs/superpowers/specs/2026-11-06-sahar-windows-v1-design.md` (§6 motion rules and activity level, §8.1 senses, §8.2 brain, §14 privacy). Builds on `docs/superpowers/plans/2026-11-06-sahar-v1-plan-1-alive.md` (done, merged).

## Global Constraints

- Everything from Plan 1's Global Constraints still holds (AOT, source-generated JSON only, pipe `sahar`, mutex, 60 MB / 0.5 % CPU at rest, English code, Arabic-first UI text, commit trailer).
- Activity level (spec §6): Calm = "بتتحرك نادراً", Medium = default, Lively. Gaps between her own actions: Calm 5–10 min, Medium 1–3 min, Lively 30–90 s (approved by the user 2026-11-06).
- She never covers what matters (spec §6): no own actions while the user types or watches video; she moves away from the typing spot.
- Privacy (spec §14): no screenshots, no window contents, no app names or titles stored. `life.json` holds only action weights and a 24-hour activity profile. Data under `%AppData%\Sahar\`.
- No network access in Plan 2a code.
- Learning file: `%AppData%\Sahar\life.json`, fixed size (one weight per action, 24 hour buckets).
- Tray text: "نشاطها / Activity" with "هادية / Calm", "متوسطة / Medium", "نشيطة / Lively".

## Review Focus

1. **Days without a restart:** her own actions keep coming at the chosen rate and `life.json` never grows. → Task 2 test `LifeStore_SizeIsFixed`; Task 7 `life` stage runs ≥ 10 own actions.
2. **Clock jumps (sleep/resume, timezone, DST):** a jump never counts as two hours of work or a second late night. → Task 2 tests `Routine_ClockJump_NoBreak`, `Routine_LateNight_OncePerNight`.
3. **The user grabs her mid-action:** the action stops, counts as disliked, and she does not start another until the next gap. → Task 5 test step in the `life` stage ("drag during an own action").
4. **Corrupt or locked `life.json` / `settings.json`:** defaults, no crash, the bad file kept as `.bad`. → Task 2 test `LifeStore_Corrupt_FallsBackToDefaults`.
5. **Typing in an app without a system caret (browsers, Electron):** typing is still detected from keyboard-only input and she still steps away, using the cursor as the typing spot. → Task 2 test `Typing_KeyboardWithoutCursor_IsTyping`; Task 6 falls back to the cursor rect.

---

### Task 1: When and what — `LifeTiming` and `LifeChooser`

**Files:** Create `src/Sahar.Core/Life/LifeTiming.cs`, `src/Sahar.Core/Life/LifeChooser.cs`; Test `tests/Sahar.Core.Tests/Life/LifeChooserTests.cs`

**Interfaces:**
- Consumes: `ActivityLevel` (Plan 1 Task 4).
- Produces:
  - `public enum LifeAction { None, Wander, PeekTaskbar, PerchWindow, CelebrateLoop, SleepInCorner }`
  - `public static TimeSpan LifeTiming.NextGap(ActivityLevel level, Random rng, float routineFactor = 1f)` — uniform in Calm [5, 10] min, Medium [1, 3] min, Lively [30, 90] s, multiplied by `routineFactor` clamped to [1, 2].
  - `public sealed record LifeContext(bool Typing, bool VideoPlaying, bool MusicPlaying, bool UserAway, bool Hidden, bool Busy, int Hour, bool HasPerchableWindow)`
  - `public static LifeAction LifeChooser.Choose(LifeContext c, Func<LifeAction, float> learnedWeight, Random rng)` — `None` when Typing, VideoPlaying, UserAway, Hidden or Busy. Base weights: Wander 4, PeekTaskbar 2, PerchWindow 2 (0 without a perchable window), CelebrateLoop 1 (3 while music plays), SleepInCorner 0. Hours 0–4: SleepInCorner 2, CelebrateLoop 0. Each base weight × `learnedWeight(action)`; weighted random draw.
- [ ] **Step 1:** Tests: `NextGap_InRange_ForEachLevel` (1 000 draws per level inside the ranges above), `NextGap_RoutineFactor_StretchesAndClamps` (factor 1.5 → Medium in [1.5, 4.5] min; factor 5 → treated as 2), `Choose_Typing_Video_Away_Hidden_Busy_None` (each flag alone → `None`), `Choose_NoPerchableWindow_NeverPerch` (2 000 draws), `Choose_LateNight_SleepsNeverCelebrates` (Hour 2, 2 000 draws: SleepInCorner seen, CelebrateLoop never), `Choose_Music_CelebratesMore` (CelebrateLoop share with music > 1.8 × without), `Choose_LearnedWeight_ShiftsChoice` (Wander weight 3 vs 0.25 → Wander share at least 4× higher). All with `new Random(seed)`.
- [ ] **Step 2–5:** FAIL → implement → PASS (`dotnet test tests/Sahar.Core.Tests`) → commit `feat(core): life timing and action chooser`.

### Task 2: Learning and routine — `Preferences`, `Routine`, `TypingDetector`, `MediaEdges`, `LifeStore`

**Files:** Create `src/Sahar.Core/Life/Preferences.cs`, `Routine.cs`, `TypingDetector.cs`, `MediaEdges.cs`, `LifeStore.cs`; Modify `src/Sahar.Core/Json/CoreJson.cs` (register `LifeState`); Tests `tests/Sahar.Core.Tests/Life/LearningTests.cs`

**Interfaces:**
- Consumes: `LifeAction` (Task 1), `EventKind` (Plan 1 Task 6).
- Produces:
  - `public sealed class Preferences { float Weight(LifeAction a); void Liked(LifeAction a); void Disliked(LifeAction a); void Decay(); }` — start 1.0; liked × 1.25, disliked × 0.7, clamped [0.25, 3]; `Decay()` moves each weight 5 % toward 1 (called once a day).
  - `public sealed class Routine { void Tick(DateTimeOffset now, bool active); bool TakeBreakDue(); bool TakeLateNightDue(); float BusyFactor(int hour); }` — active time accumulates while `active`; an inactive stretch ≥ 5 min, or a clock jump > 5 min between ticks, resets it; ≥ 2 h continuous → break due once, then the counter restarts. Late night due once per night when active between 00:00 and 04:59 local (the night is identified by the date at 05:00 the next morning). Hour profile: 24 floats, each updated by EMA α = 0.1 with 1/0 at the end of each local hour; `BusyFactor(h) = 1 + profile[h]`.
  - `public sealed class TypingDetector { bool Update(DateTimeOffset now, TimeSpan sinceInput, bool cursorMoved); }` — input within 1.5 s with no cursor movement = keyboard; stays "typing" until 3 s after the last keyboard input.
  - `public sealed class MediaEdges { IReadOnlyList<EventKind> Update(bool playing, bool isVideo); bool VideoPlaying { get; } }` — music starts → `[Music]`; music stops or turns into video → `[MusicStop]`; video only sets `VideoPlaying`.
  - `public sealed record LifeState(int Version, Dictionary<string, float> Weights, float[] Hours)`; `public static class LifeStore { string DefaultPath; LifeState Load(string path); void Save(string path, LifeState s); }` — same corrupt-file rule as `SettingsStore` (`.bad` copy, defaults); unknown action names ignored; `Hours` length fixed at 24. `Preferences` and `Routine` gain `ToState`/`FromState` members used by the store.
- [ ] **Step 1:** Tests: `Preferences_Liked_IncreasesAndCaps` (10 likes → 3), `Preferences_Disliked_DecreasesAndFloors` (10 dislikes → 0.25), `Preferences_Decay_MovesTowardOne`; `Routine_TwoHoursActive_BreakOnce` (ticks every 1 min for 120 min → due once; 30 more min → not due again), `Routine_FiveMinuteGap_Resets` (110 min, 6 min inactive, 20 min → not due), `Routine_ClockJump_NoBreak` (60 min, jump +3 h, 30 min → not due), `Routine_LateNight_OncePerNight` (active 00:30 → due; 01:30 → not; next night 00:10 → due), `Routine_Profile_LearnsWorkHours` (10 days active at 10:00, idle at 22:00 → `BusyFactor(10) > 1.6`, `BusyFactor(22) < 1.1`); `Typing_KeyboardWithoutCursor_IsTyping`, `Typing_MouseMovement_IsNotTyping`, `Typing_HoldsThreeSeconds`; `Media_MusicStartStop_Edges`, `Media_Video_NoMusicEvents_SetsVideo`, `Media_MusicToVideo_StopsMusic`; `LifeStore_RoundTrip`, `LifeStore_Corrupt_FallsBackToDefaults` (bad JSON → defaults + `.bad` exists), `LifeStore_SizeIsFixed` (after 1 000 likes and 100 days of profile the saved file is under 2 KB and `Hours.Length == 24`).
- [ ] **Step 2–5:** FAIL → implement → PASS → commit `feat(core): preferences, routine, typing and media edges`.

### Task 3: Senses for daily life — foreground, caret, media, settings

**Files:** Create `src/Sahar.Engine/Senses/ForegroundInfo.cs`, `src/Sahar.Engine/Senses/Media.cs`; Modify `src/Sahar.Engine/Interop/Native.cs`, `src/Sahar.Engine/Host/Tray.cs`, `src/Sahar.Engine/App.cs`

**Interfaces:**
- Consumes: `SettingsStore`, `SaharSettings.Activity` (Plan 1 Task 4); `MediaEdges` (Task 2).
- Produces:
  - `internal static ForegroundInfo.Current() → (nint Hwnd, RectI Window, RectI? Caret, bool Perchable)` — caret from `GetGUIThreadInfo(threadOfForeground)` (`rcCaret` mapped with `ClientToScreen` from `hwndCaret`; null when `hwndCaret == 0`); `Perchable` uses the Plan 1 `PerchWindow` rules (visible, not minimized/maximized, not ours or the shell).
  - `internal sealed class Media` — polls `GlobalSystemMediaTransportControlsSessionManager.GetCurrentSession()` every 2 s on the UI thread (manager requested once at start; failures logged and media treated as idle); playing = `PlaybackStatus == Playing`, video = `PlaybackType == Video`; feeds `MediaEdges` and posts its events to the `Director`; exposes `bool VideoPlaying`.
  - `Tray` submenu "نشاطها / Activity" with three radio items (checked = current) raising `event Action<ActivityLevel>? ActivityChosen`.
  - App loads `SettingsStore.Load(SettingsStore.DefaultPath)` at start; on `ActivityChosen` saves `settings with { Activity = level }` and tells the life director.
- [ ] **Step 1:** `dotnet build src/Sahar.Engine -c Release` → 0 warnings. The caret and the activity setting are exercised end to end by the `life` stage (Tasks 5–6); `MediaEdges` is unit-tested in Task 2, and live media playback has no source on the CI runner, so it is a manual check in the Task 7 README.
- [ ] **Step 2:** Commit `feat(engine): foreground caret, media and activity setting`.

### Task 4: Eyes on the mouse whenever she hovers

**Files:** Modify `src/Sahar.Engine/Motion/MotionController.cs`; Modify `tests/ui/run.ps1` (stage `motion`)

**Interfaces:**
- Produces: `Hover` keeps eye-following on (Plan 1 Task 11 `EyesFollow` behaviour); `EyesFollow` stays a valid pipe motion name. Pupil polling 33 ms while the cursor moves, 250 ms otherwise (unchanged).
- [ ] **Step 1:** In stage `motion`, add: after `motion Hover`, `agent-desktop --headed mouse-move` to the far left then far right of her; `debug-info` reports `faceOffsetX=<px>`; assert the sign follows the cursor side. Push → FAIL (no `faceOffsetX`, eyes still in Hover).
- [ ] **Step 2:** Implement: `Reset(Hover)` sets eyes on; `debug-info` adds `faceOffsetX`.
- [ ] **Step 3:** Push → PASS; `perf` stage still < 0.5 % CPU on the runner.
- [ ] **Step 4:** Commit `feat(engine): her eyes follow the mouse while she hovers`.

### Task 5: `LifeDirector` — she acts on her own and learns

**Files:** Create `src/Sahar.Engine/Life/LifeDirector.cs`; Modify `App.cs`, `Input/InputController.cs` (raise `Poked`/`DragStarted`), `Motion/MotionController.cs` (`event Action<MotionKind>? Finished` when a motion returns to Hover); Modify `tests/ui/run.ps1` (new stage `life`)

**Interfaces:**
- Consumes: Tasks 1–3; `MotionController.Play`, `Director.Post/CurrentKey`, `Idle`.
- Produces: `internal sealed class LifeDirector` ticking once a second (DispatcherQueueTimer, wrapped like Plan 1's `Safe`):
  - Feeds `Routine.Tick(now, sinceInput < 60 s)`, `TypingDetector`, media; posts `EventKind.Break` when `TakeBreakDue()` and `EventKind.LateNight` when `TakeLateNightDue()`.
  - When the gap (from `LifeTiming.NextGap(level, rng, routine.BusyFactor(hour))`) has passed and `motion.Current is Hover`, chooses with `LifeChooser.Choose` and plays `Wander | PeekTaskbar | PerchWindow | CelebrateLoop | SleepInCorner`; `None` re-rolls at the next tick without resetting the gap.
  - Brain key `sleep` (user away) → `SleepInCorner`; leaving it → `Wake`.
  - Learning: a poke within 15 s after an own action → `Liked(action)`; a drag that starts during or within 15 s after it → `Disliked(action)`, the action stops (drag wins) and the gap restarts. `Preferences.Decay()` once per local day.
  - Saves `life.json` every 10 min and on exit.
  - Test-mode pipe: `life` (`gap` seconds override, `now` = act at the next tick), `debug-life` → `level=… next=… last=… typing=… video=… weights=Wander:1.00,…`, `set-activity` (`level`) → same path as the tray item.
- [ ] **Step 1:** Stage `life`: (a) `life gap=2` → within 30 s at least 10 different `state.motion` changes away from `Hover` with no motion commands, each returning to `Hover`; (b) "drag during an own action": `life now`, wait until `state.motion != Hover`, drag her (`agent-desktop --headed drag`) → `debug-life` shows that action's weight < 1.00 and no own action for 2 s after release; (c) `set-activity level=Lively` → `%AppData%\Sahar\settings.json` has `"activity": "lively"`; quit, start again → `debug-life` `level=Lively`; (d) `life.json` exists after quit. Push → FAIL.
- [ ] **Step 2:** Implement.
- [ ] **Step 3:** Push → `life` PASS; all other stages still pass.
- [ ] **Step 4:** Commit `feat(engine): she lives on her own and learns what you like`.

### Task 6: She steps away from where you type

**Files:** Modify `src/Sahar.Engine/Life/LifeDirector.cs`, `src/Sahar.Engine/Motion/MotionController.cs` (`Func<IReadOnlyList<RectF>>? ExtraAvoid` used by `StartWander`); Modify `tests/ui/run.ps1` (stage `life`)

**Interfaces:**
- Produces: when typing starts and her body is within 120 DIP of the typing spot (caret rect, else the cursor rect), she plays `Wander` with `ExtraAvoid` = typing spot inflated by 150 DIP plus the foreground window's top 25 % (title bar and toolbar); while typing continues no own actions start.
- [ ] **Step 1:** Stage `life`: start `notepad.exe`, click into it, read the caret from `debug-life` (`caret=x,y`), `pose` her so her body centre is 40 px from the caret, type for 3 s with `[System.Windows.Forms.SendKeys]::SendWait` → `debug-life typing=true`; within 6 s her body centre is > 150 px from the caret; no `state.motion` other than `Wander`/`Hover` while typing continues for 5 more s. Push → FAIL.
- [ ] **Step 2:** Implement; `debug-life` adds `caret=`.
- [ ] **Step 3:** Push → PASS.
- [ ] **Step 4:** Commit `feat(engine): she steps away from where you type`.

### Task 7: Test build 0.2.0

**Files:** Modify `Directory.Build.props` (Version 0.2.0), `src/Sahar.Core/CoreInfo.cs`, `.github/workflows/build.yml` (version check reads `CoreInfo`'s value), `.github/workflows/test-build.yml` (zip and title from the version in `Directory.Build.props`, stages include `life`), `packaging/README-test-build.txt` (what is new: she lives on her own, activity levels from the tray, she learns what you like, how to reset: delete `%AppData%\Sahar\life.json`; manual checks: typing in a browser, music in Spotify/YouTube, two hours of work → break face)

- [ ] **Step 1:** Push → `build`, `ui-test` (all stages incl. `life`) and `test-build` (artifact only) PASS; zip contains exactly `Sahar.Engine.exe` and `README-test-build.txt`; perf stage < 60 MB.
- [ ] **Step 2:** Commit `ci: test build 0.2.0`. Publishing the pre-release waits for the user's OK (same rule as Plan 1).
