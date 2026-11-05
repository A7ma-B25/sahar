# Sahar v1 · Plan 1 of 3: Sahar alive on screen — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Native AOT Windows engine that shows Sahar floating on the desktop with all 25 v1 expressions, motions 1–12, mouse interaction, the event brain, and a test pipe — verified automatically on a GitHub Windows runner and shipped as a portable test build.

**Architecture:** `Sahar.Core` (pure .NET, tested on Linux) holds data, art geometry, the brain, physics and the pipe protocol. `Sahar.Engine` (net10.0-windows, Native AOT) hosts two Win32 windows (click-through overlay + tiny input window), draws Sahar's parts with Direct2D into Windows.UI.Composition drawing surfaces, and lets the compositor run idle animations. A named pipe `sahar` carries events and test commands; CI drives it and takes screenshots with agent-desktop.

**Tech Stack:** .NET 10 SDK 10.0.4xx, C# 14, Native AOT, CsWinRT projections from the `net10.0-windows10.0.22621.0` TFM, Win32 via `LibraryImport`, Direct2D 1.1 + DirectWrite via COM vtables, System.Text.Json source generation, xUnit v3, GitHub Actions (`ubuntu-latest` for Core tests, `windows-latest` for the engine), agent-desktop 1.2.0 (npm).

**Spec:** `docs/superpowers/specs/2026-11-06-sahar-windows-v1-design.md`

**Plan series:**
- **Plan 1 (this):** Core, engine host, rendering, expressions 1–25, motions 1–12, input, senses → brain, test pipe, CI visual tests, portable test build.
- **Plan 2:** radial menu (11 buttons) and its tools (timer, listen: music + Quran, prayer times, quick note, clipboard, activity level, do-not-disturb, hide, play), speech bubbles, motions 13–25, awareness level 1 comments, weather.
- **Plan 3:** Settings app (WinUI 3, RTL), `Sahar.Notify` + Claude Code hooks, audio and voice lines, tray menu, installer, updater, icons, localization, release checklist.

## Global Constraints

- Code, identifiers, comments and commit messages in English. User-facing text Arabic (default) and English.
- Engine: `net10.0-windows10.0.22621.0`, `SupportedOSPlatformVersion` `10.0.19041.0`, `PublishAot` true, `InvariantGlobalization` true, `EnableWindowsTargeting` true (so it compiles on Linux), `AllowUnsafeBlocks` true.
- Core: `net10.0`, no Windows APIs, no reflection-based JSON (source-generated `JsonSerializerContext` only, AOT-safe).
- No network access in Plan 1 code.
- Data folder: `%AppData%\Sahar\` (settings file `settings.json`).
- Pipe name: `sahar`. One JSON object per line, UTF-8, `\n` terminated.
- Single instance: named mutex `Local\Sahar.Engine`.
- Performance (spec §10): engine at rest < 60 MB working set and < 0.5% CPU on a real machine; CI asserts the RAM limit only (runner uses software rendering).
- Sizes (spec §4): Small 48, Medium 80 (default), Large 120 DIPs for the body width; scale with per-monitor DPI.
- Captures: overlay windows use `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE = 0x11)` unless `--capturable` is passed (CI and screenshots only).
- Expression changes cross-fade in 250 ms; nothing jumps.
- Commit trailer on every commit:
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` and `Claude-Session: https://claude.ai/code/session_01MwwFQTFgB3zwHXfUPTDHG6`.
- Reference implementation for Win32/Composition plumbing: tag `spike`, files `spike/Spike/Native.cs`, `Windows.cs`, `CompositionView.cs`, `Program.cs` (port, do not copy blindly; the fullscreen logic there is the fixed version).

## Review Focus

1. **Mixed-DPI multi-monitor:** Sahar dragged from a 100% monitor to a 150% one keeps the same DIP size and stays crisp. → Task 9 test `Rig_RasterSize_ScalesWithDpi`; Task 11 manual check in the test-build notes.
2. **Monitor or resolution change while flying:** Sahar must end inside the new work area, never off-screen. → Task 7 test `Physics_ReclampsWhenBoundsShrink`.
3. **Taskbar not at the bottom (left/top/right) or auto-hidden:** sitting and peeking use the real taskbar edge. → Task 7 test `Perch_UsesTaskbarEdge_ForEachSide`.
4. **Fullscreen false positives:** a maximized (not fullscreen) browser must not hide Sahar; a borderless game covering the monitor must. → Task 6 tests `Fullscreen_MaximizedWindow_IsNotFullscreen`, `Fullscreen_BorderlessCoveringMonitor_IsFullscreen`.
5. **Graphics device lost (sleep/resume, driver reset, RDP):** Sahar redraws instead of vanishing. → Task 9 pipe command `debug-device-lost` and CI assertion that the next screenshot still shows her.

---

## File Structure

```
global.json                                  pins SDK 10.0.4xx (rollForward latestFeature)
Directory.Build.props                        shared props (LangVersion, Nullable, warnings)
Sahar.slnx
src/Sahar.Core/
  Sahar.Core.csproj
  Art/PathData.cs            SVG path "d" → absolute PathCommand list
  Geometry/Rects.cs          RectI (Win32 pixels), RectF (DIPs)
  Art/Rgba.cs                color struct, "#RRGGBB" parse
  Art/Palette.cs             Mood enum + MoodColors (14 moods)
  Art/Rig.cs                 rig model (parts, fills, strokes, face variants) + loader
  Expressions/Expression.cs  ExpressionPreset, Eyes, Mouth, Extras
  Expressions/ExpressionCatalog.cs  the 25 presets (from assets)
  Settings/SaharSettings.cs  settings record + enums
  Settings/SettingsStore.cs  load/save (atomic), corrupt-file recovery
  Protocol/Messages.cs       pipe messages
  Protocol/ProtocolJson.cs   source-gen context + Serialize/Deserialize
  Brain/EventKind.cs         event kinds + priority table
  Brain/Brain.cs             active event selection, quiet rules
  Brain/CommentLimiter.cs    45-min limiter with backoff
  Brain/Fullscreen.cs        pure fullscreen decision
  Motion/Physics.cs          throw/bounce/friction, clamp
  Motion/Pendulum.cs         drag swing
  Motion/Perch.cs            taskbar/window-edge perch points
  Motion/Wander.cs           next wander target
  Motion/MotionKind.cs       motions 1–25 enum (1–12 implemented here)
  Json/CoreJson.cs           source-gen context for settings + assets
  assets/palette.json, assets/expressions.json, assets/art/sahar-rig.json  (EmbeddedResource)
tests/Sahar.Core.Tests/      xUnit v3, one test file per Core file above
src/Sahar.Engine/
  Sahar.Engine.csproj, app.manifest (PerMonitorV2, longPathAware)
  Program.cs                 args, single instance, message loop
  Interop/Native.cs          user32/dwmapi/shell32/kernel32 imports (from spike)
  Interop/D2D.cs             D2D/DWrite factories, geometry sink, drawing-surface interop
  Host/Windows.cs            OverlayWindow, InputWindow
  Host/CompositionHost.cs    DispatcherQueue, Compositor, DesktopWindowTarget, graphics device
  Render/PartRenderer.cs     draws one rig part to a CompositionDrawingSurface
  Render/SaharVisual.cs      visual tree: glow, shadow, body A/B, blush, face, leaf, bud, moon, stars, orb, extras
  Render/IdleAnimations.cs   bob, hem cross-fade, blink, leaf sway, star spin, moon orbit
  Render/ExpressionPlayer.cs apply preset with 250 ms cross-fade
  Motion/MotionController.cs motions 1–12, 60 Hz ticks only while moving
  Input/InputController.cs   click, drag from leaf, throw, hover lean, eyes follow cursor
  Senses/Foreground.cs, Senses/Power.cs, Senses/Idle.cs, Senses/Network.cs, Senses/Taskbar.cs, Senses/Monitors.cs
  Pipe/PipeServer.cs         named pipe, dispatch to brain/commands
  Diagnostics/StateReporter.cs  state snapshot for the pipe
tests/ui/
  pipe.ps1                   send a line, read a reply
  run.ps1                    CI scenario: smoke, expression gallery, motions, events, perf
tools/art/export_rig.py      regenerates assets/art/sahar-rig.json from the design generator
.github/workflows/build.yml  Core tests (ubuntu) + engine AOT publish (windows)
.github/workflows/ui-test.yml engine on windows-latest + agent-desktop screenshots
.github/workflows/test-build.yml portable zip as the "test-build" pre-release
```

---

### Task 1: Solution scaffold and CI build

**Files:**
- Create: `global.json`, `Directory.Build.props`, `Sahar.slnx`, `src/Sahar.Core/Sahar.Core.csproj`, `tests/Sahar.Core.Tests/Sahar.Core.Tests.csproj`, `tests/Sahar.Core.Tests/SmokeTests.cs`, `src/Sahar.Engine/Sahar.Engine.csproj`, `src/Sahar.Engine/app.manifest`, `src/Sahar.Engine/Program.cs`, `.github/workflows/build.yml`
- Modify: `README.md`, `.gitignore` (add `artifacts/`, `TestResults/`)

**Interfaces:**
- Produces: `Sahar.Engine.exe --version` prints `Sahar 0.1.0` and exits 0. `CoreInfo.Version` (`"0.1.0"`) in `Sahar.Core`.

- [ ] **Step 1:** Write `SmokeTests.CoreInfo_Version_Is_0_1_0`: `Assert.Equal("0.1.0", CoreInfo.Version);`
- [ ] **Step 2:** Run `dotnet test tests/Sahar.Core.Tests` → FAIL (`CoreInfo` missing).
- [ ] **Step 3:** Create the projects. Engine csproj copies the spike's PropertyGroup (TFM, AOT, `EnableWindowsTargeting`, manifest) with `AssemblyName` `Sahar.Engine`, `RootNamespace` `Sahar.Engine`, no SkiaSharp. Add `CoreInfo` static class. `Program.Main` handles `--version`.
- [ ] **Step 4:** Run `dotnet test tests/Sahar.Core.Tests` → PASS; `dotnet build src/Sahar.Engine -c Release` on Linux → succeeds (compile check).
- [ ] **Step 5:** `build.yml`: job `core` on `ubuntu-latest` (`dotnet test`), job `engine` on `windows-latest` (`dotnet publish src/Sahar.Engine -c Release -r win-x64 -o artifacts/engine`, then run `artifacts/engine/Sahar.Engine.exe --version` and assert output `Sahar 0.1.0`), upload `artifacts/engine` as artifact `engine-win-x64`. Triggers: push to `main`, pull_request, workflow_dispatch.
- [ ] **Step 6:** Push; verify both jobs green with `gh run list -R A7ma-B25/sahar --limit 1`.
- [ ] **Step 7:** Commit `chore: scaffold solution, Core tests and AOT engine build`.

### Task 2: SVG path parser

**Files:** Create `src/Sahar.Core/Art/PathData.cs`, `src/Sahar.Core/Geometry/Rects.cs`; Test `tests/Sahar.Core.Tests/Art/PathDataTests.cs`

**Interfaces:**
- Produces: `public enum PathOp { MoveTo, LineTo, CubicTo, QuadTo, ArcTo, Close }`; `public readonly record struct PathCommand(PathOp Op, Vector2 P1, Vector2 P2, Vector2 P3, Vector2 Radii = default, float XRotation = 0, bool LargeArc = false, bool Sweep = false)` (for `CubicTo` P1,P2 are controls, P3 end; `QuadTo` P1 control, P3 end; `LineTo`/`MoveTo`/`ArcTo` end in P3); `public static IReadOnlyList<PathCommand> PathData.Parse(string d)`. All points absolute; `H V S T` and lowercase forms normalized.
- Also produces (used by Tasks 6, 7, 11, 12): `public readonly record struct RectI(int Left, int Top, int Right, int Bottom)` and `public readonly record struct RectF(float X, float Y, float Width, float Height)` with `bool Contains(RectF other)`, `bool Intersects(RectF other)` and `static RectF FromRectI(RectI r, float scale)`.

- [ ] **Step 1:** Tests:
  - `Parse_AbsoluteCubic`: `"M40 176 C 34 112, 72 58, 120 58 Z"` → `[MoveTo P3=(40,176), CubicTo P1=(34,112) P2=(72,58) P3=(120,58), Close]`.
  - `Parse_RelativeQuad`: `"M120 62 q-9 3 -16 10"` → `QuadTo P1=(111,65) P3=(104,72)`.
  - `Parse_RelativeArc`: `"M126 162 a9 9 0 1 0 6 -14"` → `ArcTo Radii=(9,9) LargeArc=true Sweep=false P3=(132,148)`.
  - `Parse_SmoothQuadT`: `"M88 122 q10 -12 20 0 t20 0"` → second command `QuadTo P1=(118,134) P3=(128,122)` (reflected control).
  - `Parse_ImplicitRepeat`: `"M0 0 l 1 1 2 2"` → two `LineTo` ending (1,1) and (3,3).
  - `Parse_Invalid_Throws`: `"M 1"` → `FormatException`.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement a single-pass tokenizer (numbers incl. `-`, `.5`, exponents; commas/whitespace) with current point, subpath start, and last control point for `S`/`T` reflection.
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(core): SVG path parser`.

### Task 3: Palette, rig and expression catalog (assets)

**Files:**
- Create: `src/Sahar.Core/Art/Rgba.cs`, `Art/Palette.cs`, `Art/Rig.cs`, `Expressions/Expression.cs`, `Expressions/ExpressionCatalog.cs`, `Json/CoreJson.cs`, `src/Sahar.Core/assets/palette.json`, `assets/expressions.json`, `assets/art/sahar-rig.json`, `tools/art/export_rig.py`
- Test: `tests/Sahar.Core.Tests/Art/PaletteTests.cs`, `Art/RigTests.cs`, `Expressions/ExpressionCatalogTests.cs`

**Interfaces:**
- Produces:
  - `public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)` with `static Rgba Parse(string hex)` (`#RRGGBB` or `#RRGGBBAA`).
  - `public enum Mood { Calm, Happy, Focus, Excited, Sleepy, Mystic, Alert, Dim, Error, Online, Hot, Cold, Love, Night }`
  - `public sealed record MoodColors(Rgba Top, Rgba Mid, Rgba Bottom, Rgba Glow, Rgba Bud, Rgba Blush)`; `public static MoodColors Palette.Get(Mood mood)`.
  - `public enum Eyes { Open, Happy, Closed, Focus, Sparkle, Mystic, Worried, Wide, Look, Up, Wink, Half, Dizzy, Heart, Sad, Scan }`, `public enum Mouth { Smile, Grin, Flat, O, Sleepy, Mystic, Wobble, BigO, Frown, Tongue, Puff, Sing, Yawn, None }`
  - `[Flags] public enum Extras { None=0, Headphones=1, Notes=2, Steam=4, Hearts=8, Rain=16, Snow=32, Confetti=64, Zzz=128, Sweat=256, Stars=512, Squish=1024, Shiver=2048, Grounded=4096 }`
  - `public sealed record ExpressionPreset(int Id, string Key, Mood Mood, Eyes Eyes, Mouth Mouth, string? OrbGlyph, Rgba? OrbTop, Rgba? OrbBottom, Extras Extras)`
  - `public static class ExpressionCatalog { IReadOnlyList<ExpressionPreset> All; ExpressionPreset Get(string key); }` — `All` holds 26 presets: Id 0 `idle` (Calm, Open, Smile, no orb) plus the 25 of spec §5.
  - `public sealed record Rig(...)`: named parts (`body`, `bodyHem2`, `highlight`, `blushL`, `blushR`, `pleats`, `leafStem`, `leafA`, `leafB`, `leafVein`, `bud`), plus `Faces`: dictionary `Eyes → RigShape[]` and `Mouth → RigShape[]`; `RigShape(string PathOrPrimitive, Fill?, Stroke?, StrokeWidth, Opacity)` in the 240×240 design space with origin offset (30,30) inside a 300×300 canvas. `public static Rig Rig.Load()` reads the embedded resource.
- [ ] **Step 1:** Tests:
  - `Palette_Calm_MatchesDesign`: Top `#FFFFFF`, Mid `#F1ECFF`, Bottom `#CBC1F0`, Glow `#B9B0FF`, Bud `#C9B8FF`, Blush `#FFB8A0`.
  - `Palette_AllFourteenMoods_Defined`: `Enum.GetValues<Mood>()` each returns non-null; spot-check Mystic Mid `#7C69C9`, Sleepy Bottom `#8E88B8`, Hot Glow `#FF8A5C`.
  - `Catalog_Has26_UniqueIdsAndKeys`, `Catalog_Ids_Are_0_To_25`, `Catalog_Idle_IsCalmOpenSmile`.
  - `Catalog_Keys_MatchSpecOrder`: keys of Ids 1–25 in order `music, claude-working, claude-permission, claude-done, timer-done, celebrate, rain, battery-low, charging, download, sleep, back, notification, dnd, offline, online, screenshot, copied, reminder, error, hot, poked, dragged, break, late-night`.
  - `Catalog_PermissionPreset`: key `claude-permission` → Mood Alert, Eyes Open, Mouth O, OrbGlyph `"؟"`.
  - `Rig_AllPaths_Parse`: every part and face shape parses with `PathData.Parse` (or is a primitive `ellipse cx cy rx ry` / `circle cx cy r`).
  - `Rig_Body_MatchesDesign`: `body` path string equals `M40 176 C 34 112, 72 58, 120 58 C 168 58, 206 112, 200 176 C 200 196, 184 206, 170 196 C 158 210, 136 212, 120 200 C 104 212, 82 210, 70 196 C 56 206, 40 196, 40 176 Z`.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Write `tools/art/export_rig.py` from the design generator (`MOODS`, `BODY`, `BODY2`, leaf, highlight, pleats, `eyes()`, `mouth()` shapes; the 25 expression rows of the expressions board E1) and run it to produce the three JSON assets; mark them `EmbeddedResource`. Implement the records and loaders with `CoreJson` (source-generated).
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(core): palette, rig and 25 expression presets`.

### Task 4: Settings and store

**Files:** Create `src/Sahar.Core/Settings/SaharSettings.cs`, `Settings/SettingsStore.cs`; Test `tests/Sahar.Core.Tests/Settings/SettingsStoreTests.cs`

**Interfaces:**
- Produces: `public sealed record SaharSettings { int Version = 1; UiLanguage Language = Auto; SaharSize Size = Medium; ActivityLevel Activity = Medium; SoundLevel Sound = Light; float Volume = 0.6f; TimeOnly? QuietFrom; TimeOnly? QuietTo; bool AwarenessEnabled = true; IReadOnlyList<string> BlockedApps = []; bool WeatherEnabled = false; bool UpdateCheckEnabled = true; SeasonalIcon Seasonal = Auto; }` with enums `UiLanguage {Auto, Arabic, English}`, `SaharSize {Small=48, Medium=80, Large=120}`, `ActivityLevel {Calm, Medium, Lively}`, `SoundLevel {Silent, Light, Full}`, `SeasonalIcon {Auto, On, Off}`. `SettingsStore.Load(string path) → SaharSettings`, `SettingsStore.Save(string path, SaharSettings s)`.
- [ ] **Step 1:** Tests: `Load_MissingFile_ReturnsDefaults`; `SaveThenLoad_RoundTrips`; `Load_CorruptJson_ReturnsDefaults_AndKeepsBackup` (file `settings.json.bad` exists afterwards); `Load_UnknownFields_Ignored`; `Save_IsAtomic` (save writes `settings.json.tmp` then replaces; after Save no `.tmp` remains); `Defaults_MatchSpec` (Size Medium=80, Activity Medium, Sound Light, BlockedApps empty).
- [ ] **Step 2:** FAIL → **Step 3:** implement with `File.Replace` when target exists, else `File.Move` → **Step 4:** PASS → **Step 5:** commit `feat(core): settings model and atomic store`.

### Task 5: Pipe protocol

**Files:** Create `src/Sahar.Core/Protocol/Messages.cs`, `Protocol/ProtocolJson.cs`; Test `tests/Sahar.Core.Tests/Protocol/ProtocolTests.cs`

**Interfaces:**
- Produces: `public sealed record PipeRequest(string Type, string Name, IReadOnlyDictionary<string,string>? Args = null)` (`Type` ∈ `"event"`, `"cmd"`); `public sealed record PipeReply(bool Ok, string? Error = null, EngineState? State = null)`; `public sealed record EngineState(string Expression, string Motion, bool MenuOpen, bool Hidden, bool Quiet, float X, float Y, float Width, float Height, long WorkingSetBytes)`; `ProtocolJson.ParseRequest(string line) → PipeRequest` (throws `FormatException`), `ProtocolJson.Write(PipeReply r) → string` (single line, no indentation).
- Command names used by later tasks: `state`, `expression` (arg `key`), `motion` (arg `kind`), `pose` (args `x`,`y`), `capturable` (arg `on`), `debug-device-lost`, `quit`.
- [ ] **Step 1:** Tests: `Parse_Event` (`{"type":"event","name":"claude.permission"}`), `Parse_CommandWithArgs`, `Parse_Garbage_Throws`, `Write_IsSingleLine`, `Write_StateRoundTrips`.
- [ ] **Step 2–5:** FAIL → implement (source-gen context, camelCase) → PASS → commit `feat(core): pipe protocol`.

### Task 6: Brain, quiet rules, comment limiter, fullscreen decision

**Files:** Create `src/Sahar.Core/Brain/EventKind.cs`, `Brain/Brain.cs`, `Brain/CommentLimiter.cs`, `Brain/Fullscreen.cs`; Tests `tests/Sahar.Core.Tests/Brain/*.cs`

**Interfaces:**
- Produces:
  - `public enum EventKind` with one member per expression key except `poked`/`dragged` (those come from input), wire names `music`, `claude.working`, `claude.permission`, `claude.done`, `timer.done`, `celebrate`, `weather.rain`, `battery.low`, `battery.charging`, `download.done`, `user.away`, `user.back`, `notification`, `dnd`, `net.offline`, `net.online`, `screenshot`, `clipboard.copied`, `reminder`, `error`, `cpu.hot`, `break`, `late-night`, plus end events `music.stop`, `dnd.off` (they only end sticky events). `public static EventKind EventNames.Parse(string wire)` throws `FormatException` for unknown names.
  - `public static string Brain.ExpressionKeyFor(EventKind k)` maps to catalog keys: `user.away`→`sleep`, `user.back`→`back`, `battery.charging`→`charging`, `battery.low`→`battery-low`, `download.done`→`download`, `weather.rain`→`rain`, `net.offline`→`offline`, `net.online`→`online`, `clipboard.copied`→`copied`, `cpu.hot`→`hot`, `timer.done`→`timer-done`, other names replace `.` with `-`.
  - `public static int EventPriorities.Of(EventKind k)`: 100 for `claude.permission`, `timer.done`, `reminder`; 80 for `claude.done`, `error`, `battery.low`, `notification`; 60 for `download.done`, `net.offline`, `net.online`, `cpu.hot`, `screenshot`, `clipboard.copied`, `celebrate`, `break`; 40 for `music`, `claude.working`, `weather.rain`, `battery.charging`, `dnd`, `user.back`, `late-night`; 20 for `user.away`.
  - `[Flags] public enum QuietReason { None=0, Fullscreen=1, CameraOrMic=2, DoNotDisturb=4, Quran=8, Adhan=16, QuietHours=32 }`
  - `public sealed class Brain { void Post(EventKind k, DateTimeOffset now, TimeSpan? ttl = null); void SetQuiet(QuietReason r); BrainOutput Tick(DateTimeOffset now); }` with `public sealed record BrainOutput(string ExpressionKey, bool Hidden, bool SoundAllowed, bool CommentsAllowed)`. Default TTL 8 s; sticky kinds (`claude.working`, `music`, `dnd`, `user.away`) last until a matching end event (`claude.done`, `music.stop`, `dnd.off`, `user.back`). Idle expression key when nothing active: `idle`.
  - `public sealed class CommentLimiter { bool TryComment(DateTimeOffset now); void Dismissed(DateTimeOffset now); void Accepted(); }` — minimum gap 45 min; each dismissal doubles the gap up to 4 h; acceptance resets to 45 min.
  - `public static bool Fullscreen.IsFullscreen(RectI window, RectI monitor, string windowClass, bool hasCaption)` — true when the window covers the whole monitor and has no caption, and the class is not one of `Progman`, `WorkerW`, `Shell_TrayWnd`, `Shell_SecondaryTrayWnd`.
- [ ] **Step 1:** Tests:
  - `HigherPriority_Preempts` (music active, then claude.permission → expression `claude-permission`).
  - `LowerPriority_Waits_ThenShows` (permission active, download.done posted, after permission TTL expires → `download`).
  - `Expired_Event_Dropped` (download.done older than its TTL never shows).
  - `Sticky_Until_EndEvent` (claude.working stays until claude.done).
  - `Fullscreen_Hides` (`SetQuiet(Fullscreen)` → `Hidden=true`).
  - `Quran_Silences_But_Shows` (`SetQuiet(Quran)` → `Hidden=false, SoundAllowed=false, CommentsAllowed=false`).
  - `CommentLimiter_45MinGap`, `CommentLimiter_BackoffDoubles_CapsAt4h`, `CommentLimiter_AcceptResets`.
  - `Fullscreen_MaximizedWindow_IsNotFullscreen` (window = work area, has caption → false).
  - `Fullscreen_BorderlessCoveringMonitor_IsFullscreen` (window == monitor, no caption → true).
  - `Fullscreen_Desktop_IsNotFullscreen` (class `WorkerW` → false).
  - `ExpressionKeyFor_EveryEvent_IsInCatalog` (every non-end `EventKind` maps to a key that `ExpressionCatalog.Get` accepts).
  - `EventNames_Unknown_Throws`.
- [ ] **Step 2–5:** FAIL → implement → PASS → commit `feat(core): brain, quiet rules and comment limiter`.

### Task 7: Motion math

**Files:** Create `src/Sahar.Core/Motion/MotionKind.cs`, `Physics.cs`, `Pendulum.cs`, `Perch.cs`, `Wander.cs`; Tests `tests/Sahar.Core.Tests/Motion/*.cs`

**Interfaces:**
- Produces:
  - `public enum MotionKind { Wander=1, Hover=2, PeekTaskbar=3, EyesFollow=4, Drag=5, Throw=6, LandAndSit=7, SleepInCorner=8, Wake=9, FullscreenHide=10, PerchWindow=11, CelebrateLoop=12, FollowCursor=13, HideBehindWindow=14, PlayWithMoon=15, Curious=16, Shy=17, StartupEntry=18, Goodbye=19, HopIcons=20, Dance=21, Breathe=22, Attention=23, OrbitCursor=24, NapOnIcon=25 }` (13–25 implemented in Plan 2).
  - `public record struct Body2D(Vector2 Position, Vector2 Velocity)`; `public static (Body2D next, bool resting) Physics.Step(Body2D b, float dt, RectF bounds, Vector2 size)` — friction: velocity × e^(−2.2·dt); edge hit reflects that axis with restitution 0.55; resting when speed < 20 px/s; result always clamped so the body rect is inside `bounds`.
  - `public static Body2D Physics.Clamp(Body2D b, RectF bounds, Vector2 size)`.
  - `public sealed class Pendulum { float Angle; void Step(float dt, float pivotAccelX); }` — angle in degrees, damping 3.0/s, stiffness 30/s², clamped ±25°.
  - `public enum Edge { Bottom, Top, Left, Right }`; `public static Vector2 Perch.OnTaskbar(RectF taskbar, Edge edge, float bodyWidth, float alongFraction)` returns the body's bottom-centre point sitting on the inner edge of the taskbar; `Perch.OnWindowTop(RectF window, float bodyWidth, float alongFraction)`.
  - `public sealed class Wander { Wander(int seed); Vector2 NextTarget(RectF workArea, Vector2 size, IReadOnlyList<RectF> avoid); }` — never returns a point whose body rect intersects any `avoid` rect if a free point exists within 50 tries.
- [ ] **Step 1:** Tests: `Physics_Friction_SlowsToRest`, `Physics_BouncesOffRightEdge` (velocity.X flips sign, |v| × 0.55), `Physics_ReclampsWhenBoundsShrink` (body at x=1800 in 1920 bounds, bounds become 1280 → body inside), `Pendulum_SwingsOppositeToAcceleration_AndDamps`, `Perch_UsesTaskbarEdge_ForEachSide` (bottom/top/left/right taskbars of a 1920×1080 screen), `Wander_AvoidsRects`, `Wander_IsDeterministicForSeed`.
- [ ] **Step 2–5:** FAIL → implement → PASS → commit `feat(core): physics, pendulum, perch and wander`.

### Task 8: Engine host, pipe server and CI smoke test

**Files:**
- Create: `src/Sahar.Engine/Interop/Native.cs` (port from spike + additions), `Host/Windows.cs`, `Host/CompositionHost.cs`, `Pipe/PipeServer.cs`, `Diagnostics/StateReporter.cs`, `tests/ui/pipe.ps1`, `tests/ui/run.ps1`, `.github/workflows/ui-test.yml`
- Modify: `src/Sahar.Engine/Program.cs`

**Interfaces:**
- Consumes: `ProtocolJson`, `PipeRequest`, `PipeReply`, `EngineState` (Task 5).
- Produces: `Sahar.Engine.exe [--test-mode] [--capturable]`; `CompositionHost` exposing `Compositor Compositor`, `ContainerVisual Root`, `CompositionGraphicsDevice GraphicsDevice`, `event Action DeviceLost`; `OverlayWindow` per monitor covering the virtual screen; `InputWindow.MoveTo(RectI)`; `PipeServer.Register(string cmd, Func<PipeRequest, PipeReply>)`; `tests/ui/pipe.ps1 -Line '<json>'` prints the reply line.
- [ ] **Step 1:** Write `tests/ui/run.ps1` stage `smoke`: start `Sahar.Engine.exe --test-mode --capturable`, wait up to 5 s for pipe `sahar`, send `{"type":"cmd","name":"state"}`, assert `ok=true`; send `quit`, assert process exits within 3 s with code 0. Second instance started while first runs exits immediately with code 3.
- [ ] **Step 2:** Add `ui-test.yml` (windows-latest: setup-dotnet 10, setup-node 22, `npm i -g agent-desktop@1.2.0`, publish engine, run `tests/ui/run.ps1 -Stage smoke`, upload `artifacts/ui`). Push → job FAILS (engine has no pipe).
- [ ] **Step 3:** Implement: per-monitor-v2 DPI manifest; mutex; overlay + input windows as in spike (`WS_EX_NOREDIRECTIONBITMAP|LAYERED|TRANSPARENT|TOPMOST|TOOLWINDOW|NOACTIVATE`, input at alpha 1); `WDA_EXCLUDEFROMCAPTURE` unless `--capturable`; `CompositionHost` creates the graphics device from a D3D11 device → `ID2D1Device` → `ICompositorInterop.CreateGraphicsDevice`, and raises `DeviceLost` on `RenderingDeviceReplaced` / `D2DERR_RECREATE_TARGET`; `PipeServer` on a background thread with `NamedPipeServerStream("sahar", InOut, 4, Byte, Asynchronous)`, marshalling commands to the UI thread via `DispatcherQueue.TryEnqueue`.
- [ ] **Step 4:** Push → `ui-test` smoke stage PASS; `build` still green.
- [ ] **Step 5:** Commit `feat(engine): host windows, composition device and test pipe`.

### Task 9: Draw Sahar and her idle life

**Files:** Create `src/Sahar.Engine/Interop/D2D.cs`, `Render/PartRenderer.cs`, `Render/SaharVisual.cs`, `Render/IdleAnimations.cs`; Modify `tests/ui/run.ps1` (stage `idle`); Test `tests/Sahar.Core.Tests/Art/RigScaleTests.cs`

**Interfaces:**
- Consumes: `Rig`, `PathData`, `Palette` (Tasks 2–3); `CompositionHost` (Task 8).
- Produces: `public static SizeI RigScale.RasterSize(float bodyDip, float dpiScale)` in Core (`ceil(300 × bodyDip/160 × dpiScale)`; 160 = body width in design units); `SaharVisual(CompositionHost host, Rig rig)` with `Vector2 Position`, `float BodyDip`, `void SetMood(Mood m, TimeSpan fade)`, `void SetFace(Eyes e, Mouth m, TimeSpan fade)`, `void Redraw()`; `IdleAnimations.Start(SaharVisual v)`.
- [ ] **Step 1:** Core test `Rig_RasterSize_ScalesWithDpi`: `RasterSize(80, 1.0) == (150,150)`, `RasterSize(80, 1.5) == (225,225)`, `RasterSize(120, 2.0) == (450,450)`. Run → FAIL → implement → PASS.
- [ ] **Step 2:** Extend `run.ps1` stage `idle`: start engine, `pose` to (500,400), wait 1.5 s, `agent-desktop screenshot --screen 0 artifacts/ui/idle.png`, then sample pixels with System.Drawing: the pixel at the body centre (pose + rig offset) has R,G,B within ±40 of Calm `Mid` (#F1ECFF) and alpha-blended over the wallpaper is not the wallpaper colour; then send `debug-device-lost`, wait 1 s, screenshot `idle-after-device-lost.png`, same assertion. Push → FAIL.
- [ ] **Step 3:** Implement `D2D.cs` (factory, `ID2D1PathGeometry` from `PathCommand` list incl. arcs, linear/radial gradient brushes, `ICompositionDrawingSurfaceInterop.BeginDraw/EndDraw`). `PartRenderer` draws one rig part into a `CompositionDrawingSurface` of `RasterSize`. `SaharVisual` builds SpriteVisuals in this z-order: glow, shadow, moon-back, stars, bodyA, bodyB (hem frame 2), pleats, highlight, blush, face, leaf (CenterPoint at stem base 150,90 in canvas units), bud + bud glow, moon-front, orb, extras. Mood change = render new body pair into spare surfaces and cross-fade opacity. `IdleAnimations` (all compositor keyframe animations, infinite): bob translate Y 0→−10 DIP→0 over 3.6 s; bodyA/bodyB opacity cross-fade 2.2 s; blink = face scale Y 1→0.1→1 at 92–100% of 6 s; leaf rotation ±5° over 5 s; stars container rotation 360° over 40 s; moon on tilted ellipse (rx 112, ry 26 design units, −10°) over 9 s with front/back visibility switching at the half-way points; shadow scale X 1→0.8→1 synced with bob; bud glow opacity 0.25→0.7→0.25 over 2.4 s. Re-render all surfaces on `DeviceLost` and on DPI change (`WM_DPICHANGED`).
- [ ] **Step 4:** Push → `idle` stage PASS; open `idle.png` from the artifact and compare by eye with board "سَحَر الجديدة" (same silhouette, colours, leaf, moon).
- [ ] **Step 5:** Commit `feat(engine): draw Sahar with idle animations`.

### Task 10: Expressions 1–25

**Files:** Create `src/Sahar.Engine/Render/ExpressionPlayer.cs`, `Render/OrbRenderer.cs` (DirectWrite glyph/emoji into the orb), `Render/Extras.cs` (notes, steam, hearts, rain, snow, confetti, zzz, sweat, headphones as compositor-animated sprites); Modify `tests/ui/run.ps1` (stage `gallery`)

**Interfaces:**
- Consumes: `ExpressionCatalog`, `ExpressionPreset` (Task 3); `SaharVisual` (Task 9).
- Produces: `ExpressionPlayer.Play(ExpressionPreset p)`; pipe command `expression` (`args.key`) → reply `ok` and `state.expression == key`; unknown key → `ok=false, error="unknown expression"`.
- [ ] **Step 1:** `run.ps1` stage `gallery`: for each of the 25 keys in catalog order: send `expression`, wait 700 ms, screenshot `artifacts/ui/gallery/NN-key.png` (crop 260×260 around Sahar), assert `state.expression`; also assert unknown key returns `ok=false`. Build `gallery.png` contact sheet (5×5) with System.Drawing. Push → FAIL.
- [ ] **Step 2:** Implement: `Play` cross-fades mood (250 ms), swaps face surface (250 ms), shows/hides orb with glyph (`OrbGlyph` rendered with DirectWrite `Segoe UI Emoji` colour glyphs, `Segoe UI` fallback for `؟`), starts/stops extras; `Squish` = scale (1,1)→(1.14,0.86)→(0.94,1.08)→(1,1) over 0.6 s pivot bottom-centre; `Shiver` = translate X ±2 DIP at 0.3 s; `Grounded` stops bob and lowers 12 DIP.
- [ ] **Step 3:** Push → `gallery` PASS. Compare `gallery.png` with the expressions board 1–25; list any mismatch in the task report.
- [ ] **Step 4:** Commit `feat(engine): 25 expressions with cross-fades`.

### Task 11: Motions 1–12 and mouse interaction

**Files:** Create `src/Sahar.Engine/Motion/MotionController.cs`, `Input/InputController.cs`, `Senses/Taskbar.cs`, `Senses/Monitors.cs`; Modify `tests/ui/run.ps1` (stage `motion`)

**Interfaces:**
- Consumes: `Physics`, `Pendulum`, `Perch`, `Wander`, `MotionKind` (Task 7); `SaharVisual`, `ExpressionPlayer` (Tasks 9–10).
- Produces: `MotionController.Play(MotionKind k)`; `Taskbar.Get(nint monitor) → (RectF rect, Edge edge, bool autoHide)` via `SHAppBarMessage(ABM_GETTASKBARPOS)` + `MonitorFromRect`; pipe command `motion` (`args.kind` = enum name) → `state.motion`.
- Motion definitions (spec board "حركة سَحَر" 1–12): Wander = move to `Wander.NextTarget` over 4–8 s with tilt ±8° toward travel; Hover = idle only; PeekTaskbar = clip below taskbar top, rise 70% of height over 0.9 s, look left/right 2 s, sink; EyesFollow = pupils offset toward cursor, max 4 design units, every 33 ms only while cursor moves; Drag = body hangs from leaf pivot, `Pendulum` from cursor acceleration, scale (0.92,1.1); Throw = `Physics.Step` from release velocity, rotation 360° per second of flight, squish on edge hit; LandAndSit = descend to `Perch.OnTaskbar`, squish, Grounded; SleepInCorner = move to taskbar corner, scale 0.75, expression `sleep`; Wake = stretch scale (0.86,1.2)→(1,1), expression `back`; FullscreenHide = scale to 0 into a 4 DIP dot at the taskbar corner; PerchWindow = sit on `Perch.OnWindowTop(foreground rect)` swinging ±6°; CelebrateLoop = one circle radius 50 DIP in 2.6 s with rotation 360°.
- Input: click (no drag > 4 px) → expression `poked` + Squish; press on leaf area + move → Drag; release with speed > 300 px/s → Throw else LandAndSit or Hover; hover > 400 ms → lean 12° toward cursor. Input window follows the body rect each frame while moving.
- [ ] **Step 1:** `run.ps1` stage `motion`: for each MotionKind 1–12: send `motion`, record 6 frames 250 ms apart into `artifacts/ui/motion/NN-kind/`, assert `state.motion` and that `state.X/Y` stays inside the primary work area; for Throw send `pose` then `motion Throw` with args `vx=1600,vy=-600` and assert final position inside the work area. Use `agent-desktop click`-free physical input via `agent-desktop --headed mouse-click` and `drag` to test click → `poked` and drag → `Drag`. Push → FAIL.
- [ ] **Step 2:** Implement controller with a 60 Hz `CreateWaitableTimerEx` tick that runs only while a CPU-driven motion is active and stops when resting.
- [ ] **Step 3:** Push → `motion` PASS; review frames.
- [ ] **Step 4:** Commit `feat(engine): motions 1–12 and mouse interaction`.

### Task 12: Senses → brain wiring and minimal tray

**Files:** Create `src/Sahar.Engine/Senses/Foreground.cs`, `Senses/Power.cs`, `Senses/Idle.cs`, `Senses/Network.cs`, `Senses/FullscreenWatcher.cs`, `Host/Tray.cs`, `Host/Director.cs` (ties senses, brain, expressions, motions); Modify `tests/ui/run.ps1` (stage `events`)

**Interfaces:**
- Consumes: `Brain`, `EventKind`, `QuietReason`, `Fullscreen.IsFullscreen` (Task 6); `ExpressionPlayer`, `MotionController`.
- Produces: pipe `{"type":"event","name":<wire name>}` posts to the brain; `Director` ticks the brain every 250 ms and on every event, plays `BrainOutput.ExpressionKey`, hides via `FullscreenHide` when `Hidden`; tray icon (icon C placeholder) with menu "إخفاء ساعة / Hide 1 h" and "إغلاق / Quit"; re-adds itself on the registered `TaskbarCreated` message.
- Senses: foreground via `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` (also feeds `Fullscreen.IsFullscreen` with `GetWindowRect`, `MonitorFromWindow`, class name, `WS_CAPTION`); power via `GetSystemPowerStatus` + `WM_POWERBROADCAST` (battery.low at < 20% and not charging, battery.charging on plug-in); idle via `GetLastInputInfo` every 5 s (`user.away` after 5 min, `user.back` on input); session lock via `WTSRegisterSessionNotification` (`sleep`/`back`); network via `NetworkInformation.NetworkStatusChanged`.
- [ ] **Step 1:** `run.ps1` stage `events`: post `claude.permission` → `state.expression == "claude-permission"`; post `music` then `claude.permission` → permission wins, after 8 s back to `music`; post `screenshot` and `clipboard.copied` quickly → second waits; start a borderless window covering the screen (PowerShell WinForms, `FormBorderStyle=None`, `WindowState=Maximized`, `TopMost`) → `state.hidden == true` within 1 s, close it → `false`; a maximized captioned window → `hidden == false`; broadcast `TaskbarCreated` → tray icon present (agent-desktop `snapshot --surface system-tray-overflow` or `system-tray` lists "Sahar"). Push → FAIL.
- [ ] **Step 2:** Implement.
- [ ] **Step 3:** Push → `events` PASS.
- [ ] **Step 4:** Commit `feat(engine): senses wired to the brain, minimal tray`.

### Task 13: Performance gate and portable test build

**Files:** Modify `tests/ui/run.ps1` (stage `perf`); Create `.github/workflows/test-build.yml`, `packaging/README-test-build.txt` (Arabic + English: unzip, run `Sahar.Engine.exe`, quit from tray, what to test, how to send feedback)

**Interfaces:**
- Consumes: everything above.
- Produces: GitHub pre-release `test-build` with `Sahar-0.1.0-test-win-x64.zip` (engine exe + README); `state.workingSetBytes` used by the perf stage.
- [ ] **Step 1:** `run.ps1` stage `perf`: after 30 s at rest, `state.workingSetBytes < 60 * 1024 * 1024` (hard fail); sample `TotalProcessorTime` over 20 s and print CPU % (warning only on the runner). Push → verify PASS/FAIL honestly; if RAM fails, profile (`DOTNET_gcServer=0`, trim surfaces) before continuing.
- [ ] **Step 2:** `test-build.yml` (workflow_dispatch + tag `v0.1.*-test`): run build + ui-test, then publish the zip as pre-release `test-build` (delete and recreate, like the spike workflow), attaching `gallery.png`.
- [ ] **Step 3:** Run it; download the zip in CI's own artifact and check it contains exactly `Sahar.Engine.exe` and `README-test-build.txt`.
- [ ] **Step 4:** Commit `ci: performance gate and portable test build`.
