# Sahar v1 Plan 4: her remaining senses, the faces they bring, and gentle comments

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sahar notices copying (with a 📋 clipboard history in her menu), finished downloads, screenshots, a busy CPU, the camera or microphone in use, rain coming (opt-in), and how long you have been on an app — showing the matching faces (§5 #7, #10, #17, #18, #21) and an occasional gentle comment; test build 0.5.0.

**Architecture:** As before, every decision is pure Core code with unit tests (clipboard rules and history, screenshot keys, download names, CPU heat, camera/mic use, rain rules, app time, comment lines). The engine adds thin senses: a clipboard listener and raw-input key codes on the tray window, a FileSystemWatcher on Downloads, a 10 s CPU sample, a 5 s registry check for camera/mic, an hourly Open-Meteo fetch when weather is on, and a speech bubble drawn on the overlay. Test-mode pipe commands feed each sense so UI tests don't depend on the runner's hardware or the network.

**Tech Stack:** C# .NET 10 Native AOT, Win32 (AddClipboardFormatListener, Raw Input, registry), System.IO.FileSystemWatcher, HttpClient + System.Text.Json source generation, Windows.UI.Composition, xUnit, PowerShell + agent-desktop.

**Spec:** `docs/superpowers/specs/2026-11-06-sahar-windows-v1-design.md` — §5 (#7 rain, #10 download, #17 screenshot, #18 copied, #21 hot), §7.1 (📋 الحافظة), §7.2 (clipboard rules), §8.1 (senses), §8.2 (quiet rules, comments at most every 45 min and fewer when ignored), §14 (privacy). Builds on Plan 3 (branch v1/plan-3, 0.4.0 not yet published).

## Global Constraints

- §7.2 clipboard: "آخر 20 شي نسخته (نص، رابط، صورة). كبسة بترجّعه للحافظة. ما بتحفظ شي من برامج كلمات السر"; skip content marked `ExcludeClipboardContentFromMonitorProcessing` or `CanIncludeInClipboardHistory = 0`; history stays on this PC; a "امسح الكل" button.
- §8.1 downloads: `.crdownload` / `.part` finishing; screenshots: Print Screen or Win+Shift+S ("بتعرف إنك ضغطت، ما بتشوف الصورة"); CPU "hot": above 85 % for two minutes; camera/mic from `CapabilityAccessManager\ConsentStore`; weather from Open-Meteo hourly, "بس إذا شغّلته".
- §8.2: camera or microphone in use → quiet (no comments); comments at most once per 45 minutes, and fewer when ignored (Core `CommentLimiter`).
- §8.1 level 1 of "sees and discusses": she knows the app in front and for how long, comments gently; **no screenshots, no reading screen content**. Comments name the app only, never window titles (ruling below).
- §14: nothing leaves the PC except Open-Meteo (city name for geocoding, then coordinates) when weather is on; a blocked-apps list (password managers, private browsing) gets no time tracking and no clipboard history.
- The menu shows only working buttons (user's choice): 📋 الحافظة joins now, after 📝, so the order is timer, note, clipboard, activity, dnd, hide, play.
- CPU at rest stays < 0.5 %: no new timer faster than 5 s at rest; RAM at rest < 60 MB.
- No credits are spent (Open-Meteo is free and keyless).
- Version 0.5.0.

## Review Focus

1. **Copying from a password manager or marked-private content:** never stored, never shown, no face. → Task 1 `ClipboardRules_*` tests; Task 2 UI step copies with the exclusion format set.
2. **Copying fast and often (Ctrl+C held, a script):** history keeps 20, consecutive duplicates collapse, the copied face at most once per 30 s, no CPU spike. → Task 1 `ClipboardHistory_*`; Task 2 UI step copies 30 items quickly.
3. **Downloads that never finish, are cancelled, or are written straight to their final name:** cancelled/temp files make no face; a direct write counts once its size stops changing for 2 s. → Task 1 `DownloadRules_*`; Task 3 UI steps with a temp file renamed, deleted, and a direct write.
4. **No network or a city Open-Meteo doesn't know:** weather stays off with a short message in the bubble, she never retries more than once an hour, and nothing else breaks. → Task 6 UI step with the stubbed fetcher failing.
5. **Comments while you are busy or quiet** (typing, camera/mic, DND, fullscreen, menu open): none; a comment you click away makes the next one wait longer. → Task 5 `CommentRules_*` and UI step.

Rulings taken while writing (the spec is silent or a documented API is missing):
- Windows "Focus Assist / Do not disturb" has no documented API; Sahar's own DND (Plan 3) covers §5 #14 and the README says so. Cost if wrong: Windows DND alone does not quiet her.
- Comments use the app's friendly name only (Chrome, Word, VS Code…), never the window title (titles can hold private text). Cost if wrong: comments are a little less specific than the spec's example.
- Images in the clipboard history are kept as `.bmp` files (no image codec in AOT without new dependencies), at most 16 MB each, and count toward the 20. Cost if wrong: disk use up to ~320 MB worst case; "امسح الكل" frees it.

---

### Task 1: Core rules for the new senses

**Files:** Create in `src/Sahar.Core/Senses/`: `ClipboardRules.cs`, `ClipboardHistory.cs`, `ScreenshotKeys.cs`, `DownloadRules.cs`, `CpuHeat.cs`, `CapabilityUse.cs`, `WeatherRules.cs`, `AppTime.cs`, `CommentRules.cs`; modify `src/Sahar.Core/Menu/MenuItems.cs`, `src/Sahar.Core/Settings/SaharSettings.cs` (weather city/lat/lon). Tests: `tests/Sahar.Core.Tests/Senses/SensesTests.cs`, update `MenuTests`.

**Interfaces (Produces):**
- `ClipboardRules.ShouldKeep(IReadOnlyCollection<string> formatNames, uint? canIncludeInHistory, string ownerExe) -> bool` (false for `ExcludeClipboardContentFromMonitorProcessing`, `Clipboard Viewer Ignore`, `CanIncludeInClipboardHistory = 0`, or an owner in `PrivacyLists.PasswordManagers`); `ClipboardRules.KindOf(string text) -> ClipKind { Text, Link }`.
- `record ClipItem(ClipKind Kind, string? Text, string? ImageFile, int Width, int Height, DateTimeOffset At)`; `enum ClipKind { Text, Link, Image }`; `ClipboardHistory(string folder)` with `IReadOnlyList<ClipItem> Items` (newest first), `bool AddText(string, DateTimeOffset)`, `bool AddImage(byte[] dib, int w, int h, DateTimeOffset)`, `void Clear()`; keeps 20, drops consecutive duplicates, text ≤ 100 000 chars, image ≤ 16 MB, stored in `clipboard.json` + `clip-*.bmp`, corrupt file → `.bad` and empty, unreadable → never overwritten (like `NotesStore`).
- `ScreenshotKeys.Update(int vk, bool down) -> bool` (true on Print Screen down, or S down while a Win key and Shift are down).
- `DownloadRules.IsTemp(string name) -> bool` (`.crdownload`, `.part`, `.partial`, `.download`, `.tmp`, `~$*`); `DownloadRules.FinishedByRename(string oldName, string newName) -> bool`.
- `CpuHeat.Update(DateTimeOffset now, float busyPercent) -> bool` (true once after > 85 % for 2 min; re-arms after < 60 % for 2 min).
- `CapabilityUse.InUse(IEnumerable<long> lastUsedTimeStop) -> bool` (any 0).
- `WeatherRules.RainSoon(IReadOnlyList<int> weatherCodes, IReadOnlyList<int> precipitationProbability) -> bool` (next 2 hours: a WMO rain/snow/storm code 51–67, 71–77, 80–86, 95–99, or probability ≥ 60); `WeatherRules.ShouldFetch(DateTimeOffset now, DateTimeOffset? last) -> bool` (hourly).
- `AppTime` with `void Front(string exe, DateTimeOffset now)`, `void Away(DateTimeOffset now)`, `TimeSpan Continuous(DateTimeOffset now)`, `string? Current`; a new local day resets; blocked apps are not tracked.
- `CommentRules.Pick(string? exe, TimeSpan onIt, int hour, bool firstToday) -> string?` — Syrian Arabic lines, app friendly names from `AppNames.Friendly(exe)` (chrome→Chrome, msedge→Edge, Code→VS Code, WINWORD→Word, EXCEL→Excel, POWERPNT→PowerPoint, figma→Figma, Photoshop→Photoshop, Teams/ms-teams→Teams, slack→Slack, WhatsApp→WhatsApp, Telegram→Telegram, Discord→Discord, Notion→Notion; unknown → no name in the line): first activity of the day before noon → "صباح الخير ☀️"; ≥ 120 min on one app → "صار لك ساعتين على {app}، كيف ماشي؟"; ≥ 60 min → "ساعة كاملة على {app}… خود نفس 🌿"; otherwise null. `CommentRules.Allowed(bool typing, bool quiet, bool menuOpen, bool hidden) -> bool`.
- `PrivacyLists.PasswordManagers` (keepass, keepassxc, 1password, bitwarden, lastpass, dashlane, keeper, enpass, roboform, nordpass, protonpass) and `PrivacyLists.IsPrivateWindow(string title)` (InPrivate, Incognito, Private Browsing, نافذة خاصة).
- `MenuItems.Main` gains `new("clipboard", "📋", "الحافظة")` after note.

- [ ] **Step 1:** Write the failing tests: `ClipboardRules_SkipsExcludedFormats`, `ClipboardRules_SkipsHistoryZero`, `ClipboardRules_SkipsPasswordManagers`, `ClipboardRules_LinkVsText`; `ClipboardHistory_Keeps20NewestFirst`, `ClipboardHistory_CollapsesConsecutiveDuplicates`, `ClipboardHistory_Clear_RemovesImages`, `ClipboardHistory_SurvivesRestart`, `ClipboardHistory_UnreadableNeverOverwritten`; `ScreenshotKeys_PrintScreen`, `ScreenshotKeys_WinShiftS`, `ScreenshotKeys_PlainS_No`; `DownloadRules_TempNames`, `DownloadRules_RenameFromTemp_Finished`, `DownloadRules_RenameBetweenFinals_No`; `CpuHeat_TwoMinutesAbove85_Once`, `CpuHeat_Rearms`; `CapabilityUse_ZeroStopMeansInUse`; `WeatherRules_RainCodes`, `WeatherRules_Probability`, `WeatherRules_Hourly`; `AppTime_ContinuousOnOneApp`, `AppTime_SwitchResets`, `AppTime_AwayPauses`, `AppTime_NewDayResets`; `CommentRules_Lines`, `CommentRules_NotWhileBusy`; `MenuItems_Main_IsExactlyTheWorkingButtons` (now 7).
- [ ] **Step 2:** `dotnet test tests/Sahar.Core.Tests` → FAIL (behaviour).
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `dotnet test tests/Sahar.Core.Tests` → PASS.
- [ ] **Step 5:** Commit `feat(core): rules for clipboard, screenshots, downloads, CPU, camera/mic, rain, app time and comments`.

### Task 2: Clipboard sense and the 📋 history

**Files:** Create `src/Sahar.Engine/Senses/ClipboardSense.cs`, `src/Sahar.Engine/Tools/ListBubble.cs`, `src/Sahar.Engine/Tools/ClipboardTool.cs`; modify `Host/Tray.cs` (AddClipboardFormatListener, WM_CLIPBOARDUPDATE), `App.cs`, `tests/ui/run.ps1` (stage `senses`, both workflows).
- Listener on the tray window; reads format names (`EnumClipboardFormats` + `GetClipboardFormatName`), the `CanIncludeInClipboardHistory` DWORD, the owner's exe (`GetClipboardOwner`), then CF_UNICODETEXT or CF_DIB; our own writes are ignored. Copied → `Express("copied", transient)` at most once per 30 s.
- `ListBubble` (Win32 popup like `TextBubble`): a LISTBOX (`LBS_NOTIFY`, RTL) with up to 20 lines ("🔗 …", "📄 …" first 60 chars, "🖼️ صورة W×H") and a "امسح الكل" button; a click on a line copies it back and closes; Esc / outside closes.
- Test-mode pipe: `debug-clipboard` → `count=… first=<base64 of first text>`; `clipboard-pick {index}`.
- [ ] **Step 1:** UI steps: 'copying text shows the copied face and lands in the history' (PowerShell `Set-Clipboard`); 'a password manager / excluded copy is never kept' (C# helper sets text plus the `ExcludeClipboardContentFromMonitorProcessing` format); 'thirty quick copies keep twenty, one face'; 'picking an item puts it back on the clipboard' (`Get-Clipboard`); 'clear all empties it'; menu shows 7 buttons with `clipboard` third. Push → FAIL.
- [ ] **Step 2:** Implement; push → PASS.
- [ ] **Step 3:** Commit `feat(senses): clipboard history`.

### Task 3: Downloads, screenshots and a hot CPU

**Files:** Create `Senses/Downloads.cs`, `Senses/CpuSense.cs`; modify `Host/Tray.cs` (raw input key codes → `ScreenshotKeys`), `App.cs`, `tests/ui/run.ps1`.
- Downloads: `FileSystemWatcher` on the Downloads known folder (`SHGetKnownFolderPath`), Renamed/Created/Changed; finished → `DownloadDone` (one per 10 s); a direct write counts after 2 s without size change. Test mode: `--downloads <dir>` overrides the folder.
- Screenshot keys → `Screenshot` event. CPU: `GetSystemTimes` every 10 s → `CpuHeat` → `CpuHot`. Test mode: `debug-cpu {percent}` feeds a sample with a fake clock step.
- [ ] **Step 1:** UI steps: temp file renamed to final → `download` face; deleted temp → nothing; direct write → face after ~2 s; `SendKeys` `{PRTSC}` → `screenshot` face; `debug-cpu` 90 % for 13 samples (2 min of fake time) → `hot`. Push → FAIL.
- [ ] **Step 2:** Implement; push → PASS.
- [ ] **Step 3:** Commit `feat(senses): downloads, screenshots, hot CPU`.

### Task 4: Camera or microphone in use → quiet

**Files:** Create `Senses/CameraMic.cs`; modify `Host/Director.cs` (quiet flag `CameraOrMic`), `Life/LifeDirector.cs` (quiet → only small things), `App.cs`, `tests/ui/run.ps1`.
- Every 5 s read `HKCU\…\CapabilityAccessManager\ConsentStore\webcam` and `\microphone` (and their `NonPackaged` subkeys) `LastUsedTimeStop`; any 0 → in use. Test mode: `debug-cameramic {on}`.
- [ ] **Step 1:** UI step: `debug-cameramic on=true` → `debug-life` shows quiet; `life gap=2` 10 s → only Hover/Breathe; a forced comment (`comment-now`) does not show; off → back. Push → FAIL.
- [ ] **Step 2:** Implement; push → PASS.
- [ ] **Step 3:** Commit `feat(senses): quiet while the camera or microphone is on`.

### Task 5: App time and gentle comments

**Files:** Create `Senses/AppWatch.cs`, `Render/SpeechBubble.cs`; modify `App.cs`, `Life/LifeDirector.cs`, `tests/ui/run.ps1`.
- Foreground exe via `Native.WindowProcessPath` on `Foreground.Changed`; away (idle sense) pauses; private windows and blocked apps not tracked.
- Every minute: if `CommentRules.Allowed` and `CommentLimiter.TryComment`, show `CommentRules.Pick` in a speech bubble above her (white rounded pill, `#1E2350`, Segoe UI 13 DIP, max 260 DIP, two lines, stays 8 s); a click on her while it shows → `Dismissed` (next one waits longer) and it closes; if it times out unclicked → `Accepted`.
- Test mode: `debug-apptime` → `app=… minutes=…`; `apptime-skip {minutes}` advances the fake clock; `comment-now` asks for a comment immediately (still obeys the rules and the limiter).
- [ ] **Step 1:** UI steps: front a test window, `apptime-skip 125` → `comment-now` → a bubble with text (`debug-comment` → `showing=true text64=…` contains "ساعتين"); click her → closed, `debug-comment` shows the next gap > 45 min; while typing → no comment. Push → FAIL.
- [ ] **Step 2:** Implement; push → PASS.
- [ ] **Step 3:** Commit `feat: app time and gentle comments`.

### Task 6: Rain coming (opt-in weather)

**Files:** Create `Senses/Weather.cs` (HttpClient, JSON source generation), modify `Host/Tray.cs` (item "الطقس… / Weather…"), `Settings/SaharSettings.cs` use, `App.cs`, `tests/ui/run.ps1`.
- Tray item opens a `TextBubble` "شو مدينتك؟"; Open-Meteo geocoding (`https://geocoding-api.open-meteo.com/v1/search?name=…&count=1&language=ar`) → city, lat, lon saved, weather on; hourly forecast (`https://api.open-meteo.com/v1/forecast?latitude=…&longitude=…&hourly=weather_code,precipitation_probability&forecast_hours=2&timezone=auto`) → `RainSoon` → `WeatherRain` (once per 3 h). The tray item then reads "الطقس: {city} — وقّف / Weather: {city} — off".
- Failures: a short line in the bubble ("ما لقيت هالمدينة" / "ما في نت هلق"), logged; retry at most hourly.
- Test mode: `--weather-stub <dir>` makes the fetcher read `geocode.json` / `forecast.json` from a folder (or fail if missing).
- [ ] **Step 1:** UI steps: stub with a rainy forecast → set city via the bubble → `rain` face; stub with no files → bubble says no network, weather stays off. Push → FAIL.
- [ ] **Step 2:** Implement; push → PASS.
- [ ] **Step 3:** Commit `feat(senses): rain coming (Open-Meteo, opt-in)`.

### Task 7: Test build 0.5.0

**Files:** `Directory.Build.props`, `CoreInfo.cs`, `app.manifest`, `SmokeTests.cs`, `packaging/README-test-build.txt` (what's new; manual checks: copy from a password manager, a real download, Win+Shift+S, a video call, a long session on one app, weather in your city; Windows Focus Assist note).
- [ ] **Step 1:** `CoreInfo_Version_Is_0_5_0` RED → bump → GREEN; all stages pass; test-build zip = exe + README.
- [ ] **Step 2:** Commit `feat: test build 0.5.0`. Publishing waits for the user's OK.
