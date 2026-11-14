# Sahar installer + auto-update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sahar installs from `Sahar-Setup-<version>.exe` (per user, Arabic) and offers newer versions from GitHub Releases in her bubble, never silently.

**Architecture:** Inno Setup 6 script in `packaging/installer/`, compiled by one PowerShell script that both workflows call. All update decisions live in `Sahar.Core.Update` (pure, unit-tested); the engine's `Updater` only does HTTP, files and process start. A new `install` UI stage drives the real installer and a local static fake of the GitHub API.

**Tech Stack:** C# .NET 10 (Core + Native AOT engine), xUnit v3, Inno Setup 6.7.4, PowerShell 7, GitHub Actions `windows-latest`.

**Spec:** the owner-approved roadmap item "installer + auto-update" as relayed in the task brief of 2026-11-13 (no separate spec file; the brief's bullets are quoted below as Global Constraints).

## Global Constraints

- Per-user install, no admin, into `%LocalAppData%\Programs\Sahar`; Start-menu shortcut «سحر»; optional "start with Windows" (default on); uninstall entry in Settings → Apps.
- Arabic UI with English fallback.
- Upgrade keeps `%AppData%\Sahar` (settings) and `%LocalAppData%\Sahar` (log).
- A running Sahar is closed gracefully (the existing pipe `quit`), never killed.
- Uninstall removes program files and shortcuts; asks before removing data; default keep.
- Unsigned: README documents «More info → Run anyway» in simple Arabic.
- Update check at start + 2 min, then every 12 h; channel is a setting, default the `test-build` release.
- Compare with `CoreInfo.Version` (semantic versions).
- Ask «في نسخة جديدة مني 0.x.y، بدك ثبّتها؟» with yes / later; never update silently; later = ask again next day.
- Yes: download `Sahar-Setup-*.exe` to temp, verify against the release's `.sha256`, run it silently; the installer restarts Sahar.
- Tray item «دوّر على تحديث».
- Network: only `api.github.com` and release asset URLs; timeouts; failures are a log line only. User-Agent `Sahar/<version>`; never log titles, typed text, clipboard, user name (or paths, which hold it).
- CI never hits the real GitHub API in tests. Keep `tools/ci` stage lists consistent (run.ps1, test-build full run, ui_groups). Do not bump the version.

## Review Focus

1. A redirect from a release asset to a host outside the allow-list must not be followed (the engine follows redirects by hand, checking each hop with `UpdateRules.AllowedUrl`).
2. A release with a setup but no `.sha256` (or a `.sha256` naming another file) must offer nothing — never "install unverified".
3. A user who closes the question bubble (Esc / clicks away) must not be asked again within the day: asking itself sets the snooze.
4. Sahar that does not quit within the wait (hung, or busy) makes setup/uninstall stop with a message, not a kill.
5. Non-test engines started by the installer in CI must not reach GitHub: the stage writes `updateCheckEnabled: false` first; test mode never checks without `--update-api`.

---

### Task 1: Semantic versions (Core)

**Files:** Create `src/Sahar.Core/Update/SemVersion.cs`; Test `tests/Sahar.Core.Tests/Update/SemVersionTests.cs`

**Interfaces — Produces:** `readonly record struct SemVersion(int Major, int Minor, int Patch, string? Pre) : IComparable<SemVersion>`; `static bool TryParse(string? s, out SemVersion v)` (optional leading `v`, `+build` ignored, prerelease after `-`); `CompareTo` per semver 2.0 §11; operators `<`, `>`; `ToString()` → `0.12.0` / `0.12.0-rc.1`.

- [ ] Tests: `0.12.0 > 0.11.0`, `0.11.10 > 0.11.9`, `1.0.0 > 0.99.99`, `0.12.0-rc.1 < 0.12.0`, `rc.2 < rc.10`, `alpha < alpha.1 < beta`, `v0.11.0 == 0.11.0`, `+build` ignored; rejects `""`, `1.2`, `1.2.3.4`, `a.b.c`, `01.2.3`; `CoreInfo.Version` parses. Run → RED (type missing).
- [ ] Implement; run `dotnet test tests/Sahar.Core.Tests --filter FullyQualifiedName~Update` → GREEN. Commit.

### Task 2: Release → offer, URL allow-list, checksum (Core)

**Files:** Create `src/Sahar.Core/Update/Release.cs`, `UpdateRules.cs`; modify `src/Sahar.Core/Json/` (new `UpdateJson` context); Test `tests/Sahar.Core.Tests/Update/UpdateRulesTests.cs`

**Interfaces — Produces:**
- `sealed record ReleaseAsset(string Name, string Url, long Size)`; `sealed record Release(string Tag, bool Draft, IReadOnlyList<ReleaseAsset> Assets)`; `static Release? Release.Parse(string json)` (GitHub fields `tag_name`, `draft`, `assets[].name|browser_download_url|size`; null on bad JSON).
- `sealed record UpdateOffer(SemVersion Version, ReleaseAsset Setup, ReleaseAsset Checksum)`.
- `UpdateRules.Pick(Release r, SemVersion current, bool allowLoopback) → UpdateOffer?`: the highest `Sahar-Setup-<semver>.exe` with an asset named exactly `<setup name>.sha256`, newer than current, both URLs allowed, release not draft.
- `UpdateRules.AllowedUrl(string url, bool allowLoopback) → bool`: https to `api.github.com`, `github.com`, `*.githubusercontent.com`; plain http only to `127.0.0.1`/`localhost` and only when `allowLoopback`.
- `UpdateRules.ParseChecksum(string text, string fileName) → string?`: first 64-hex token, lowercase; a named file (`hex  name` / `hex *name`) must equal `fileName`.
- `UpdateRules.Matches(Stream s, string hex) → bool` (SHA-256).
- `UpdateRules.SetupArguments(string logPath) → "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG=\"<logPath>\""`.

- [ ] Tests (canned GitHub JSON in the test): picks 0.12.0 over 0.11.0 and a non-setup asset; none when only 0.11.0 (equal) or older; none without `.sha256`; none when the checksum asset is for another file; none for a draft; none for an `http://evil` or `https://evil.com` URL; loopback only when allowed; checksum parse (both formats, bare hex, uppercase→lower, wrong name → null, short hex → null); `Matches` true/false on known bytes; bad JSON → null. RED.
- [ ] Implement with a source-generated `UpdateJson` context (AOT). GREEN. Commit.

### Task 3: Schedule, channel and settings (Core)

**Files:** Create `src/Sahar.Core/Update/UpdateSchedule.cs`; modify `src/Sahar.Core/Settings/SaharSettings.cs`; Test `tests/Sahar.Core.Tests/Update/UpdateScheduleTests.cs`, settings round-trip in the same file.

**Interfaces — Produces:**
- `enum UpdateChannel { TestBuild, Stable }`; `UpdateRules.ReleaseApi(UpdateChannel c, string? apiBase) → Uri`: `{apiBase ?? https://api.github.com}/repos/A7ma-B25/sahar/releases/tags/test-build` or `/releases/latest`; `UpdateRules.UserAgent` = `Sahar/{CoreInfo.Version}`.
- `sealed class UpdateSchedule(DateTimeOffset started)`: `FirstDelay` 2 min, `Every` 12 h, `BusyRetry` 10 min, `AskAgainAfter` 24 h; `NextCheck`; `bool Due(now)`; `Checked(now)`; `Failed(now)` (backoff 15 min · 2ⁿ⁻¹, capped at 12 h; reset by `Checked`); `Busy(now)`.
- `enum UpdateStep { Nothing, Ask, UpToDate, Snoozed, Later }`; `UpdateSchedule.Decide(UpdateOffer? offer, bool manual, bool busy, DateTimeOffset now, DateTimeOffset? askAfter) → UpdateStep` (manual ignores snooze and busy; background: busy → Later, snoozed → Snoozed, no offer → Nothing; manual no offer → UpToDate).
- Settings: `UpdateChannel UpdateChannel = TestBuild`, `DateTimeOffset? UpdateAskAfter`. Asking sets `UpdateAskAfter = now + 24 h` (so a dismissed bubble is "later").

- [ ] Tests: first due at +2 min not before; then +12 h; failures 15/30/60 min… capped 12 h, reset after success; Decide table (each row above); API URLs for both channels and an `apiBase`; settings JSON round-trip keeps channel and askAfter and an old file without them gets the defaults. RED.
- [ ] Implement; GREEN; commit.

### Task 4: Engine — updater, tray item, `--quit`

**Files:** Create `src/Sahar.Engine/Tools/Updater.cs`, `src/Sahar.Engine/Host/QuitRunning.cs`; modify `Program.cs`, `EngineOptions.cs` (`--update-api <base>`, test mode only), `Host/Tray.cs` (item «دوّر على تحديث / Check for update», event `CheckUpdate`, `Pick("update")`), `App.cs` (wiring + test pipe commands `update-check {manual}`, `update-answer {yes|later}`, `debug-update`).

**Interfaces — Consumes:** Tasks 1–3. **Produces:** `Sahar.Engine.exe --quit` → sends `{"type":"cmd","name":"quit"}` on pipe `sahar`, waits ≤ 20 s for mutex `Local\Sahar.Engine` to go; exit 0 = not running, 4 = still running. Never kills.

- Updater: one `HttpClient` (`AllowAutoRedirect = false`, UA, Accept `application/vnd.github+json`, 20 s timeout for the API/.sha256, 10 min for the setup); every hop checked with `AllowedUrl` (≤ 5 hops); downloads to `%TEMP%\Sahar-update\` (cleared first); verify; start setup with `SetupArguments`; then the engine quits through the same path as the pipe `quit`. Failures: one log line (`update: … failed (<ExceptionType>)`); a user-initiated step also gets a short line from her.
- Automatic checks only when `UpdateCheckEnabled` and (not test mode, or `--update-api` given); busy = hidden, menu open, game, show, director quiet, away, typing.
- [ ] `dotnet build src/Sahar.Engine -p:EnableWindowsTargeting=true` → 0 errors; Core suite green. Commit. (Windows behaviour is proven in Task 6.)

### Task 5: Installer + build in both workflows

**Files:** Create `packaging/installer/Sahar.iss` (UTF-8 BOM), `packaging/installer/build.ps1`, `tools/ci/test_installer.py`; modify `.github/workflows/ui-test.yml` (build job builds the installer, uploads artifact `installer`; ui job downloads it), `.github/workflows/test-build.yml` (build, upload artifact, publish `.exe` + `.sha256` beside the zip).

- `.iss`: fixed `AppId`, `PrivilegesRequired=lowest`, `DefaultDirName={userpf}\Sahar`, `DisableDirPage/ProgramGroupPage/ReadyPage/WelcomePage=yes`, `CloseApplications=no`, languages arabic (first, default) + english, `ShowLanguageDialog=no`, `[Icons] {userprograms}\سحر`, task `startup` (checked) → `HKCU\…\Run` value `Sahar` (`uninsdeletevalue`; removed when the task is off), `[Run]` the engine `postinstall nowait` (runs in silent mode too = the restart after an update), Pascal: `PrepareToInstall` and `InitializeUninstall` run `--quit` (temp copy / installed copy) and stop with a message if it fails; `usPostUninstall` asks (default No) before deleting `{userappdata}\Sahar` and `{localappdata}\Sahar`; silent uninstall keeps.
- `build.ps1 -Version -Engine -Out`: use `ISCC.exe` if found, else download `innosetup-6.7.4.exe` from the official release (`github.com/jrsoftware/issrc/releases/download/is-6_7_4/`), check SHA-256 `a549929319f9120716677c685535ca4671e4b937b5fdc87e2585f32d8eb30e8c`, install it silently to `$env:RUNNER_TEMP`; compile; write `Sahar-Setup-<v>.exe.sha256` (`<hex>  <name>`).
- [ ] `test_installer.py` (RED first): the .iss has the directives above; both workflows call `build.ps1`; test-build publishes the `.exe` and `.sha256`; build.ps1 pins the hash. GREEN. Commit.

### Task 6: UI stage `install`

**Files:** modify `tests/ui/run.ps1` (`-Setup` param, `Stage-Install`, ValidateSet), `tools/ci/ui_groups.py` (add to the `menu` group), `.github/workflows/test-build.yml` (full list); fake API files are written by the stage under `artifacts/ui/fake-api/` and served by `python -m http.server --bind 127.0.0.1`.

- Steps: seed settings (`helloDone`, `updateCheckEnabled: false`) and marker files in both data folders → silent install → files, shortcut target, uninstall key (`DisplayVersion`), Run value, engine started from the install dir → silent upgrade over a running Sahar: previous run ended with `exit 0`, new process, markers and settings bytes kept → `/MERGETASKS="!startup"` removes the Run value → update: installed engine in test mode with `--update-api`; `update-check` offers 99.0.0 and shows the question; `later` sets askAfter ≈ +24 h; background check → Snoozed; wrong `.sha256` → `checksum mismatch`, no setup run; right one → engine quits, setup reinstalls, Sahar comes back (new PID) → silent uninstall: dir, shortcut, key, Run value gone; markers kept.
- [ ] `python3 -I -m unittest discover -s tools/ci -p 'test_*.py'` green; push; `gh workflow run ui-test.yml -f stages=install` green; then full `ui-test` and no-publish `test-build` green. Commit.

### Task 7: Owner docs

**Files:** modify `README.md` (install + SmartScreen steps in Arabic), `packaging/README-test-build.txt` (what's new: installer + updates, Arabic and English; install steps), `tools/ci/test_readme.py` (setup names in the README are this version).

- [ ] Extend `test_readme.py` (RED: no `Sahar-Setup-0.11.0.exe` yet) → write docs → GREEN. Commit.
