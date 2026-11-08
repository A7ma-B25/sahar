# Sahar — new look, menu and settings app (design spec, 2026-11-08)

Status: approved in conversation by the owner, 2026-11-08 (visual reference: design canvas
https://claude.ai/artifact/3AwcaNV2NwsPSTE2r5kiMk — pages «شكل سحورة» row 1, «بورسلان» row 1).
Inputs: `docs/design/2026-11-08-sahar-design-study.md` (UI study, "Porcelain"),
`docs/design/2026-11-08-sahar-character-study.md` (character audit), `PRODUCT.md`.
Supersedes for these surfaces: spec 2026-11-06 §7 (radial menu look) and §13 (settings app tech).

## 1. Scope

1. **Sahar's look:** keep the original rig exactly; fix star visibility on white; add stars inside her; a new elastic motion layer.
2. **The on-click menu:** a card beside her replaces the radial ring; adds a chat box (text + mic).
3. **The settings app:** a new separate program: a local web UI in a WebView2 window, nine pages including a new «القائمة» page.

Out of scope here: real AI answers (the "smart" phase), Sahar's voice output (voice phase), Now Bar features, installer/update.

## 2. Global constraints

- **Engine:**
  - C# .NET 10 Native AOT, Windows.UI.Composition + Direct2D.
  - Resting RAM < 60 MB; idle CPU < 1%.
  - Animation must be Composition-driven (expression/keyframe/natural-motion animations), not a per-frame CPU loop.
- **Settings app:**
  - A separate executable, opened on demand, fully exits when closed, never loads anything from the network.
- **Language and layout:**
  - Arabic-first RTL, English LTR.
  - Text contrast ≥ 4.5:1 in light and dark; meaningful graphics ≥ 3:1.
- **Owner rules:**
  - No feature of Sahar's original look is removed.
  - Buttons for features that don't work yet are hidden, not greyed out (rule from 2026-11-06).
- **Reduced motion:** Windows "Animation effects" off, or `prefers-reduced-motion` → all idle motion stops; state changes are instant.

## 3. Sahar's look (character)

**Source of truth:** `tools/art/design.py` rig (halo, dashed orbit, five four-point stars, moon with glow, leaf + glowing bud, gloss stroke, blush, eyes with two shines).

**Prerequisite:** `tools/art/export_rig.py` is out of sync with `design.py`. Today it would drop the Sick/Angry moods and expressions 26–29. Fix it and add a test that the exported rig contains every mood and expression listed in Core.

**Changes (nothing else changes):**

| # | Change | Values |
|---|---|---|
| L1 | Outer stars get a thin warm edge so they read on white | stroke `#C9963A`, 1.3 (in 300-unit rig space), round join; fill and twinkle unchanged |
| L2 | Night inside her lower body, clipped to the body path | radial wash centre (120,196) rx 86 ry 44: `#8E7BE0` @0.55 → `#A897EA` @0.28 → `#CBC1F0` @0 |
| L3 | Inner stars: 6 white four-point stars and 5 white dots inside the wash | stars at scale 0.32–0.55, opacity 0.15→1 and scale 0.6→1.1, periods 2.3–3.8 s with staggered starts; dots r 0.9–1.3, opacity 0.2→0.85 |
| L4 | Happy face while she hops or is tapped | eyes become up-arcs `M90 127 Q100 115 110 127` / `M130 127 Q140 115 150 127` (stroke 3.6, round) + small lash ticks; smile `M110 138 Q120 152 130 138`; blush opacity 0.55→0.85; lasts 1.1 s |

The wash tints with mood: inner stop = `palette[mood].glow` darkened by 0.12 OKLab L (Calm gives ≈`#8E7BE0`), mid stop = `palette[mood].glow`, outer = `palette[mood].bottom` at 0 opacity. Verify per mood that the white inner stars keep ≥ 1.8:1 against the wash centre (decorative, but must read).

## 4. Motion layer (the "soul")

Everything is relative to her resting pose. Pivot = bottom centre of the body.

| Behaviour | Spec |
|---|---|
| Float | translateY = −6·sin(2πt/4.2 s) DIP@medium (scales with size) |
| Follow-through | scaleY = 1 + 0.012·(−cos φ) + 0.012·sin(2πt/3.6 s); scaleX = 1/scaleY (volume kept) |
| Sway | rotate 1.6°·sin(φ/2) |
| Leaf lag | leaf rotate = 6°·sin(φ − 0.9) about the stem base (rig 122,60), plus a damped kick after a hop: −10°·e^(−4t)·sin(12t) |
| Hop (tap, idle, or any settings change while previewed) | **Anticipation** 90 ms: squash to sy 0.84. **Air** 400 ms: parabolic rise, peak 38 DIP; stretch up to sy 1.14; tilt +3°. **Land**: damped squash 0.2·e^(−7t)·cos(16t). Cooldown 0.75 s. Happy face (L4) during the hop. |
| Look | pupils (+ shines) offset up to (3.2, 2.4) rig units toward the cursor, eased (12%/frame equiv.); the body leans up to ±1.8° toward the cursor. Only when the cursor is within 400 DIP; otherwise she looks ahead. |
| Blink | random 2.6–6 s, 120 ms |
| Idle actions | every 6–12 s, either a small hop (power 0.6) or a look to one side for 1.2 s |
| Ground shadow | rx = 62 − 0.5·height, opacity 0.16 − 0.002·height |
| Moon | keeps orbiting the dashed ellipse (rx 112, ry 26, −10°), 16 s per lap |

**Engine mapping:**
- Float, sway, follow-through and leaf lag: looping expression animations on the body and leaf visuals.
- Hop: one keyframe animation set (scale.y, offset.y, rotation) plus a spring (`NaturalMotion`, damping 0.35) for the landing.
- Look: an expression bound to a property set fed by the pointer (updated only on mouse move, throttled to 30 Hz).
- These replace today's idle clocks (≈10 unrelated timers); existing motions and expressions keep working on top.

## 5. The menu (replaces the radial ring)

**Card** (opaque, no glass; tokens §7):
- 296 DIP wide, padding 8, radius 22, elevation e1.
- Opens beside her on the roomier side, 12 DIP from her body, top-aligned with her head, clamped to the work area.
- Opening: 180 ms, opacity + scale 0.96→1 from the corner nearest her. Closing: 120 ms.

**Moon clasp:** while the card is open, a 20 DIP moon rests on the card corner nearest her. Her own orbiting moon is hidden during that time.

**Header (mood region):**
- Min 84 tall, radius 14, background = mood tint (light: `#EEE8FF` for Calm, from `palette[mood].mid`; dark: `#3A3450`), three tiny star dots.
- Line 1 (17/600), in her voice: «شو بدك نعمل؟». It changes to her reply to a chat message (6 s), «عم اسمعك…» while listening, or «عم اسكت هلق» while hushed.
- Line 2: the single most urgent fact, with a moon dot, in `moon` colour, tabular digits. Order: running timer › Claude waiting › next prayer › Quran playing › hush remaining. Clicking it opens that item.

**Chat row:**
- Sunken field with the placeholder «احكي معي…», a mic button and a send button.
- **Enter / send:** runs the local command parser (§5.1) and shows her reply in the header. She hops.
- **Mic:** shown only when a working Windows speech recognizer is available for the UI language (`Windows.Media.SpeechRecognition`, dictation). Otherwise it is hidden.
  - While listening, the field gets an accent ring and the header says «عم اسمعك…».
  - Recognised text goes through the same parser.
  - Whether Arabic dictation works offline must be checked on the owner's machine; if it doesn't, the mic stays hidden for Arabic.

**Tiles (hers):** 3 × 86 tall, radius 12, hand-drawn stroke icons.
- «نلعب؟» opens the games list.
- «بسكت شوي» drills in to «قديش بدك ياني اسكت؟» with نص ساعة / ساعة / ساعتين / لحد ما تقلي. Once active, the tile reads «رجّعيني احكي».
- «بغفى شوي» hides her until woken; 1 h by default.

**Tools row (quiet):** مؤقت · ملاحظة · استماع · الحافظة. 58 tall, 19 px icons, 13 px `text-2` labels.

**Footer:** «إعداداتي» (opens the settings app) and «Esc لسكّر».

**Visibility:** every item can be turned on or off from Settings → القائمة. Items whose feature isn't available are hidden.

**Keyboard:**
- The global shortcut (default `Alt+S`, changeable) opens the menu with the caret in the chat field.
- Tab / arrow keys move through the items (mirrored in RTL). `1`–`3` pick the tiles; `Enter` activates.
- `Esc` goes back, then closes. On close, focus returns to the previous window.

**Rendering:** Direct2D/DirectWrite inside the engine.
- Rubik is loaded as a custom font collection; the hand-drawn icons are path data.
- Hover 120 ms; press scale 0.97.
- Drill-in: 160 ms crossfade.

### 5.1 Chat commands (v1, local, no AI)

Parsed in Core with Arabic and English patterns. Any number can be written in Arabic-Indic or Latin digits.

| Input | Action | Her reply |
|---|---|---|
| «مؤقت N» / "timer N" (N minutes; default 25) | starts the timer | «تمام! شغّلت مؤقت N دقيقة.» |
| «ملاحظة …» / "note …" | saves a note | «حفظتها.» |
| «اسكتي» or «اسكتي ساعة» | hush | «ماشي، رح اسكت…» |
| «نامي» | sleep | «تصبح على خير» (gendered) |
| «نلعب» | opens the games list | «يلا!» |
| «الصلاة» | shows the next prayer | «العصر بعد …» |
| greetings («كيفك», «شلونك», "hi") | — | a short friendly line |
| anything else | — | «سمعتك! لسا عم اتعلّم الحكي، وقريباً بجاوبك على كل شي.» |

All of her lines go through `Address.ToYou`.

## 6. The settings app

**Process and host:**
- `Sahar.Settings.exe`: .NET 10, not AOT, a WebView2 control in a plain Win32 window.
- Default 960×680, minimum 720×520.
- The caption is coloured to the page background with `DWMWA_CAPTION_COLOR`, plus immersive dark mode when the app is dark.

**UI:**
- Static HTML/CSS/JS bundled with the app and served from a virtual host mapping. No network.
- Rubik self-hosted.

**Bridge:**
- **Reading and writing:** the page talks to the host through `postMessage`. The host reads and writes `settings.json` through `SettingsStore`, the same code the engine uses.
- **Preview pipe:** a named-pipe line tells the engine to reload settings and lets the engine show a live preview, e.g. her size.

**Live Sahar preview:**
- An SVG port of the same rig and the same motion layer (§3–4).
- It hops on every change and says a line in her voice (e.g. «هيك بتشوفني أحسن!», «أهلين يا غالية!»).
- The rig SVG is generated from `design.py`, so there is one source.

**Pages:** the sidebar is grouped by spacing.
1. سحورة · القائمة
2. الأصوات · الصلاة · الاستماع
3. عام · الخصوصية · Claude Code
4. حول, at the foot

Page contents follow the UI study §3.2, plus:

| Page | Controls |
|---|---|
| **سحورة** | Preview · name · addressing (شب/صبية/ما بدي حدد) · size ×3 · activity ×3 with a changing line · "by herself" switches (comments on work, automatic mood, plays alone, feels the weather) |
| **القائمة** (new) | Item switches: chat box, نلعب, بسكت, بغفى, مؤقت, ملاحظة, استماع, الحافظة · header shows (أهم شي هلق / الصلاة الجاية / ولا شي) · opens with (كبستين / كبسة يمين / الاتنين) · position (جنبي / بنص الشاشة) · keyboard shortcut (record a new one; conflicts are refused with a message) |
| **الصلاة** | Today card with five fixed cells (tabular digits) · city search + Windows location · method · Asr (الجمهور/حنفي) · reminder switch + minutes (٥/١٠/١٥/٢٠) · adhan-silence note |

All copy is in her voice, first person («هون بتظبطني متل ما بدك…»). Every change applies instantly. Light and dark follow Windows, with an override in General.

**New settings fields** (`SaharSettings`, all with defaults so old files keep working):

| Field | Type / default |
|---|---|
| `MenuItems` | set of item ids; default: all available |
| `MenuHeader` | `Urgent` · `Prayer` · `None` |
| `MenuOpen` | `Double` · `Right` · `Both`; default `Both` |
| `MenuPlace` | `Beside` · `Center` |
| `MenuShortcut` | default `Alt+S` |
| `Appearance` | `System` · `Light` · `Dark` |
| `InnerStars` | bool; default `true` |

## 7. Tokens (from the UI study §5.1–5.6)

- **Light:**
  - bg `#F5F4F8` · surface/menu `#FFFFFF` · sunken `#ECEAF2` · hover `#F1EFF6` · pressed `#E8E5F0`
  - ink `#24212B` · text-2 `#5C5867` · text-3 `#6B6776` · boundary `#8F8A9B`
  - accent `#5B47C4` · on-accent `#FFFFFF` · accent-soft `#ECE7FF` · accent-ink `#4636A6`
  - moon `#7A5A1C` · leaf `#2F7A4A`
- **Dark:**
  - bg `#18161C` · surface `#221F27` · menu `#2A2630` · sunken `#1C1A20` · hover `#353140`
  - ink `#EFECF4` · text-2 `#B8B3C2` · text-3 `#A29DAD` · boundary `#7D7889`
  - accent `#B6A9FF` · on-accent `#1C1733` · accent-soft `#3A3354` · accent-ink `#D2C9FF`
  - moon `#F2C86A`
- **Type:** Rubik; title 24/600, section 16/600, row 15/500, body 15/400, description 13/400. Arabic is never below 13 px. Digits are tabular.
- **Radii:** card 22 · header 14 · tile 12 · group 16 · row 10 · segmented 12/9 · input 10 · switch full.
- **Elevation e1:**
  - light: `0 1px 2px rgba(0,0,0,.08), 0 8px 24px rgba(0,0,0,.12)`
  - dark: `0 1px 2px rgba(0,0,0,.45), 0 12px 32px rgba(0,0,0,.5)`
  - No borders with large blur and no coloured glows on UI surfaces. Sahar's own halo and glows are character art, not UI.
- **Motion:**
  - Segmented thumb 200 ms `cubic-bezier(.3,.7,.2,1)`; switch spring ζ≈0.9.
  - Page change: 140 ms crossfade.
  - No stagger.

## 8. Testing and verification

**Core unit tests:**
- The chat parser: every row of §5.1, Arabic-Indic and Latin digits, gendered replies.
- Old settings files load with the new fields at their defaults.
- Menu item filtering: hidden when unavailable or switched off.
- Shortcut conflict validation.
- The export-rig completeness test (§3 prerequisite).

**Windows UI CI:**
- The `menu` stage is updated for the card. Steps: open via double-click and the shortcut → the caret is in chat → type «مؤقت ٥» → the timer is running; Esc closes the card; drill into «بسكت»; check that hidden items are hidden.
- A new `settings` stage: launch the settings app → navigate all pages → change size → the engine size changes over the pipe → close → the process exits and memory is released.
- Screenshots are kept as CI artifacts.

**Design checks:**
- Run the impeccable detector on the settings web UI: 0 findings.
- Contrast is computed for every text token pair in both themes (≥ 4.5).
- The ui-verification checklist: focus rings, keyboard reach, RTL mirroring, reduced motion.

**Performance:**
- Engine RAM at rest < 60 MB, with the motion layer on.
- The menu opens in under 100 ms.
- Settings-app RAM is reported, and it is 0 after close.

## 9. Order of work

1. Fix `export_rig.py`.
2. Character changes L1–L4 and the motion layer.
3. The menu card and chat parser.
4. The settings app shell and bridge.
5. The settings pages.
6. CI stages and checks.
7. Test build 0.10.0, published after the owner's word.
