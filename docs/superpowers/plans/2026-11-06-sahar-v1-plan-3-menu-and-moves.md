# Sahar v1 Plan 3: the radial menu, its tools, and the last 8 moves

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Double-click or right-click Sahar opens her radial menu with the six buttons that work in v1 so far (timer, quick note, activity, do-not-disturb, hide 1 h, play); the remaining motions 13, 14, 17, 18, 19, 20, 23, 25 are built and triggered; a tray item "فرجيني حركاتك" plays everything she can do; test build 0.4.0.

**Architecture:** Every decision that can be pure lives in `Sahar.Core` with unit tests: menu layout and items, timer and its typed input, notes store, do-not-disturb state, shy detector, attention pacing, showcase list. The engine draws the menu on the existing Composition overlay (button surfaces drawn with D2D/DWrite, spring pop-in on the compositor), catches clicks with one transparent full-screen window while the menu is open, and uses a small Win32 popup with an EDIT control for typing (timer minutes, notes). UI tests drive everything through new test-mode pipe commands plus real clicks and keys.

**Tech Stack:** C# .NET 10, Native AOT, Windows.UI.Composition (CsWinRT), raw COM D2D/DWrite, Win32 (EDIT control, SetWindowRgn), xUnit, PowerShell + agent-desktop 1.2.0 on windows-latest.

**Spec:** `docs/superpowers/specs/2026-11-06-sahar-windows-v1-design.md` — §5 (#5 timer-done, #6 celebrate, #13 notification, #14 dnd), §6 motions 13, 14, 17, 18, 19, 20, 23, 25 and the motion rules, §7 and §7.1 (menu), §8.2 (quiet rules), §10, §14. Boards: "القائمة الدائرية" (`/home/claude/gen/menu.py`), "حركة سَحَر" (`/home/claude/gen/motion.py`). Builds on Plans 1, 2a, 2b (merged; 0.3.0 published).

## Global Constraints

- The user's choice (2026-11-06): **buttons of later plans are not shown at all** (اسأل Claude، استماع، أوقات الصلاة، الحافظة، الإعدادات). The menu shows exactly: ⏱️ مؤقت، 📝 ملاحظة سريعة، 🌗 مستوى نشاطها، 🔕 لا تزعجني، 😴 خبّيها ساعة، 🎲 العبي — in that order, clockwise from the top.
- §7: "كبستين أو كليك يمين: بتفتح القائمة الدائرية." "الأزرار بتطلع بنابض واحد ورا التاني. قرب حافة الشاشة بتصير نص دائرة. بتتسكّر بـ Esc أو بكبسة برا."
- §7.1 timer: "5 / 15 / 25 / 45 دقيقة أو وقت بتكتبه. حلقة الوقت حوالين سَحَر. لما يخلص: تعبير المؤقت وصوت" — sound comes with the sound plan; this plan shows the expression only.
- §7.1 note: "فقاعة كتابة صغيرة، Enter للحفظ، وآخر 3 ملاحظات. محفوظة محلياً" → `%AppData%\Sahar\notes.json`.
- §7.1 dnd: "بتسكت وبتوقف التعليقات لوقت بتختاره"; hide: "بتختفي ساعة، أو لحد ما تكبس على أيقونتها جنب الساعة"; play: "حركة عشوائية حلوة من الحركات الـ25".
- §5: transitions 250 ms, no sudden jumps. §6: calm, slow; never covers the pointer or the typing area.
- §10: RAM at rest < 60 MB; CPU at rest < 0.5 %; CPU while moving < 3 %; 60 fps only while physics runs. Spring and ring animations run on the compositor.
- §14: nothing leaves the machine; notes stay in `%AppData%\Sahar\`.
- Every engine window stays excluded from capture (WDA_EXCLUDEFROMCAPTURE) unless `--capturable`.
- UI is Arabic (the language setting arrives with the settings plan); tray items stay bilingual "عربي / English" as today.
- No credits are spent by anything in this plan.
- Version 0.4.0 in `Directory.Build.props`, `CoreInfo`, `app.manifest`, smoke test, README.

## Review Focus

1. **Menu at a screen edge, a corner, or on a second monitor with a different DPI:** every button and label lies fully inside her monitor's work area. → Task 1 `RadialLayout_*` tests from all four corners at 100 % and 150 %; Task 2 UI step opens the menu with her in each corner.
2. **The timer across sleep, lock, or a clock change:** it ends at its wall-clock end; if that passed while the PC slept, it fires right after resume, once. → Task 1 `FocusTimer_*` tests; Task 3 re-checks on power resume and unlock.
3. **Typed minutes in Arabic-Indic or Persian digits, spaces, zero, huge numbers, letters:** "٢٥"، "۲۵"، " 25 " → 25; "0"، "abc"، "" → rejected; above 600 → rejected. → Task 1 `TimerInput_*` tests.
4. **Something happens while the menu is open** (fullscreen app starts, hide, a drag starts, an important event): the menu closes on hide/fullscreen; events still change her face; she can't be dragged while the menu is open. → Task 2 UI step "the menu closes when she hides".
5. **notes.json corrupt, locked, or a very long / multi-line note:** corrupt → `.bad` copy and an empty list; locked → note kept in memory and a log line; a note is one line, trimmed, at most 500 characters. → Task 1 `NotesStore_*` tests.

---

### Task 1: Core models for the menu and its tools

**Files:**
- Create: `src/Sahar.Core/Menu/MenuItems.cs`, `src/Sahar.Core/Menu/RadialLayout.cs`, `src/Sahar.Core/Tools/FocusTimer.cs`, `src/Sahar.Core/Tools/TimerInput.cs`, `src/Sahar.Core/Tools/NotesStore.cs`, `src/Sahar.Core/Tools/DndState.cs`
- Modify: `src/Sahar.Core/Brain/Brain.cs` (add `End`), `src/Sahar.Core/Brain/EventKind.cs` (nothing new unless needed)
- Test: `tests/Sahar.Core.Tests/Menu/MenuTests.cs`, `tests/Sahar.Core.Tests/Tools/ToolsTests.cs`, `tests/Sahar.Core.Tests/Brain/BrainEndTests.cs`

**Interfaces:**
- Produces:
  - `record MenuItem(string Id, string Emoji, string Label)`; `static class MenuItems` with `IReadOnlyList<MenuItem> Main` (ids `timer`,`note`,`activity`,`dnd`,`hide`,`play` with emoji ⏱️📝🌗🔕😴🎲 and labels `مؤقت`,`ملاحظة سريعة`,`مستوى نشاطها`,`لا تزعجني`,`خبّيها ساعة`,`العبي`) and `IReadOnlyList<MenuItem> Options(string id, bool timerRunning, bool dndOn, ActivityLevel level)`:
    - `timer`: running → `timer-cancel` (⏹ `وقّف المؤقت`) + `back`; else `timer-5`,`timer-15`,`timer-25`,`timer-45` (labels `5 د`… ) + `timer-custom` (✏️ `وقت تاني`) + `back` (↩ `رجوع`).
    - `activity`: `activity-calm` (`هادية`), `activity-medium` (`متوسطة`), `activity-lively` (`نشيطة`) + `back`; the current level's label gets a leading `✓ `.
    - `dnd`: on → `dnd-off` (🔔 `رجّعي`) + `back`; off → `dnd-30` (`نص ساعة`), `dnd-60` (`ساعة`), `dnd-120` (`ساعتين`), `dnd-on` (`لحد ما وقّفها`) + `back`.
    - other ids → empty (they act directly).
  - `record ButtonSlot(string Id, Vector2 Centre, float Radius)` (px); `static class RadialLayout` with `IReadOnlyList<ButtonSlot> Place(IReadOnlyList<MenuItem> items, RectF body, RectF work, float dpiScale)`.
  - `sealed class FocusTimer` with `void Start(TimeSpan length, DateTimeOffset now)`, `void Cancel()`, `bool Running`, `TimeSpan Length`, `TimeSpan Remaining(DateTimeOffset now)`, `bool TakeDone(DateTimeOffset now)` (true once when the end has passed), `bool IsLong` (Length ≥ 25 min).
  - `static class TimerInput` with `int? ParseMinutes(string text)`.
  - `sealed record Note(string Text, DateTimeOffset At)`; `sealed class NotesStore(string path)` with `IReadOnlyList<Note> Latest(int n)`, `bool Add(string text, DateTimeOffset now)` (false if empty after cleanup), `static string DefaultPath` = `%AppData%\Sahar\notes.json`.
  - `sealed class DndState` with `void TurnOn(TimeSpan? length, DateTimeOffset now)` (null = until turned off), `void TurnOff()`, `bool IsOn(DateTimeOffset now)`, `bool TakeEnded(DateTimeOffset now)` (true once when a timed DND runs out).
  - `Brain.End(EventKind k)` removes an active event (a poke dismisses timer-done).

- [ ] **Step 1: Write the failing tests.**
  - `RadialLayout_SixButtons_FullRingAroundHer_WhenThereIsRoom`: body (900,500,80,80) in work (0,0,1920,1040), dpi 1 → 6 slots, all centres the same distance from body centre (±1 px), radius ≥ 90, first slot straight above the centre (x within 1 px), order clockwise.
  - `RadialLayout_EveryButtonInsideWork_FromEachCorner` (Theory over the 4 corners × dpi 1 and 1.5): each slot's circle plus a 24 DIP label band below lies inside work.
  - `RadialLayout_AtAnEdge_IsAHalfRingFacingIn`: body hugging the right edge → all slot centres have x < body centre x; spread ≤ 180°.
  - `RadialLayout_ButtonsNeverOverlap`: for counts 2..7, distance between neighbouring centres ≥ 2 × radius + 8 DIP.
  - `MenuItems_Main_IsExactlyTheSixWorkingButtons` (ids and order); `MenuItems_Options_TimerRunning_OffersCancel`; `MenuItems_Options_Activity_ChecksTheCurrentLevel`; `MenuItems_Options_Dnd_OnOffersOff`.
  - `FocusTimer_Remaining_CountsDown`; `FocusTimer_TakeDone_OnceAfterTheEnd`; `FocusTimer_AfterASleepPastTheEnd_DoneRightAway` (start 5 min, ask at +2 h → `TakeDone` true, then false); `FocusTimer_Cancel_NeverDone`; `FocusTimer_IsLong_From25Minutes`.
  - `TimerInput_ParseMinutes` (Theory): "25"→25, " 25 "→25, "٢٥"→25, "۲۵"→25, "5د"→5, "600"→600; null for "", "0", "601", "abc", "-5", "2.5".
  - `NotesStore_AddAndLatest_NewestFirst`; `NotesStore_SurvivesRestart` (new store on the same path sees the notes); `NotesStore_OneLineTrimmedMax500`; `NotesStore_EmptyNote_NotAdded`; `NotesStore_Corrupt_MakesBadCopyAndStartsEmpty`; `NotesStore_KeepsAtMost50`.
  - `DndState_Timed_EndsOnce`; `DndState_UntilOff_NeverEndsByItself`; `DndState_TurnOff_EndsNow`.
  - `Brain_End_RemovesTheEvent` (post TimerDone, End(TimerDone) → key idle).
- [ ] **Step 2: Run** `dotnet test tests/Sahar.Core.Tests` → Expected: compile errors, then (after empty stubs) the new tests FAIL on behaviour.
- [ ] **Step 3: Implement.** Layout rule: button radius 22 DIP; spacing 70 DIP along the arc; full ring radius `max(90, bodyDip/2 + 50)` DIP; if any circle-plus-label falls outside work, use a half ring of radius `max(ring, 70·(n−1)/π)` centred on the direction from her centre toward the work centre; if still outside, move the ring centre inward by the overflow (buttons may then sit over her body edge but never off screen). Digits: map U+0660–0669 and U+06F0–06F9 to 0–9, strip spaces and a trailing `د`/`m`/`min`, accept 1–600. Notes JSON via its own source-generated context (AOT), like `LifeJson`.
- [ ] **Step 4: Run** `dotnet test tests/Sahar.Core.Tests` → Expected: all pass.
- [ ] **Step 5: Commit** `feat(core): menu layout and items, focus timer, typed minutes, notes, do-not-disturb`.

### Task 2: The radial menu in the engine

**Files:**
- Create: `src/Sahar.Engine/Menu/RadialMenu.cs` (state, open/close, sub-menus, actions), `src/Sahar.Engine/Menu/MenuVisual.cs` (button surfaces + spring), `src/Sahar.Engine/Menu/MenuCatcher.cs` (transparent full-screen click/key window)
- Modify: `src/Sahar.Engine/Input/InputController.cs` (double-click, right-click, no drag while open), `src/Sahar.Engine/App.cs` (wiring, pipe), `src/Sahar.Engine/Life/LifeDirector.cs` (no own actions while the menu is open), `tests/ui/run.ps1` (new stage `menu`), `.github/workflows/ui-test.yml` and `test-build.yml` (run stage `menu`)

**Interfaces:**
- Consumes: Task 1 `MenuItems`, `RadialLayout.Place`.
- Produces: `RadialMenu` with `bool IsOpen`, `void Open()`, `void Close()`, `void Press(string id)`, `IReadOnlyList<ButtonSlot> Slots`, `event Action<string>? Pressed` (for tools in Tasks 3–4), `Func<bool> TimerRunning`, `Func<bool> DndOn`; `_state.MenuOpen` reflects `IsOpen`.
- Test-mode pipe: `menu-open`, `menu-close`, `menu-press {id}`, `debug-menu` → `open=true level=main|<id> buttons=<id>:<x>,<y>,<r>;…` (px, screen coordinates).

- [ ] **Step 1: Write the failing UI stage `menu`** (helper `Menu` parses `debug-menu`):
  - 'right-click opens the menu with the six buttons' — `--headed mouse-click --button right` on her centre → `debug-menu` open, ids exactly `timer,note,activity,dnd,hide,play`; `state.menuOpen` true; screenshot `menu-open.png`.
  - 'Esc closes it' — send `{ESC}` → closed within 1 s.
  - 'a double-click opens it' — two left clicks 120 ms apart on her → open.
  - 'a click outside closes it and nothing else happens' — click 300 px away from every button → closed; her expression unchanged.
  - 'buttons work by real clicks' — open; click the `activity` slot's centre → level `activity`, options include `activity-lively`; click it → `debug-life` level Lively, menu closed.
  - 'from every corner every button is on screen' — pose her in each work-area corner, `menu-open`, every slot circle inside `$Work`; screenshot `menu-corner-<n>.png`.
  - 'hide closes the menu and hides her' — `menu-press hide` → menu closed, state hidden; `show`.
  - 'play makes her move' — `menu-press play` → within 1 s motion ∉ {Hover, EyesFollow}.
  - 'the menu closes when she hides' — open, then `hide minutes=1` → menu closed; `show`.
  - 'no drag while the menu is open' — open, `--headed drag` from her centre 200 px → position unchanged, menu still open or closed but motion never Drag.
  - 'the menu costs nothing at rest' — open for 10 s: working set < 60 MB.
- [ ] **Step 2: Push** → Expected: stage `menu` FAILS (`unknown cmd 'debug-menu'`).
- [ ] **Step 3: Implement.**
  - `MenuCatcher`: WS_POPUP, `WS_EX_LAYERED|WS_EX_TOOLWINDOW|WS_EX_TOPMOST`, alpha 1, covers the virtual screen, capture-excluded; it is activated on open (`SetForegroundWindow`) so it gets Esc; `WM_KILLFOCUS`/`WM_ACTIVATE` inactive → close; a click inside a slot circle → `Press(id)`, anywhere else → `Close()` (that click is consumed).
  - `MenuVisual`: one `SpriteVisual` per button on the overlay's root above Sahar; surface drawn once per DPI: white circle, 1.5 px `#E7E3F3` ring, emoji via `D2DContext.Text("Segoe UI Emoji", …)`, label in a white 90 % pill with `#1E2350` text below (Segoe UI 12 DIP semibold); hover ring `#FFD98A`. Pop-in: compositor scale keyframes `0 → 1.15 → 1` over 240 ms, delay 60 ms × index; close: scale to 0 over 150 ms then remove.
  - Options levels replace the ring in place (same pop-in). `back` returns to main.
  - Actions in this task: `activity-*` → `SetActivity`; `hide` → `director.HideFor(1 h)`; `play` → a random pick from `Showcase.PlayPicks` (Task 5 makes the list; until then `Wander|CelebrateLoop|PlayWithMoon|Curious|OrbitCursor` and a random flight).
  - Opening: ignored while hidden, dragging or thrown; she switches to `Hover` and life pauses (`LifeDirector.Paused = true`) until close. Close on `SetHidden(true)` and on display change (then reopen is not needed).
  - InputController: `WM_RBUTTONUP` → toggle menu; double-click = two `WM_LBUTTONUP` within `GetDoubleClickTime()` and 4 px → toggle menu (the first click's poke stays).
- [ ] **Step 4: Push** → Expected: all stages PASS; look at `menu-open.png` and the corner shots (buttons readable, inside the screen).
- [ ] **Step 5: Commit** `feat(menu): radial menu with real clicks, sub-menus, activity, hide and play`.

### Task 3: Timer and do-not-disturb

**Files:**
- Create: `src/Sahar.Engine/Tools/TimerTool.cs` (FocusTimer + ring + done), `src/Sahar.Engine/Tools/TextBubble.cs` (popup with a Win32 EDIT, shared with Task 4), `src/Sahar.Engine/Tools/DndTool.cs`
- Modify: `src/Sahar.Engine/Host/Director.cs` (quiet flags combine fullscreen + DND; `Post(EventKind, TimeSpan ttl)`; `End(EventKind)`), `src/Sahar.Engine/App.cs` (wiring; poke while `timer-done` ends it), `src/Sahar.Engine/Life/LifeDirector.cs` (DND → only small things, like a video in front), `tests/ui/run.ps1` (stage `menu`)

**Interfaces:**
- Consumes: Task 1 `FocusTimer`, `TimerInput`, `DndState`, `Brain.End`; Task 2 `RadialMenu.Pressed`.
- Produces: `TextBubble` with `void Show(Vector2 anchorPx, string placeholder, IReadOnlyList<string> below, Action<string> onEnter)`, `void Close()`, `bool IsOpen`, `nint EditHwnd` (tests). Test-mode pipe: `debug-timer` → `running=… remaining=<s>`; `timer {seconds}` starts a short timer; `debug-dnd` → `on=… until=…`.

- [ ] **Step 1: Write the failing UI steps** in stage `menu`:
  - 'a 25-minute timer runs with its ring' — `menu-press timer`, `menu-press timer-25` → `debug-timer` running, remaining 1490–1500 s; screenshot `timer-ring.png`.
  - 'the timer menu offers to stop it' — `menu-press timer` → options `timer-cancel,back`; press cancel → not running.
  - 'a typed time in Arabic digits' — `menu-press timer`, `menu-press timer-custom` → a bubble window with an EDIT appears (agent-desktop `snapshot` finds an Edit); SendKeys `٢` then `{ENTER}` → remaining 110–120 s; bubble gone.
  - 'when it ends she shows it and a poke dismisses it' — `timer seconds=3` → expression `timer-done` within 5 s and motion `Attention` (after Task 5; until then assert the expression only); click on her → expression back to idle within 1 s.
  - 'do not disturb for 30 minutes' — `menu-press dnd`, `menu-press dnd-30` → expression `dnd`, `debug-dnd` on; `life gap=2` for 10 s → motion stays in {Hover, Breathe}; `menu-press dnd` → options `dnd-off,back`; press `dnd-off` → expression idle.
  - 'fullscreen while DND is on keeps both' — DND on, fullscreen test window → hidden; close it → visible and still `dnd`.
- [ ] **Step 2: Push** → Expected: these steps FAIL.
- [ ] **Step 3: Implement.**
  - Ring: a `CompositionSpriteShape` ellipse around her (radius = body/2 + 10 DIP, stroke 3 DIP `#B9B0FF`, round caps) with `TrimEnd` animated by the compositor from `remaining/length` to 0 over the remaining time; rebuilt on DPI or position change (it is a child of her root, so it moves with her).
  - Ending: a one-shot `DispatcherQueueTimer` for the remaining time, plus `TakeDone` checks on `tray.PowerChanged` and unlock; done → `director.Post(TimerDone, ttl 60 s)` and, if `IsLong`, `Celebrate` after it; poke while key is `timer-done` → `director.End(TimerDone)`.
  - DND: `DndState`; on → `Post(Dnd)` (sticky) and quiet flag `DoNotDisturb`; off or timed end (checked every 30 s and on resume) → `Post(DndOff)`.
  - TextBubble: WS_POPUP + `WS_EX_TOPMOST|WS_EX_TOOLWINDOW`, rounded region (`CreateRoundRectRgn` 16 DIP), white; one EDIT (`ES_AUTOHSCROLL|ES_RIGHT`, `WS_EX_RTLREADING`, Segoe UI 14 DIP, max 500 chars), up to 3 STATIC lines below in `#4A4E78`; placed beside her toward the screen centre, inside work; capture-excluded; activated so typing goes there; Enter → `onEnter(text)` then close; Esc or losing focus → close. Edit subclassing via `SetWindowLongPtr(GWLP_WNDPROC)` with an `UnmanagedCallersOnly` proc.
- [ ] **Step 4: Push** → Expected: all stages PASS; look at `timer-ring.png` and the bubble shot.
- [ ] **Step 5: Commit** `feat(tools): focus timer with ring, typed minutes, do-not-disturb`.

### Task 4: Quick note

**Files:**
- Create: `src/Sahar.Engine/Tools/NoteTool.cs`
- Modify: `src/Sahar.Engine/App.cs`, `tests/ui/run.ps1`

**Interfaces:**
- Consumes: Task 1 `NotesStore`; Task 3 `TextBubble`.
- Produces: nothing new for later tasks.

- [ ] **Step 1: Write the failing UI steps:** 'a quick note is saved and shown next time' — `menu-press note` → bubble; SendKeys `اشتري خبز{ENTER}` → `%AppData%\Sahar\notes.json` contains `اشتري خبز`; expression `notification` briefly (2 s, transient); `menu-press note` again → the bubble's STATIC lines include `اشتري خبز` (agent-desktop `snapshot`); Esc closes it and nothing new is saved. 'four notes: the bubble shows the last three, newest first'.
- [ ] **Step 2: Push** → Expected: FAIL.
- [ ] **Step 3: Implement** `NoteTool`: opens `TextBubble` with placeholder `اكتب ملاحظة…` and `NotesStore.Latest(3)` as lines; Enter → `Add`, then `Express("notification", transient: true)`; a failed save logs `note not saved: …` and keeps the note in memory for this run.
- [ ] **Step 4: Push** → Expected: PASS.
- [ ] **Step 5: Commit** `feat(tools): quick note`.

### Task 5: The last eight moves (13, 14, 17, 18, 19, 20, 23, 25)

**Files:**
- Create: `src/Sahar.Core/Life/ShyDetector.cs`, `src/Sahar.Core/Life/AttentionPacer.cs`, `src/Sahar.Core/Life/Showcase.cs`
- Modify: `src/Sahar.Engine/Motion/MotionController.cs`, `src/Sahar.Core/Life/LifeTiming.cs` + `LifeChooser.cs` (new actions), `src/Sahar.Engine/Life/LifeDirector.cs`, `src/Sahar.Engine/App.cs` (startup entry, goodbye, attention), `src/Sahar.Engine/Host/Tray.cs` (quit waits for goodbye), `tests/Sahar.Core.Tests/Life/MovesTests.cs`, `tests/ui/run.ps1` (stage `motion`)

**Interfaces:**
- Produces:
  - `LifeAction` gains `FollowCursor`, `HideBehindWindow`, `HopIcons`; chooser weights: FollowCursor 2, HopIcons 2, HideBehindWindow 1 (only if `HasPerchableWindow`); all 0 with video/DND.
  - `sealed class ShyDetector` with `bool Update(DateTimeOffset now, float pointerDistanceDip, bool clicked)` → true when the pointer came within 60 DIP of her body 3 times (each approach separated by leaving past 120 DIP) within 10 s without a click; a click resets it.
  - `sealed class AttentionPacer` with `bool ShouldPlay(string expressionKey, DateTimeOffset now, bool dnd)` → true for `claude-permission`, `timer-done`, `reminder` at the start, then every 30 s while the key stays, at most 3 times; never with DND.
  - `static class Showcase` with `IReadOnlyList<ShowcaseStep> Steps` (`record ShowcaseStep(ShowcaseKind Kind, string Id, string Caption, float Seconds)`, `enum ShowcaseKind { Motion, Flight, Expression }`) and `IReadOnlyList<MotionKind> PlayPicks`.
  - `MotionController.Play` handles `FollowCursor`, `HideBehindWindow`, `Shy`, `StartupEntry`, `Goodbye`, `HopIcons`, `Attention`, `NapOnIcon`; `event Action? GoodbyeDone`.
- Behaviour (from the board "حركة سَحَر"):
  - 13 FollowCursor: flies after the pointer, gently, a little behind (spring toward pointer + 90 DIP offset, max 260 DIP/s), 6 s, then Hover; never ends on the pointer (avoid box).
  - 14 HideBehindWindow: flies to the left or right edge of the foreground window (the side with room), her body clipped by `InsetClip` at the window edge so she looks behind it, peeks out 40 % twice (Look/Smile), 7 s, comes out.
  - 17 Shy: shy face (`Eyes.Half`, `Mouth.Smile`, through the face gate) and a small Character scale-down to 0.94, drifts 120 DIP away from the pointer, 2.5 s.
  - 18 StartupEntry: from off-screen at the nearest side edge she flies to her home spot (1.6 s, ease-out), waves (Character rotation ±10° ×2), Happy/Grin. Played at start when system uptime (`GetTickCount64`) < 5 min; otherwise the normal fade-in.
  - 19 Goodbye: waves with her leaf (rotation ±12° ×2, 1 s), then shrinks to 0 while 8 small star sparkles fade out (0.8 s). Played on tray Quit (quit after `GoodbyeDone` or 2.5 s) and on `WM_QUERYENDSESSION` (no waiting).
  - 20 HopIcons: on the taskbar's inner edge, 4 hops of 48 DIP toward the screen centre (each 0.45 s arc, squish on landing), then Hover above the taskbar.
  - 23 Attention: comes 25 % of the way toward the screen centre, 3 soft light rings (`#B9B0FF`, scale 1→2.2, opacity 0.6→0, 0.9 s, staggered 0.3 s) from her body, then back; paced by `AttentionPacer`.
  - 25 NapOnIcon: sits on the taskbar edge (like LandAndSit) at a spot 1/3 from the taskbar's start, sleep face; used instead of SleepInCorner for half of the away naps.
- [ ] **Step 1: Write the failing tests.** Core: `ShyDetector_ThreeQuickApproaches_Shy`, `ShyDetector_AClickResets`, `ShyDetector_SlowApproaches_NotShy`; `AttentionPacer_StartThenEvery30s_MaxThree`, `AttentionPacer_NeverWithDnd`, `AttentionPacer_NewKeyStartsOver`; `Showcase_CoversEveryMotionButDrag_EveryFlight_EveryExpression` (the 26 expression keys come from `src/Sahar.Core/assets/expressions.json`, loaded the way the engine loads it), `Showcase_CaptionsAreArabicWithNumbers` (motion captions start with the board number, e.g. `13 · بتلحق الماوس`), `Showcase_PlayPicks_AreOnlyStandaloneMoves` (no Drag, Throw, FullscreenHide, Wake, Goodbye, StartupEntry); chooser `Choose_NewMoves_HaveWeight`, `Choose_Video_StillOnlySmallThings`. UI stage `motion`: for each of the eight kinds, `motion kind=<k>` → state motion is `<k>` within 300 ms, body stays inside the work area while it runs (sample every 200 ms), she is back to Hover within its time + 2 s (Goodbye: ends hidden, then `show` brings her back); frame sheet `moves-8.png` (two frames each). Startup: start the engine with `--simulate-boot` → first motion `StartupEntry`.
- [ ] **Step 2: Run** core tests and push → Expected: FAIL.
- [ ] **Step 3: Implement** the motions (compositor animations for waves/rings/sparkles; the CPU ticker only for paths that follow the pointer or travel), the triggers above, `--simulate-boot` (test mode only) and the chooser weights.
- [ ] **Step 4: Run and push** → Expected: PASS; review `moves-8.png`.
- [ ] **Step 5: Commit** `feat(motion): follow, hide behind a window, shy, hello, goodbye, hop icons, attention, nap on an icon`.

### Task 6: "Show me your moves", and test build 0.4.0

**Files:**
- Create: `src/Sahar.Engine/Life/ShowcasePlayer.cs`
- Modify: `src/Sahar.Engine/Host/Tray.cs` (item `فرجيني حركاتك / Show me your moves`, and `وقّف العرض / Stop the show` while it runs), `src/Sahar.Engine/App.cs`, `Directory.Build.props`, `src/Sahar.Core/CoreInfo.cs`, `src/Sahar.Engine/app.manifest`, `tests/Sahar.Core.Tests/SmokeTests.cs`, `packaging/README-test-build.txt`, `tests/ui/run.ps1`

**Interfaces:**
- Consumes: Task 5 `Showcase.Steps`.
- Produces: test-mode pipe `showcase {from?}` and `debug-showcase` → `running=… step=<n>/<total> id=<id>`.

- [ ] **Step 1: Write the failing tests.** Core: `CoreInfo_Version_Is_0_4_0`. UI: 'the show plays every step with a caption' — `showcase` → `debug-showcase` advances through ≥ 10 steps in 60 s; a caption pill is visible under her (screenshot `showcase.png`); a click on her stops it (running=false, she Hovers); life does nothing while it runs. Test-build: the zip holds the exe and README.
- [ ] **Step 2: Push** → Expected: FAIL.
- [ ] **Step 3: Implement** `ShowcasePlayer`: plays steps in order (motions via `Play`, flights via `PlayFlight(force: true)`, expressions via `Express(key, transient: false)` then back to idle), a caption pill below her (white 90 %, `#1E2350`, Segoe UI 13 DIP semibold, fades 250 ms); pauses life; stops on click, drag, menu open, hide or fullscreen; restores her home spot and idle at the end. Version 0.4.0 everywhere; README "what's new" (menu, timer, note, DND, the 8 moves, the show) and the manual checks (double-click on a touchpad, a menu at each screen edge, a timer across sleep, Arabic typing in the note).
- [ ] **Step 4: Push** → Expected: all stages PASS; test-build artifact `Sahar-0.4.0-test-win-x64.zip` = exe + README.
- [ ] **Step 5: Commit** `feat: show me your moves, test build 0.4.0`. Publishing waits for the user's OK.
