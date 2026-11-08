# Sahar · design study: menu + settings (2026-11-08)

Status: research and direction, for the owner to confirm. No product code changed.
Inputs: `PRODUCT.md`, spec v1 (§3, 4, 7, 9, 13), `assets/palette.json`, `assets/art/sahar-rig.json`, `Menu/MenuItems.cs`, `Settings/SaharSettings.cs`, rounds 1–2 boards (`sahar-design/project/*.dc.html`).
Method: I ran the impeccable detector on the old boards and on scratch test pages, and rolled `concept-seed` (key `a73657f8`). Contrast figures are computed WCAG 2.x ratios. §9 lists the decisions the owner still has to make.

---

## 1. Usage study

| # | Moment | Action | Open for | Next |
|---|---|---|---|---|
| S1 | A call starts in 2 min | right-click her → Hush → 1 h | ~2 s | call app |
| S2 | Screen share, or she covers a button | → Sleep (hide 1 h) | ~1 s | keep working |
| S3 | A break, "play with me" | double-click → Play → game | ~3 s | a few minutes of play |
| S4 | Focus block | → Timer → 25 | ~3 s | ring around her |
| S5 | "When's Asr?" | open the menu and **read the header** | ~1 s | Esc |
| S6 | "What did I copy?" | → Clipboard → pick | ~4 s | paste in the editor |
| S7 | Opened by accident | Esc or click outside | <1 s | focus back in the editor |
| S8 | Quran while working | → Listen → Quran → reciter/surah | ~6 s | she goes still |

**Settings visits:**
- First run: her name for you, how she addresses you, city for prayer.
- Week one: tuning (activity, sounds, quiet hours).
- After that, rarely: block an app, link Claude Code once, change city when travelling. A few times a month, 1–5 minutes each.

**Jobs to be done:** (1) quiet her now, for a while; (2) spend a moment with her; (3) glance at what's next (prayer, timer, Claude); (4) do a rare small tool job without leaving the work; (5) tune who she is once.

**Implications:**
- **The menu is glance-and-go.**
  - Open for 1–6 s, with every job reachable in ≤2 clicks.
  - The header answers S5 with zero clicks.
  - It closes instantly and **gives focus back** to the previous window.
  - Her own actions lead. The rarely used tools stay one click away but visually quiet.
- **Settings is a calm "tune my companion" surface.**
  - Every change shows on her right away, so there's no Save button.
  - Never a dashboard.

## 2. Why the previous rounds failed

**Round 1 (navy glass: menu A/B/C, settings, prayer, tokens). Concept and execution both failed.**
- It applied the *beautiful-ui-glass* DNA literally:
  - its rules: "dark mode is the default", "glass is the primary surface", "light is a material … glows";
  - what that produced: a `#120F33` navy ground, `blur(18–28px) saturate(170%)`, lavender halo rings (`0 0 0 6px rgba(185,176,255,.22)`), navy-tinted shadows, inset white highlights on 1 px white borders.
- The detector found **17× dark-glow** and **15× gpt-thin-border-wide-shadow**.
- Other faults:
  - dark-only, although the owner uses the laptop day and night;
  - IBM Plex Sans Arabic, which is on impeccable's training-default list;
  - emoji icons;
  - 9–11 equal-weight buttons for rarely used tools;
  - seven different radii.

**Round 2 (four sketches: agenda, LCD device, goods catalog, boxed manual). The concept failed first.**
- Each sketch dressed Sahar in an *object costume*, so the surface became about the metaphor. That breaks Principle 1, "she is the interface".
- The manual is a stock impeccable catalog world: `rw-manual-acetate-tab-board` came up again as a challenger in this study's roll. It was dealt, not derived.
- Execution faults on top:
  - costume devices that craft-floor bans (`3px 3px 0` offset shadows, inset block shadows, stamps, dotted paper);
  - saturated tab boards;
  - three Arabic faces (Readex Pro, Baloo Bhaijaan 2, Noto Naskh);
  - detector findings: low contrast (`#7D6F88` on `#FFF7F2` = 4.4:1), UI text under 11 px, cramped padding.

**Lesson:** no new costume and no more effects. Sahar supplies the character (her live figure, mood colors, moon). The UI supplies precision, the way well-made native utilities do.

## 3. Information architecture

### 3.1 Menu: a card beside her, replacing the 11-button ring

| Zone | Content | Why |
|---|---|---|
| Header (mood-tinted) | Her mood word + the **one most urgent live fact**: running timer › Claude waiting for permission › next prayer › Quran playing › hush remaining. Clicking it opens that item. | Covers S5; one event at a time, as in the brain's priority rule (spec §8.2) |
| 3 tiles: hers | **العبي** Play (4 games + random) · **اسكتي** Hush (½h/1h/2h/until I say) · **نامي** Sleep (hide 1 h / until I wake her). Stateful: when Hush is on, its tile becomes "رجّعي". | Jobs 1–2 |
| Tools row (quiet) | **مؤقت** Timer · **ملاحظة** Note · **استماع** Listen · **الحافظة** Clipboard | Job 4: rare but wanted |
| Footer | **الإعدادات** Settings · Esc hint | |

**Taken off the menu:**
- *Activity level* moves to Settings → Sahar (it's a lasting trait; Hush already covers "less, right now").
- *Prayer* becomes header information.
- *Claude* appears in the header only while active, with a button that focuses the terminal (spec §7.1).

**Drill-in:** a tile swaps the card's content in place, with a back arrow at the start edge. One level deep, never a second floating panel.

**Keyboard:**
- An optional global shortcut (proposed `Alt+S`) opens the menu with focus on the first tile.
- Arrows move logically (mirrored in RTL); `1`–`3` are the tiles; `Enter` activates; `Esc` goes back, then closes.
- On close, focus returns to the previous foreground window.

### 3.2 Settings: 8 pages in 3 groups (≤4 items per group)

The sidebar separates groups with space, not headings:

1. **سحورة** Sahar
2. **الأصوات** Sounds · **الصلاة** Prayer · **الاستماع** Listening
3. **عام** General · **الخصوصية** Privacy · **Claude Code**

**حول** About sits at the sidebar's foot.

| Page | Controls (model field) |
|---|---|
| Sahar | Live preview of her (the real rig art, in her current mood) · **How she talks to you**: name (`UserName`), addressing segmented شب/صبية/ما بدي حدد (`UserGender`) · **Size** segmented ×3 · **Activity** segmented ×3 + a one-line description that changes · **By herself**: switches for comments on your work (`AwarenessEnabled`), automatic mood, plays alone, reacts to weather (`WeatherEnabled` → reveals a city field) |
| General | Language segmented تلقائي/عربي/English · *new:* Appearance (with Windows/light/dark), Digits ٣/3, Menu shortcut · Start with Windows · Seasonal icon ×3 · Check for updates |
| Sounds | Level صامتة/خفيفة/كاملة · Volume slider (disabled when Silent) · Quiet hours switch + from/to times · Voice language ×3 |
| Prayer | Today card · City with search + "use Windows location" · Method select · Asr جمهور/حنفي · Reminder switch + minutes 5/10/15/20 · Adhan silence note |
| Listening | Reciter radio list with a 5 s preview · "Show the song playing" switch |
| Privacy | Knows which app you're in · Blocked apps (rows with remove, Add, an empty state that teaches) · Clipboard history + Clear · Internet use: weather/updates/Quran switches · **Erase all my data** (inline confirm) |
| Claude Code | Status · **Link** → preview the diff → confirm · Unlink |
| About | Version, Check now, licenses |

Everything applies instantly. Mobile later reuses these pages, with the sidebar collapsing to a pushed list.

## 4. Benchmarks and references

### 4.1 Benchmarks (from product knowledge, not fetched)

- **macOS Control Center and menu-bar popovers:** attached to the trigger, state visible at a glance, dismissed by a click outside.
- **Raycast:** keyboard-first rows (icon · label · keycap at the end), instant open, depth from surface steps rather than shadows.
- **Things 3:** calm settings, one accent, light and dark designed as equals.
- **Apple Weather:** the surface takes the current condition's tint. This is the model for our **mood-tinted header**.
- **Finch** (self-care pet app): the character previews every change and speaks the copy.

### 4.2 Owner-chosen sites

- **awesome-design-md** (I read the Apple, Raycast, Notion and Airbnb files from a clone of the repo):
  - *Apple:* one interactive color, one shadow, elevation from surface changes, blur only where functional, tight tracking on Latin display text only.
  - *Raycast:* "no drop-shadow elevation at all", a surface ladder instead, radii of 6–10, keycap hints.
  - *Airbnb:* a single tight dropdown shadow, `rgba(0,0,0,.02) 0 0 0 1px, rgba(0,0,0,.04) 0 2px 6px, rgba(0,0,0,.1) 0 4px 8px`, and one brand color.
  - *Notion:* pastel tints echo product colors (cf. our mood tints). Its navy hero is the look to avoid.
- **styles.refero.design:** 2,000+ extracted styles. The calm ones it features are mostly "warm paper/parchment" (Notion, Intercom, Cursor, Steep) or "frosted dusk" (Dimension). Those are impeccable's two named AI ruts, so avoid both.
- **component.gallery, popover page:**
  - short, non-critical content only;
  - closes on Esc, close button or click outside;
  - Esc returns focus to the trigger;
  - uses `aria-expanded`, `aria-haspopup`, `aria-controls`.
- **kinetics.colorion.co:**
  - Most of its 162 examples use overshoot (`cubic-bezier(0.34,1.56,0.64,1)`, "Switch Spring"). We reject that for routine controls, following impeccable's bounce/elastic check.
  - Kept: press 60 ms, tooltip 150 ms with a 400 ms delay, icon swap as a 300 ms blur crossfade.
- **21st.dev:** a marketplace of React components (docks, popovers, toggles, sliders). The dock pattern of round-1 menu C is a web trend, so avoid it.
- **whatships.com:** a directory of launch videos. Its companion-type entries (Crumb, Robyn, Boski) are titles only, with no UI to learn from.
- **impeccable.style/slop:** 67 checks; the relevant ones are in §7.
- **Not reachable** (fetch provenance): the component.gallery *toggle* and *segmented-control* pages, the getdesign.md pages (I used the repo clone instead), and the 21st.dev category pages.

### 4.3 ui-ux-pro-max

- Both `--design-system` queries fell back to **landing-page patterns**:
  - the queries: "desktop companion character app settings" and "virtual pet mascot utility preferences";
  - what came back: Hero+Features or Storytelling layouts, Swiss or Claymorphism styles, blue/orange palettes, Outfit/Work Sans or Varela Round/Nunito.
  - I **rejected** them as off-target.
- Verified hits I kept:
  - WinUI: Mica for long-lived windows and Acrylic for transient flyouts; support High Contrast; theme resources.
  - UX: WCAG 2.2 target ≥24 px with an 8 px gap; respect reduced motion; animate 1–2 elements per view; cancel or retarget in-flight transitions.
  - Font data: confirmed the Arabic + Latin subsets of Rubik, Beiruti, Alexandria, Zain and Vazirmatn.

## 5. Direction: "Porcelain"

**Thesis.** *Sahar's surfaces are made of the same matte porcelain she is: opaque, soft-edged, lit from above, the same object by day and by night. Only she carries color.*

**How it was chosen.** I ranked seven worlds grounded in her subject:
1. her body as material;
2. her moon's orbit;
3. an Arabic star almanac;
4. Damascene mother-of-pearl;
5. **matte bisque porcelain**;
6. a herbarium of her leaf;
7. a Levantine paper-cut picture book.

The roll assigned **#5**. All four catalog challengers were declined (each loses on audience fit). Each still lent one discipline:
- *raise (split-flap board):* times live in fixed cells with tabular digits, and state changes restyle a cell without reflowing it.
- *raise (labanotation):* mirroring is law. Specs use logical start/end only, and directional icons flip.
- *raise (mixtape j-card):* values sit tight against the row's end edge, labels at the start.
- *raise (acetate manual):* one size does the work. About 80% of text is body size, and hierarchy comes from weight and space.

Porcelain is a **material rule, not an illustration**: no drawn lamp or vase. It produces opaque, quiet surfaces, the opposite of glass-on-navy. In light mode its cool white-lavender matches her Calm body (`#FFFFFF → #F1ECFF`). At night it becomes a low-chroma graphite-plum glaze, **not navy**.

### 5.1 Color (Restrained: neutrals, one accent, her mood tints one region)

| Token | Light | Dark | Use | Contrast L / D |
|---|---|---|---|---|
| `bg` | `#F5F4F8` | `#18161C` | settings ground | |
| `surface` | `#FFFFFF` | `#221F27` | settings groups | |
| `menu` | `#FFFFFF` | `#2A2630` | menu card | |
| `sunken` | `#ECEAF2` | `#1C1A20` | segmented/slider track, inputs | |
| `hover` / `pressed` | `#F1EFF6` / `#E8E5F0` | `#353140` / `#3D3848` | states | |
| `ink` | `#24212B` | `#EFECF4` | text | 15.8 / 12.7 (on menu) |
| `text-2` | `#5C5867` | `#B8B3C2` | descriptions | 6.9 / 7.2 |
| `text-3` | `#6B6776` | `#A29DAD` | hints, placeholders | 5.5 / 5.6 |
| `boundary` | `#8F8A9B` | `#7D7889` | outlines for the off switch and inputs | 3.35 / 3.47 (non-text) |
| `accent` | `#5B47C4` | `#B6A9FF` | selection, primary, focus | 6.7 / 7.1 |
| `on-accent` | `#FFFFFF` | `#1C1733` | text on accent | 6.7 / 8.3 |
| `accent-soft` / `accent-ink` | `#ECE7FF` / `#4636A6` | `#3A3354` / `#D2C9FF` | selected row or segment | 7.5 / 7.6 |
| `moon` | `#7A5A1C` | `#F2C86A` | times, next event (from moon `#F2C86A`) | 6.4 / 9.3 |
| `leaf` | `#2F7A4A` | `#8ED3A0` | success, "linked" (from leaf `#6FB683`) | 5.3 / 8.4 |
| `error` / `warn` | `#C2363B` / `#9A5418` | `#FF8A8D` / `#F2A06E` | from palette Error/Alert | ≥5.4 / ≥6.5 |

**Notes:**
- The accent is her Calm bud and glow (`#C9B8FF`/`#B9B0FF`), darkened for light mode.
- **Mood header tint:**
  - light: `palette[mood].mid` (Sleepy mixed 50% with white → `#E4E1F3`);
  - dark: 16% `palette[mood].bottom` over `menu`;
  - header text is always `ink`, computed at ≥12.4 (light) and ≥8.3 (dark) across ten moods; `text-2` would fail Sleepy-light at 4.1.
- In Windows High Contrast, drop the tints and use system colors.

### 5.2 Typography

**Face: Rubik** (Google Fonts, OFL, variable 300–900, Arabic + Latin in one family).
- Its Arabic is by Omaima Dajani (Levantine), and its slightly rounded corners echo her dome.
- One family ends the three-face fight of round 2.
- It isn't on impeccable's default list, and the detector passes it.
- Self-host it for settings; load it as a DirectWrite custom collection for the menu.
- Alternate: Beiruti. Avoid IBM Plex, Readex Pro, Baloo Bhaijaan 2 and Inter (the detector flags Inter as overused).

**Scale:**

| Role | Size / weight | Line height (Latin / Arabic) |
|---|---|---|
| Title | 24/600 | 1.3 |
| Section | 16/600 | 1.4 |
| Row label | 15/500 | 1.45 / 1.6 |
| Body | 15/400 | 1.45 / 1.6 |
| Description | 13/400 | 1.5 / 1.65 |
| Menu label | 14/500 | — |
| Keycap (Latin only) | 12 | — |

**Rules:**
- Arabic is never below 13 px.
- Tracking is 0, and Arabic is never letter-spaced.
- Titles use `text-wrap: balance`.
- Times, countdowns and volume use `tabular-nums`. **Verify that Rubik has `tnum` and Arabic-Indic digits**; otherwise use fixed-width digit cells.

### 5.3 Space, radii, elevation, material

**Spacing** (4-based): 2 · 4 · 8 · 12 · 16 · 20 · 24 · 32 · 40 · 56.
- Between groups 32, inside a group 8–12.
- A section title gets 24 above it and 8 below.
- Separate with spacing first, a surface change second, and a divider only in lists longer than 6 rows.

**Radii** (concentric: outer = inner + padding):

| Element | Radius |
|---|---|
| Menu card | 20 |
| Tiles in the card | 12, inset 8 |
| Settings group | 16 |
| Row highlight in a group | 10, inset 6 |
| Segmented control | track 12, thumb 9, inset 3 |
| Buttons, inputs | 10 |
| Switches, slider thumb | full |
| Focus ring | element radius + 2 |

Nothing else.

**Elevation:**

| Level | Applies to | Treatment |
|---|---|---|
| e0 | settings content | flat; the `bg`→`surface` step separates it |
| e1 | menu card, dropdowns | light: `0 1px 2px rgba(0,0,0,.08), 0 8px 24px rgba(0,0,0,.12)` |
| | | dark: `0 1px 2px rgba(0,0,0,.45), 0 12px 32px rgba(0,0,0,.5)` |
| e2 | settings window | the system window shadow |

- The e1 shadows are neutral black with **no border**. Both recipes passed the detector on a test page.
- **Never** pair a visible 1 px border with a blur over 12 px. The detector fires at ≥30 px and stays quiet at ≤24 px, so 12 keeps a margin.
- No colored shadows, inset highlights or zero-offset halos.
- Composition uses the same values through `DropShadow`. In dark mode the shadow does most of the separating, because `menu` is only 1.1–1.2:1 against dark apps.

**Material: no translucency in v1.**
- The menu is opaque. Acrylic over arbitrary windows can't guarantee contrast, and it was round 1's signature.
- The settings window is opaque, with its caption colored to `bg` through `DWMWA_CAPTION_COLOR` (and `DWMWA_USE_IMMERSIVE_DARK_MODE` in dark). That keeps the native caption buttons without the cold Windows look.
- Mica can be revisited for the sidebar only if the owner asks, with a tint of ≥85% opacity and contrast re-measured.

### 5.4 Iconography

- **Phosphor Regular** (MIT), one weight, rounded joins: 20 px in tiles, 18 px in the tools row and nav, 16 px inline.
- Custom glyphs in the same stroke: moon, leaf, bud, and hush (the bud closed). Never emoji.
- The engine draws path data with Direct2D (`ID2D1PathGeometry` / `ID2D1SvgDocument`).
- Directional icons flip in RTL. Icons beside visible text are hidden from assistive tech.

### 5.5 Motion

| Interaction | Spec | Reduced motion |
|---|---|---|
| Menu open | 180 ms; opacity 0→1, scale 0.96→1 from the corner nearest her; `cubic-bezier(0.2,0.9,0.3,1)`; no overshoot | opacity only, 100 ms |
| Menu close | 120 ms; opacity, scale →0.98; `cubic-bezier(0.4,0,1,1)` | instant |
| **Moon clasp** (the one authored moment) | On open, her moon leaves its orbit on a short arc (240 ms, expo-out) to rest on the card's corner nearest her, and her eyes turn to the card. It returns in 300 ms. | appears in place |
| Drill-in | 160 ms crossfade, content moves 8 px toward the logical end | crossfade |
| Hover / press | 120 ms color / scale 0.97 in 60 ms, release 160 ms | color only |
| Segmented thumb | 200 ms, `cubic-bezier(0.3,0.7,0.2,1)` | jump |
| Switch thumb | spring k=500, c=40, m=1 (ζ≈0.9, about 180 ms, no overshoot) | jump |
| Slider | thumb tracks the pointer 1:1 | — |
| Settings page change | 140 ms crossfade, no slide | instant |
| Mood tint | 250 ms (spec §5), interpolated in OKLab | instant |

- No stagger and no entrance choreography.
- Transitions retarget from their current value. Never `transition: all`.
- Reduced motion: Windows "Animation effects" off (`SPI_GETCLIENTAREAANIMATION`), or `prefers-reduced-motion`.

### 5.6 Components (states apply in both themes)

**Menu card**
- `menu` surface, e1, r20, padding 8, 288 DIP wide.
- Placed 12 DIP from her body on the roomier side, top-aligned with her head, clamped to the work area.
- Header: 44 tall, r12, mood tint, value end-aligned in `moon`.
- Takes keyboard focus while open.

**Tile** (r12, 76×84)
- Hover: `hover`. Pressed: `pressed` + the press scale.
- Active (e.g. Hush on): `accent-soft` with an `accent-ink` label.
- Focus: 2 px `accent` ring, offset 2.
- Disabled: `text-3`, icon at 40%, reason in the tooltip.

**Tools row:** 4 items of 64×56, 18 px icon, 13 px `text-2` label, gap 8.

**Segmented control**
- Track: `sunken`, r12, padding 3, height 36, ≤4 options.
- Thumb: r9; `surface` with `0 1px 2px rgba(0,0,0,.10)` in light, `hover` with no shadow in dark.
- Selected label `ink` 500, others `text-2`.
- `radiogroup` semantics, arrow keys, applies instantly.

**Switch**
- Track 40×24. Off: `sunken` + a 1 px `boundary` outline. On: `accent`.
- Thumb 18 px. The whole row is the hit area.

**Slider:** 4 px `sunken` track with an `accent` fill, 18 px thumb, value at the end edge, 5% keyboard steps.

**List row**
- Min 52 tall, padding 12/14.
- Label and description at the start, control or value at the end.
- Hover: `hover`, r10, inset 6.

**Section header:** 16/600 `ink`. No eyebrow or kicker.

**Settings nav**
- 220 DIP on the start side, items 36 tall, r10.
- Selected: `accent-soft` + `accent-ink`, no side stripe.
- Groups separated by 20.

**Inputs**
- `sunken` fill, 1 px `boundary` outline, r10, height 40.
- Focus: `accent` 2 px ring.
- Error: `error` outline + a message that names the fix.

**Empty state** teaches. Blocked apps example: "ما في برامج ممنوعة. أي برنامج بتضيفه، سحورة ما بتسجّل وقته." plus an Add button.

**Loading:** an inline skeleton row (city search), never a centered spinner.

## 6. Wireframes (RTL; the right side is the start)

**Menu beside her** (she sits near the right edge; the card opens toward the middle of the screen):

```
 screen middle ←                                            → screen edge
  ┌────────────────────────────────────────┐(◐)  ← her moon clasps the corner
  │ ▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒ mood tint ▒▒▒▒▒▒▒▒▒▒▒ │          .-'''-.
  │    العصر بعد ٤٢ د   ·   سحورة هادية    │         /  ˘ ˘  \
  ├────────────────────────────────────────┤        |   ‿    |   ← Sahar, eyes on the card
  │ ┌──────────┐ ┌──────────┐ ┌──────────┐ │         \__/\/\__/
  │ │  [moon]  │ │  [bud]   │ │  [ball]  │ │
  │ │   نامي   │ │  اسكتي   │ │  العبي   │ │   tiles read right→left:
  │ └──────────┘ └──────────┘ └──────────┘ │   العبي · اسكتي · نامي
  │                                        │
  │  [clip]     [ear]     [pen]   [clock]  │   tools (quiet, text-2)
  │  الحافظة    استماع    ملاحظة    مؤقت     │
  │                                        │
  │  Esc للإغلاق                 الإعدادات ‹ │   footer
  └────────────────────────────────────────┘
```

**Drill-in (Hush):** the header stays and the content swaps.

```
  │  اسكتي                              →  │   → = back (points to start in RTL)
  │  [ لحد ما رجّعك | ساعتين | ساعة | نص ساعة ]│   segmented, reads right→left
  │  بتسكت وبتوقف تعليقاتها. التنبيهات المهمة   │
  │  (الصلاة، المؤقت) بتضل.                     │
```

**Settings → Sahar** (880×600 default, min 720×520):

```
┌──────────────────────────────────────────────────────────────────────┐
│                                                     سَحَر   — ▢ ✕   │  caption colored = bg
├────────────────────────────────────────────────┬─────────────────────┤
│  سحورة                                          │  ▓ سحورة ▓          │  selected: accent-soft
│  شخصيتها، وقديش بتتحرك، وكيف بتحكي معك.          │                     │
│ ┌────────────────────────────────────────────┐ │    الأصوات           │
│ │   (live Sahar, current mood, real rig art) │ │    الصلاة            │
│ │   «هيك بيّن هلق عالشاشة.»                    │ │    الاستماع          │
│ └────────────────────────────────────────────┘ │                     │
│  كيف بتحكي معك                                   │    عام               │
│ ┌────────────────────────────────────────────┐ │    الخصوصية          │
│ │ شو بدها تناديك؟            [ عمّار        ] │ │    Claude Code       │
│ │ بتحكي مع        [ ما بدي حدد | صبية | شب ]   │ │                     │
│ └────────────────────────────────────────────┘ │                     │
│  حجمها وحركتها                                  │                     │
│ ┌────────────────────────────────────────────┐ │                     │
│ │ حجمها              [ كبيرة | ▓وسط▓ | صغيرة ] │ │                     │
│ │ نشاطها           [ نشيطة | ▓متوسطة▓ | هادية ] │ │                     │
│ │ بتتحرك كل شوي، وبتهدى وقت بتكتب.   (text-2)  │ │                     │
│ └────────────────────────────────────────────┘ │                     │
│  شو بتعمل لحالها                                 │                     │
│ ┌────────────────────────────────────────────┐ │                     │
│ │ بتعلّق على شغلك                     (●──)  │ │                     │
│ │ مزاجها بيتغيّر لحاله                 (●──)  │ │                     │
│ │ بتلعب لحالها                         (──○)  │ │                     │
│ │ بتحس بالطقس                          (──○)  │ │    حول · 0.9.0       │
│ └────────────────────────────────────────────┘ │                     │
└────────────────────────────────────────────────┴─────────────────────┘
```

**Settings → Prayer:**

```
│  الصلاة                                                              │
│  الأوقات بتنحسب عالجهاز، بلا إنترنت.                                    │
│ ┌──────────────────────────────────────────────────────────────────┐ │
│ │ الصلاة الجاية                                   العصر  · بعد ٤٢:١٠ │ │  value end-aligned, moon
│ │ ┌────────┬────────┬────────┬────────┬────────┐                     │ │
│ │ │ الفجر  │ الضهر  │▓العصر▓ │ المغرب │ العشا  │  fixed cells,        │ │
│ │ │  ٤:٥٢  │ ١١:٤٨  │▓ ٢:٤١▓ │  ٥:٠٣  │  ٦:٢١  │  tabular digits      │ │
│ │ └────────┴────────┴────────┴────────┴────────┘                     │ │
│ └──────────────────────────────────────────────────────────────────┘ │
│  المكان والطريقة                                                      │
│ ┌──────────────────────────────────────────────────────────────────┐ │
│ │ المدينة                                   [ حلب، سوريا        ⌄ ] │ │
│ │ استعمل موقع الويندوز                                        (──○) │ │
│ │ طريقة الحساب                          [ أم القرى              ⌄ ] │ │
│ │ العصر                                     [ حنفي | ▓الجمهور▓ ]   │ │
│ └──────────────────────────────────────────────────────────────────┘ │
│  التذكير                                                              │
│ ┌──────────────────────────────────────────────────────────────────┐ │
│ │ ذكّرني قبل الصلاة                                           (●──) │ │
│ │ قبل قديش                          [ ٢٠ | ١٥ | ▓١٠▓ | ٥ ] دقيقة    │ │
│ │ وقت الأذان بتسكت وبتوقف حركاتها.                         (text-2) │ │
│ └──────────────────────────────────────────────────────────────────┘ │
```

**Light vs dark**

| | Light | Dark |
|---|---|---|
| Ground / surface | porcelain `#F5F4F8` / `#FFFFFF` | glaze `#18161C` / `#221F27` (menu `#2A2630`) |
| Separation | surface step + e1 shadow at 0.12 | lighter surface + e1 shadow at 0.5 (does most of the work) |
| Mood header | `mood.mid` | 16% `mood.bottom` over the menu |
| Accent | `#5B47C4` fill, white text | `#B6A9FF` fill, `#1C1733` text |
| Segmented thumb | white + a 1 px soft shadow | `hover` tone, no shadow |
| Same in both | layout, radii, type, motion, icon stroke | |

## 7. Anti-patterns and the verification checklist

**Refuse: lessons from our two rejected rounds**
- navy grounds;
- acrylic or blur;
- any colored glow or halo;
- inset highlights;
- a border combined with a wide shadow;
- emoji icons;
- rings with more than 6 buttons;
- dock magnification;
- object costumes (notebook, device, catalog, manual);
- stamps, dotted paper, offset block shadows;
- saturated section boards;
- more than one typeface;
- cream grounds;
- a dark-only design.

**Refuse: from the skills**
- *impeccable:*
  - eyebrow labels;
  - nested cards;
  - icon-tile grids;
  - side stripes;
  - gradient text;
  - decorative glass;
  - bounce easing;
  - pulsing dots;
  - gray text on tinted fields;
  - tiny text;
  - monotonous spacing;
  - clipped popovers;
  - a reflexive modal;
  - rough mascot art (use the real rig).
- *make-interfaces-feel-better:* `transition: all`, or hit areas under 40×40.
- *beautiful-ui:* card piles, or a happy path only.

**Verify: the build must pass**
1. **Detector:** `sh <impeccable>/scripts/impeccable detect --json <settings html>` exits 0 in **both** themes. It must report no `dark-glow`, `gpt-thin-border-wide-shadow`, `low-contrast`, `undersized-ui-text`, `cramped-padding` or `clipped-overflow-container`. The native menu has no DOM, so scan a static HTML twin built from the same tokens.
2. **Contrast,** measured on rendered pixels in both themes: text ≥4.5 (≥3 at 18 px+); icons, outlines and focus ring ≥3; the header on all 16 palette moods.
3. **States:** default, hover, focus-visible, pressed, selected, disabled, loading, error and empty for every control. Pressing never shifts layout.
4. **Keyboard:** the shortcut opens the menu; arrows work in RTL and LTR; Esc goes back, then closes; focus returns to the previous window; settings tab order matches the visual order; no traps.
5. **Semantics:**
   - a `nav` landmark;
   - `aria-current` on the current page;
   - segmented controls as `radiogroup`s;
   - `role="switch"` with `aria-checked`;
   - select popovers with `aria-expanded`/`aria-controls`;
   - menu state exposed through the test pipe (spec §11).
6. **RTL/LTR:** a pure mirror; directional icons flip; digits follow General; Arabic ≥13 px and never letter-spaced.
7. **DPI and OS:**
   - 100% and 150%; Windows 10 and 11;
   - the menu stays inside the work area at every corner and on a second monitor;
   - High Contrast uses system colors.
8. **Motion:** matches §5.5; no overshoot; transitions interrupt cleanly; the reduced-motion column is honored.
9. **Hit areas** ≥40×40 with gaps ≥8 px.
10. **Craft:**
    - concentric radii checked numerically;
    - one Phosphor weight;
    - tabular digits;
    - text selection, caret, focus ring and scrollbar themed in WebView2.
11. **Budget:** engine <60 MB at rest with the font loaded; the settings app exits fully.
12. **Owner check:** a light/dark screenshot pair (menu + 2 settings pages) with real copy, before any polish.

## 8. Risks

- Whether Rubik has `tnum` and Arabic-Indic digits.
- The live preview needs the rig's SVG paths inside the web page.
- A dark menu over dark apps depends on the shadow, so test it over VS Code, Windows Terminal and dark Chrome.

## 9. Owner decisions

1. Replace the ring with a **card beside her**?
2. **3 of hers + 4 tools**, or the tools behind a single "أدوات" (tools) tile?
3. **Rubik**, or Beiruti?
4. Add Appearance, Digits and Menu shortcut to General?
5. Keep the moon clasp, or use a plain open?
