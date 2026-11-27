# CI speed ledger (branch ci/speed, from main 39c523e / 0.16.0)

Goal (owner): a release cycle from ~6–8 h to ~3–4 h. Four fixes, approved by the owner.

## What changed
- **test-build reuses a full UI pass** (`tools/ci/prior_runs.py ui`): a "ui-test all" run on the same SHA whose build and
  UI jobs all passed spares the ~37 min serial UI run — now for `publish=false` too (0.16.0's branch test-build re-ran it:
  45963348861, 42 min). A "ui-test all" still running on the SHA is waited for (≤ 40 min). `force_ui` runs every stage
  anyway. smoke + gallery still run (the zip's engine starts; the release carries gallery.png).
- **ui-test `stages=auto`** (+ `base`, default origin/main): `tools/ci/ui_stages.py` maps the branch's changes to the
  stages that can see them, plus smoke. run.ps1 maps by the `Stage-X` function holding each changed line; shared
  helpers, the engine, the settings app (her surface host), Core outside a few owned folders, build files and unknown
  paths run every stage. `all` stays what releases (and every push to main) run.
- **mind-eval only for her brain**: in ui-test and test-build it runs when her brain's files changed (`BRAIN` in
  ui_stages.py: src/Sahar.Mind, Core/Engine Mind, Core assets/mind, tools/mind with pins.json, tests/mind, the eval's
  own files) since the base (ui-test: `base`, or the commit before a push; test-build on main: the last published
  test build's commit) and no mind-eval passed or is running on the SHA; or when asked (`mind_eval=yes`,
  `stages=mindeval`). It runs beside the stages and never holds the publish back.
- **Caches**: NuGet (`~/.nuget/packages`, by the project files) in every publishing Windows job; the pinned llama.cpp
  zips (and the CI model in test-build) by `tools/mind/pins.json`. The model caches were already there.
  `fetch.ps1` retries a failed download once (45861363040: GitHub's "Unicorn!" page after 2 min).
- ui-test's concurrency group includes the stages: "all" and "mindeval" (or "auto") no longer cancel each other.

## Flakes (failed, then passed on a re-run or on the same SHA elsewhere; ui-test runs of 2026-11-13…27)
| Step (stage) | Seen | Cause | Fix |
|---|---|---|---|
| a flight moves her smoothly (face) | 45963289158 #1 (39c523e) | `showing=false` comes as the game bubble *starts* its 150 ms fade; the burst began at once and frame +27 ms still had «ولا مرة مسكتني» over her: the white-pixel centroid jumped 63 px | wait 500 ms after the bubble goes |
| a drag moves her smoothly (face) | 45969673666 (39c523e, main) | a frame judged only by its grab time: after a stalled grab the next picture is one composition later than its stamp, and the drag moves the pointer per grab, not per ms (6.8 px off, limit 5.5) | a frame fits by time **or** halfway between its neighbours; a stale frame (a step off and back) misses both |
| a flight … (face), 6.8 px | 44771580600 (f3e0fd7, main) | same | same |
| a tap: one face at a time — "only 16/17 fps" (face) | 45867034486 #1, 44771580600, 45889618186, 45882237735, 45874008077 | the 18 fps *average* is the runner's BitBlt rate (16–24 fps), not hers | ask for what the check needs: ≥ 15 frames in the 1.5 s after the tap and no gap > 200 ms (CI: 23–36 frames, gaps ≤ 156 ms) |
| picking an item puts it back (senses) | 45879305217 #1 (b3db24f) | 30 copies 150 ms apart: a busy runner coalesces copies (45887095062 kept 15 of 30), and a history still filling moved the item picked; a fixed 500 ms sleep before reading the clipboard | each copy lands before the next; the pick waits for the clipboard (≤ 3 s) |
| hide and seek: «برد…» (games) | 45876790889, 45874008077 | the first whisper (5 s after she hid) was about the pointer's start corner (Warm), and the next comes 3 s later; in the dark step the pointer never moved at all (a spot is only 300 DIP away, «برد…» needs 450) | the pointer rests in the far corner as soon as she has hidden, in both steps |
| hide and seek: never behind the test window in 8 games (games) | 45958700392 | the window is picked at random among the runner's windows | test hook `game-start … "window":"<hwnd>"` (GamePlayer.SeekPreferWindow) puts that window's spots first; the retry loop stays |
| Quran: no network … then every step "Connect timed out" (menu) | 44771580600 (f3e0fd7, main), 45882237735 | her UI thread froze (pipe silent > 5 s) after a refused stream: MediaPlayer Pause/Source/Dispose ran on it | MediaRecitation.Dispose runs on a worker (like Seek, review I6) |
| test-build: llama.cpp download (build) | 45861363040 #1 | GitHub release download error | one retry + cache |

Not fixed here (not seen since 0.14.1, or a real bug then fixed on the branch): «no face moment while the browser
plays» (44760115118, 0.14.1), «still 646 px from the typing spot» (43611917236, 0.14.0), the menu/bubble memory step
(0.14.0), the 0.16 update-question and settings-folder steps (fixed on feat/0.16-look before 39c523e).

## Proof runs
- ui-test all **45996716033** on 46b10fa: green, no re-run. UI part 15.4 min (12:12:32 → 12:27:54); mind-eval ran
  beside it (tools/mind/fetch.ps1 changed) and passed in 23.5 min. The face tap burst ran at 17 fps — the old check
  would have failed it — and passed with 24 frames in the fade, largest gap 144 ms; hide-and-seek found the test window
  on the first game; the thirty copies kept items 30…11 exactly.
- test-build publish=false **45996921271** on the same SHA: waited 9 min for that ui-test, then ran smoke + gallery
  (66 s) instead of every stage (37.3 min in 45963348861); its own mind-eval skipped (one was already going).
  15.8 min in all, against 42 min for 0.16.0's (45963348861).
- ui-test stages=auto base=46b10fa **45999425636** on b6af6af (this ledger, docs only): plan picked `stages=smoke`,
  `brain=false` → mind-eval skipped; 5.1 min. Its build job restored the NuGet and llama.cpp caches (Native AOT publish
  57 s against 77–81 s; the zips "cached, verified").

## Before / after (one release gate: ui-test all + test-build + mind-eval on the candidate)
- Before, 0.16.0 on 39c523e: ui-test all 45963289158 22 min (a face flake, re-run), test-build 45963348861 42 min (the
  UI suite again, serially), ui-test mindeval 45966753654 20 min by hand after: 08:07 → 08:54, **47 min**; then the
  push to main's ui-test all 45969673666 failed on another face flake.
- After, ci/speed: ui-test all with mind-eval beside it + test-build started together: 12:12 → 12:36, **23.5 min**
  (about 16 min when her brain is untouched: no mind-eval; test-build then ends ~2 min after the UI jobs).
