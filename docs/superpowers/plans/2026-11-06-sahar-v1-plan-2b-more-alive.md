# Sahar v1 Plan 2b — More Alive Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** After the user's first real test of 0.2.0 ("she only floats", "Lively did nothing", "a YouTube song did nothing"): music from a browser shows (headphones, music face); her own life gets variety — flight patterns from the motion boards, motions 15/16/21/22/24, small face moments; and a readable log of what she does and why.

**Architecture:** Pure pieces in `Sahar.Core/Life` (flight paths, moments, extended chooser, media rule) with xUnit tests; the engine plays them through `MotionController`/`ExpressionPlayer`; `LifeDirector` logs each decision (and each reason for waiting) to `engine.log`; the tray opens the Sahar folder.

**Tech Stack:** as Plan 2a.

**Spec:** `docs/superpowers/specs/2026-11-06-sahar-windows-v1-design.md` (§5 expressions, §6 motions 1, 15, 16, 21, 22, 24 and "she never covers what matters", §8.1). Boards: "حركة سَحَر" M1–M4 (flight cards in `/home/claude/gen/motion_more.py`). Builds on Plan 2a (merged, 0.2.0).

## Global Constraints

- Everything in Plans 1 and 2a still holds (AOT, no network, privacy §14, 60 MB / 0.5 % CPU at rest, test-mode rules).
- User decision 2026-11-06: browser playback is treated as music (headphones and music face); while the browser is in front she makes no big moves (it may be a video); fullscreen video still hides her.
- Own actions stay within the activity-level gaps of Plan 2a; face moments count as own actions.
- Every flight stays inside the work area of her monitor (clamped every frame) and returns her to rest.

## Review Focus

1. **Big moves over a playing browser:** with browser media playing and the browser in front, only face moments and Breathe may start. → Task 2 test `Choose_BrowserInFront_OnlySmallThings`.
2. **Flights near screen edges:** a flight started in a corner never leaves the work area. → Task 3 UI step "every flight stays on screen" (starts each pattern from a corner).
3. **Music stops mid-dance:** she stops dancing within one poll and returns to her own face. → Task 4 UI step with `debug-media`.
4. **The log grows forever:** `engine.log` stays under its cap on a long run. → Task 5 test step: the log is trimmed to ≤ 1 MB while running (write path checks size).
5. **A face moment during an event face:** a moment never replaces an event expression (permission, timer, error…). → Task 4: moments only start while the brain's face is `idle`; UI step posts `claude.permission` then forces a moment → expression stays `claude-permission`.

---

### Task 1: Browser music shows

**Files:** Modify `src/Sahar.Core/Life/MediaEdges.cs`, `tests/Sahar.Core.Tests/Life/LearningTests.cs`
- Produces: `MediaEdges.Update(playing, isVideo, fromBrowser, browserInFront)`: music = `playing && !isVideo` (browser included); `VideoPlaying` unchanged (`playing && (isVideo || (fromBrowser && browserInFront))`) and keeps meaning "no big moves now".
- [ ] **Step 1:** Change test `Media_BrowserPlaying_InFront_IsVideo_NotMusic` → `Media_BrowserPlaying_IsMusic_AndStillWhileInFront` (`[Music]` emitted, `VideoPlaying` true); `Media_BrowserPlaying_Behind_NeitherVideoNorMusic` → `Media_BrowserPlaying_Behind_MusicNotVideo` (`[Music]`, `VideoPlaying` false). FAIL → implement → PASS → commit `fix(core): browser playback shows as music`.

### Task 2: What she can do — flights, moments, chooser

**Files:** Create `src/Sahar.Core/Life/FlightPath.cs`, `src/Sahar.Core/Life/Moments.cs`; Modify `LifeChooser.cs`; Tests `tests/Sahar.Core.Tests/Life/VarietyTests.cs`
- Produces:
  - `public enum FlightPattern { Zigzag, FigureEight, SpiralUp, Dive, Wave, GlideHigh, BigCircle, DashAndStop, FallingLeaf, TaskbarPatrol }`
  - `public static Vector2 FlightPath.Offset(FlightPattern p, float t, float sizeDip)` — offset in DIPs from the start point at progress t∈[0,1], `Offset(p,0)=Offset(p,1)=0` for loops (FigureEight, BigCircle, Wave back-and-forth, TaskbarPatrol) and a net move for the rest; amplitudes from the boards scaled so the largest excursion is ≤ 260 DIP. `public static float FlightPath.Seconds(FlightPattern p)` (3.5–7 s from the boards).
  - `public enum MomentKind { Wink, Smile, LookAround, Drowsy, Giggle }`; `public static (Eyes Eyes, Mouth Mouth, float Seconds) Moments.Face(MomentKind k)` (Wink→Wink/Smile 1.6 s, Smile→Happy/Grin 2 s, LookAround→Look/Flat 2.4 s, Drowsy→Half/Yawn 2.6 s, Giggle→Happy/Grin 1.8 s + squish).
  - `LifeAction` gains `Flight, PlayWithMoon, Curious, Dance, Breathe, OrbitCursor, Moment`. `LifeContext` gains `bool CursorResting` (cursor still ≥ 2 s within 300 DIP of her). Base weights: Moment 4, Flight 3, Wander 2, PeekTaskbar 2, PerchWindow 2 (perchable only), PlayWithMoon 2, Curious 2, Breathe 1, CelebrateLoop 1, OrbitCursor 2 (cursor resting only), Dance 5 (music only), SleepInCorner 2 at night. While `VideoPlaying`: only Moment and Breathe. Typing/away/hidden/busy: None (unchanged).
- [ ] **Step 1:** Tests: `Flight_LoopsReturnHome` (loop patterns: |Offset(1)| < 0.5), `Flight_StaysWithinBound` (all patterns, t in 0..1 step 0.01: |Offset| ≤ 260), `Flight_Seconds_InRange` (3.5–7), `Moments_FaceForEach`, `Choose_BrowserInFront_OnlySmallThings` (VideoPlaying → only Moment/Breathe over 2 000 draws), `Choose_Dance_OnlyWithMusic`, `Choose_Orbit_OnlyWhenCursorRests`, `Choose_Variety` (normal context, 4 000 draws → ≥ 8 different actions). FAIL → implement → PASS → commit `feat(core): flights, moments and a wider choice`.

### Task 3: Flights and motions 15, 16, 21, 22, 24

**Files:** Modify `src/Sahar.Engine/Motion/MotionController.cs`, `App.cs` (pipe `motion` accepts `PlayWithMoon`, `Curious`, `Dance`, `Breathe`, `OrbitCursor`, and `flight=<pattern>` with kind `Wander`), `tests/ui/run.ps1` (stage `motion`)
- Produces: `MotionController.PlayFlight(FlightPattern p)` (Current = Wander; path from `FlightPath`, clamped to the work area every frame, tilt toward travel ±8°, Hover at the end). Motions: **PlayWithMoon** (4 s: moon orbit 3× faster, she sways toward it, happy face transient); **Curious** (3 s: drifts 30 DIP toward the cursor, tilts 10°, eyes Look, mouth O, then back); **Dance** (loops while music plays: sway ±8° and a 0.5 s bounce, music face; ends on MusicStop or after 12 s); **Breathe** (8 s: two slow breaths, scale 1→1.06, half-closed eyes); **OrbitCursor** (3 s: one circle of 80 DIP around the resting cursor, then back home).
- [ ] **Step 1:** Stage `motion`: each new kind → `state.motion` matches and 4 frames inside the work area; every flight pattern started from each work-area corner stays inside it (Review Focus 2). Push → FAIL.
- [ ] **Step 2–3:** Implement; push → PASS; review frames.
- [ ] **Step 4:** Commit `feat(engine): flights and motions 15, 16, 21, 22, 24`.

### Task 4: Life uses them — moments, dance, music

**Files:** Modify `src/Sahar.Engine/Life/LifeDirector.cs`, `App.cs` (test pipe `debug-media` → feeds the media path: `playing`, `video`, `browser`, `front`), `tests/ui/run.ps1` (stage `life`)
- Produces: LifeDirector maps the new actions; Flight picks a random pattern; Moment picks a random `MomentKind` and plays it as a transient face only while the brain's face is `idle` (Review Focus 5); `CursorResting` from cursor samples.
- [ ] **Step 1:** Stage `life`: (a) gap 2 s for 120 s → `debug-life last=` shows ≥ 6 different actions; (b) `debug-media playing=true browser=true front=true` → expression `music`, no big moves for 10 s (gap 2); `front=false` → Dance starts within 10 s; `playing=false` → dancing stops within 3 s; (c) `claude.permission` then `life now=Moment` → expression stays `claude-permission`. Push → FAIL.
- [ ] **Step 2–3:** Implement; push → PASS.
- [ ] **Step 4:** Commit `feat(engine): her life uses flights, moments and dance`.

### Task 5: A log you can send, and test build 0.3.0

**Files:** Modify `LifeDirector.cs` (log line per decision and per change of waiting reason: typing / video / hidden / away / busy / gap), `Diagnostics/Log.cs` (size cap checked on write: when over 1 MB keep the newest half), `Host/Tray.cs` + `App.cs` (menu "افتح مجلد سَحَر / Open Sahar folder" → `ShellExecute` explorer on `%LocalAppData%\Sahar`), `Directory.Build.props` 0.3.0, `CoreInfo`, `app.manifest`, `SmokeTests`, `packaging/README-test-build.txt` (what's new; how to send the log)
- [ ] **Step 1:** Core version tests 0.3.0 RED → GREEN; stage `life` checks `engine.log` contains `life:` lines naming her actions; stage `perf` writes 1.2 MB of log through a test command and checks the file is ≤ 1 MB. Push → FAIL → implement → PASS; test-build artifact zip = exe + README.
- [ ] **Step 2:** Commit `feat: life log, open-folder menu, test build 0.3.0`. Publishing waits for the user's OK.
