# Sahar · character design study (2026-11-08)

Status: research and direction, for the owner to confirm. No engine code changed.

**Inputs**
- `PRODUCT.md`
- spec v1 §4–6
- `assets/palette.json` (16 moods), `assets/expressions.json` (29 expressions + idle), `assets/art/sahar-rig.json`
- the rig source `tools/art/design.py` and `export_rig.py`
- `Render/` (`SaharVisual`, `PartRenderer`, `IdleAnimations`, `ExpressionPlayer`, `OrbRenderer`, `Extras`)
- the squash code in `Motion/`

**Method**
- Numbers are computed from the real geometry and palette: pixel sizes via `RigScale`; WCAG contrast sampled along the silhouette through the actual radial gradient; mood distances in OKLab.
- Sketches use the rig's own paths and were rendered (rsvg) to check them.
- The impeccable detector returned only its advisory `shape-assembled-illustration`, equally for today and every variant, so it doesn't separate the options.
- I couldn't put impeccable's probe question to the owner; §6 holds those decisions.

| File | What it shows |
|---|---|
| `00-overview.svg` | all four side by side |
| `01-current.svg` | today, Calm, Medium; left half white, right half `#202020` |
| `02-variant-a-polish.svg` | A · small polish |
| `03-variant-b-night-inside.svg` | **B · night inside (recommended)** |
| `04-variant-c-bolder.svg` | C · bolder refresh |
| `05-contact-sheet-48px.svg` | 10 main expressions at true Small size, today vs proposed, on white and dark. **View at 100%.** |

---

## 1. Audit of today's Sahar

### 1.1 Silhouette (fill-black test)

Filled black, she reads as **ghost dome + four-lobe hem + two-leaf sprout**, with a moon dot drifting beside her. The sprout is what makes the silhouette hers. The dome-with-wavy-hem on its own is the stock cartoon ghost; Pac-Man's ghosts and Nintendo's Boo own that outline.

Two things weaken it:
- **Her footprint is 1.9× her body** (a 150 DIP square at Medium). Halo, dashed orbit and five outer stars fill it, so she reads as a *cloud of things* more than a creature.
- **Her own edge often isn't there** (§1.5).

Resemblance: a stem with a bud points toward Nintendo's Pikmin. The two-leaf pair keeps her clear, so never reduce her to one leaf with a round bud. Nothing points toward Sanrio, Pokémon or Disney.

### 1.2 Shape language

- **Dome:** circle family, so soft, safe, calm. Correct.
- **Hem:** fluid, floating. Correct, but symmetric, so it reads as "ghost costume" more than "in motion".
- **Leaves and bud:** growth, life, and the mood signal (her "Bao" cue).
- **Moon:** companionship, night.
- **Four-point sparkle stars:** these are the one wrong shape. The four-point sparkle is today's universal "AI" glyph, so on her it says *AI feature* rather than *night sky*. That conflicts directly with the owner's "nothing AI-looking".

Everything is a circle or a curve. That serves "calm, soft". Nothing yet carries "a little mysterious" or "slightly serious" except the Mystic mood.

### 1.3 Proportion, face placement, eye spacing

**Body:** 160 × 150 units, about 1:1.

- **Eyes:** line at 43% of body height, mouth at 54%; eyes 18 × 22 units, 40 apart, inner gap ≈ 1.2 eye widths. The face cluster is 36% of body width: compact and appealing. Keep it.
- **Blush is detached:** at x 78/162, almost at the body edge. At Small it reads as stains at her sides, not cheeks.
- **Two highlights per eye** is maximum baby-schema: cute, the opposite of "slightly serious".
- **Light contradicts itself:** the body is lit from the upper left, the eye highlights sit upper right.

### 1.4 Expression legibility: 48 vs 120 px

| Feature | Units | at 48 px | at 80 | at 120 |
|---|---|---|---|---|
| eye (open) | 18×22 | 5.4×6.6 px | 9×11 | 13.5×16.5 |
| mouth stroke / brow stroke | 2.6 | **0.78 px** | 1.3 | 1.95 |
| smile width / depth | 12 / 2.25 | **3.6 / 0.67 px** | 6 / 1.1 | 9 / 1.7 |
| lower eye highlight | Ø 3.2 | 0.96 px | 1.6 | 2.4 |
| pleats / leaf vein | 1.8 / 1.2 | 0.54 / 0.36 px | 0.9 / 0.6 | 1.35 / 0.9 |
| moon face stroke | 1 | 0.3 px | 0.5 | 0.75 |
| dizzy spiral | Ø 16, stroke 2.6 | 4.8 px blob | 8 | 12 |
| orb / its emoji | Ø 48 / 22 | **14 / 6.6 px** | 24 / 11 | 36 / 16.5 |

At **48 px** (contact sheet, rows 1–2) every brow disappears, so **error (Worried) and Focus ≈ Open**; Smile, Flat and Sleepy mouths become one sub-pixel smudge; Dizzy and Mystic eyes become rings; Sick and Angry differ only by tint. And only **19 face combinations serve 30 presets** (five share Open + Smile; permission and timer-done share Open + O), so meaning falls to a 14 px orb with a 6.6 px emoji. At 120 everything reads; at 80, all but the orb emoji.

### 1.5 Colour per mood, on light and dark desktops

**Contrast of her silhouette edge** (minimum / median along the outline):

| Moods | vs white | vs `#202020` (Windows dark, VS Code) | vs blue wallpaper `#2F5BD3` |
|---|---|---|---|
| 12 light moods (Calm, Happy, Focus, Excited, Hot, Alert, Error, Online, Cold, Love, Sick, Angry) | **1.00–1.09** / 1.1–1.7 | 8.0–12.5 / 9.8–13.5 | 2.9–4.4 / 3.6–4.8 |
| Sleepy, Dim | 1.25–1.40 / 1.9–2.4 | 5.2–7.2 | 1.9–2.6 |
| Night | 1.54 / 3.5 | 3.3 / 4.7 | **1.20** |
| Mystic | 2.9 / 7.4 | **1.6** / 2.2 | **1.01** |

- **By day the top of her head disappears.** The dome samples the gradient at t ≈ 0.3, which is nearly the `top` colour, and 12 moods use `top` = `#FFFFFF`. Over a white Chrome or Word window, her dome is invisible.
- **By night Mystic disappears** into dark apps.
- ui-ux-pro-max's *Icon Contrast* rule asks meaningful graphics for ≥ 3:1 against adjacent colours, and its dark-mode rule says to test dark separately. Today she passes on dark (apart from Mystic) and fails on light.

**Several moods can't be told apart by colour:**
- **Happy vs Alert: ΔE 1.8** (OKLab ×100). The "Claude needs permission" mood looks the same as "happy".
- Calm vs Focus: 4.0. Error vs Love: 4.6.
- The Calm and Mystic buds are identical (ΔE 0.9).
- Bottom chroma is 5–15 for every mood, all pastel. Colour can set a mood but can't carry a message.

On white, the stars (`#FFE7A8`) measure 1.22, the moon edge 1.59 and the light leaf 1.3. Eye ink on Mystic `mid` only just passes at 3.25.

### 1.6 Secondary shapes: character or clutter?

| Part | Verdict |
|---|---|
| Leaf + bud | **Character.** Best silhouette signature; carries mood. Keep. |
| Moon | **Character** (the pet). Keep. Its glow and 0.3 px face don't add anything. |
| Dashed orbit line | **Clutter.** Draws a path the motion already implies. |
| 5 outer stars | **Clutter, wrong shape** (AI sparkle). |
| Halo (breathing r120 glow) | **The AI look.** craft-floor: "a zero-offset colored halo is decoration". On dark it's the violet haze of rejected round 1. |
| Bud, moon and orb glows, orb ring | Break Porcelain's "no colored glow or halo". |
| Glossy highlight (7 u, 75%) | Reads plastic. Porcelain is matte: a soft sheen at most. |
| Ground shadow `#1E2350` | Navy-tinted; Porcelain wants neutral. |

### 1.7 Edge on busy wallpapers, and consistency with Porcelain

- She has **no outline**, so on photos and gradients her edge depends entirely on luck.
- Porcelain's thesis is that **only she carries colour, matte, no glow**. Today she is the one glowing, glassy object on a matte system.
- She also still uses **emoji** in the orb (22 of 26 orb glyphs). Both craft-floor and the approved UI study ban emoji for icons.

## 2. What stays, and what to improve

**Identity: must stay**
- the white-lavender dome and hem (floats like a ghost);
- the two-leaf sprout with the mood bud;
- the pet moon that orbits;
- the compact low-set face with dark ink eyes and a small mouth;
- blush;
- mood tinting;
- the Bao squish;
- the slow bob.

**Improvements, ranked by impact** (cost is in engine terms)

| # | Sev. | Change | Why | Cost |
|---|---|---|---|---|
| 1 | P1 | **Contour:** stroke the body path with a per-mood `line` colour at a constant 1.5 DIP (width ÷ `Scale`). The leaf gets a darker green line. | Fixes §1.5. All 16 proposed line colours measure **4.20:1 on white and 3.88:1 on `#202020`**: a mid-tone between 0.15 and 0.30 luminance clears 3:1 on both. A drawn line is also the clearest "illustrated, not AI-rendered" signal. | palette slot `line` + ~10 lines in `PartRenderer` |
| 2 | P1 | **Remove every glow:** halo, bud glow, moon glow, orb glow and ring, dashed orbit. | Owner's #1 dislike; craft-floor; Porcelain. Shrinks her visual footprint by about half. | **delete** 3 layers + 3 idle loops (less GPU memory) |
| 3 | P1 | **Small-size level of detail (LOD)** (§4): a 48 px face set, a stroke floor, a minimum orb size, and **drawn orb marks instead of emoji**. | Makes 48 px usable; ends the emoji ban violation. | stroke floor: 1 line. LOD eyes/mouths: new shapes in `design.py` + an LOD map in the rig. ~22 orb glyph paths (Phosphor + the custom moon/leaf/bud set). |
| 4 | P2 | **Alert to amber** (`bottom #F7B04A`, bud/glow `#E8932A`): ΔE ≥ 7.5 from every warm mood. Rule: **no event is told by colour alone.** | Permission requests must not look like "happy". | palette tweak |
| 5 | P2 | **Night inside her:** move the stars *into* the lower body, as a mood-tinted dusk with 3 tiny rounded stars. Retire the outer star ring. | Fulfils the brief she was picked for ("night sky inside her"), which today's Calm body doesn't show. Gives mood a home. Removes outer clutter. | 1 body-clipped layer, palette slot `sky`; delete 5 star sprites + 6 loops |
| 6 | P2 | **Motion consolidation** (§4.2) | 10 independent loops today | keyframe edits |
| 7 | P3 | One eye highlight, moved upper-left; matte sheen (12 u, 38%) instead of gloss; blush moved in to x 88/152; neutral shadow `#24212B` 14%; pleats and vein only at Large. | Coherent light, calmer, slightly more serious. | parameter edits in `design.py` → re-export |

> **Before any of this: fix the exporter.** `export_rig.py` regenerates `palette.json` and `expressions.json` from `design.py`. But `design.py` lacks Sick and Angry, and its preset table stops at #25. Re-running the exporter today would **silently delete** the Sick and Angry moods and expressions 26–29. Move those into `design.py` first.

## 3. Variants

All four sketches share one scale and a split white/dark ground, so you can judge footprint and edge directly.

### A · Small polish (items 1–4, 7)

Same paths. Glows and orbit line gone, mood contour, three small rounded stars in moon-gold, matte moon with a readable face, neutral shadow, one eye highlight. She holds her edge on white and dark, the footprint halves, and she reads as illustrated, not rendered. Still missing: "night sky inside"; the outer stars stay meaningless decoration. **About 2 days.**

### B · Night inside (A + item 5): recommended

The stars move inside her: below the mouth her body deepens into a **mood-tinted dusk** (`sky`: Calm `#A795E0`, Mystic `#2E2468`…) with three tiny cream stars. Nothing orbits but the moon.

*Her shell is porcelain; her mood is the weather inside her.* The dome stays constant (identity); the dusk carries the mood, legible as a region rather than a tint on a pastel edge, echoing Porcelain's mood-tinted header. The face stays on the light part, so its contrast is unchanged.

**Why this one:** the only option that adds *more of who she is* while removing parts; every identity element and every expression keeps working. **A + 1 layer, about 3 days.**

### C · Bolder refresh (B + new silhouette)

- **Hem:** an asymmetric **trailing wisp** instead of four even lobes: she looks in motion, and leaves the generic ghost outline.
- **Leaves:** 1.28× larger, so the sprout carries the silhouette at 48 px.
- **Eyes:** calm **half-lidded**, 6 units lower: "slightly serious, a little mysterious" directly.
- **Moon:** a **crescent (hilal)**, recognisable at 7 px where a disc becomes a dot; fitting for an Arabic-first companion that tracks prayer times.

Cost: new `BODY`/`BODY2` (equal command counts), leaf and moon paths, and re-checking all 29 expressions; **1–1.5 weeks**. Risks: the lidded default can read "unimpressed"; the wisp must mirror with travel direction. C's ideas can also be adopted one at a time after B.

## 4. Expressions and motion

### 4.1 Small-size rules (LOD)

Small (48) and the taskbar mini use the small face set; Medium (80) the full set minus pleats and vein; Large (120) everything. Precedent: the `design` skill's icon guide (*"test at 16px and 48px for clarity"*) and the `brand` skill's logo rules (a minimum size per lockup, with a simplified icon-only variant below it).

**Rules for the small set** (contact sheet rows 3–4):
1. **Stroke floor:** 1.5 px device width for every face and leaf stroke (≥ 5 units at Small).
2. **Eyes 1.15×**, one highlight ≥ 2 px, on the upper left.
3. **No brows.** Brows become **eyelid cuts** that change the eye's shape:

   | Mood | Eyelid cut |
   |---|---|
   | Focus | flat lid |
   | Worried | lid slopes down to the outside |
   | Angry | lid slopes down to the inside |
   | Mystic | half lid with a pale pupil |

   They survive downscaling because they change a filled shape, not a hairline.
4. **Dizzy** becomes a thick ring with a dot; **Sick** gets its own **squeeze `> <`** eyes (recommended at every size).
5. **Mouths** ≥ 18 units wide and 5 thick. **Drop** pleats, vein, inside stars and moon craters.
6. **Orb:** never under **20 DIP**; matte, no glow or ring; drawn marks (✓ ✕ ••• and the **mirrored Arabic ؟**) from the menu's stroke set instead of emoji.

Result at 48 px: all ten sample expressions are distinct (done, working, permission, error, sleep, late night, dizzy, sick, angry, idle).

### 4.2 Idle motion

Today, at rest, each runs on its own clock: bob 3.6 s, hem crossfade 2.2 s, blink 6 s, leaf 5 s, halo 4 s, bud glow 2.4 s, star ring 40 s, five twinkles 2.2–3.4 s, moon 9 s.

The skills: ui-ux-pro-max, *"Animate 1–2 key elements per view"* and *"infinite animations are distracting … don't use for decorative elements"*; motion-foundations, motion must *guide attention, communicate state or preserve continuity*, or be removed; craft-floor, *"one authored moment, not scattered effects"*.

**Proposal: four rhythms.**
1. **Breath family, one clock.** Bob at ~3.8 s. The hem, leaf and shadow are driven by the bob's phase, not by their own timers:
   - the hem trails the rise by ~15% (follow-through);
   - the leaf lags ~25% at ±4° (overlapping action);
   - the shadow shrinks in sync.
2. **Blink.** An irregular pattern inside one ~23 s keyframe loop, with blinks at 18%, 41%, 47% (a double blink) and 80%. Still zero CPU, no longer metronomic.
3. **Moon orbit.** ~14 s, with occasional rests beside her.
4. **Inside stars.** A rare single glint, not a constant twinkle.

**Hem.** Crossfading two opaque hems leaves a semi-transparent double edge at mid-fade. motion-advanced: path morphs need equal command counts, which `BODY`/`BODY2` already have. Composition can animate a path on a shape visual (Windows 10 1809+). Moving the body to a shape visual would give a true hem morph; verify the API before choosing it.

### 4.3 Squash and stretch

The values already conserve area:

| Motion | Scale | Area |
|---|---|---|
| poke squash | 1.14 × 0.86 | 0.98 |
| poke rebound | 0.94 × 1.08 | 1.015 |
| stretch | 1.18 × 0.84 | 0.99 |

The pivot is bottom-centre, which is right for landings. Keep all of it.

The next three notes come from classic animation principles, which the installed motion skills don't cover:
- **Overlapping action on poke.** The leaf flops 60–80 ms late and the eyes squint (scale-y 0.85) during the squash. Nearly free, and it's what makes the Bao squish feel alive.
- **Pivot for in-air pokes.** Use the body centre, not the ground point.
- **Finite celebration loops.** `poked` and `celebrate` currently loop their squish forever (`Loop3 … Forever` in `ExpressionPlayer`). Play it 1–3 times, then rest. motion-foundations reserves "bouncy" for playful moments only.

**Reduced motion** (Windows "Animation effects" off; motion-foundations rule 3): stop bob, orbit and glints (the moon rests beside her); expressions become opacity crossfades ≤ 200 ms; blink becomes a quick face swap.

**Face swaps:** make-interfaces-feel-better crossfades icon swaps with opacity, scale and blur; the 250 ms face crossfade can add a 0.96 → 1 scale.

> **Superseded (2026-11-10):** §5–6 below (removing glows, an outline, a simpler small face) were not adopted. The approved spec `docs/superpowers/specs/2026-11-08-sahar-look-menu-settings-design.md` keeps every original glow and the halo (owner rule: nothing of her original look is removed); read §5–6 as history.

## 5. Verification before shipping

- [ ] Each mood measured **on device** at 48/80/120 px and 100%/150%, over white Chrome, VS Code dark, the Windows 11 wallpaper and a photo: outline ≥ 3:1 on white and on `#202020`.
- [ ] The contact sheet re-rendered from the engine: all 29 expressions distinct at 48 px.
- [ ] No glow, emoji or four-point sparkle left anywhere in her tree.
- [ ] At most 4 independent idle clocks; reduced motion honoured.
- [ ] Rest budget unchanged or better: removing the halo, star and orbit surfaces should *lower* GPU memory.
- [ ] Owner check: one light/dark screenshot pair of her next to the Porcelain menu.

## 6. Decisions for the owner

1. **Which direction?** **B · night inside** (recommended), A (polish only), or C (new silhouette)?
2. **A drawn outline around her?** A thin line in her mood colour, the same thickness at every size. It's what keeps her visible on white windows. It's also the biggest visible change.
3. **Remove all glows?** That's the halo, the bud's glow, the moon's glow and the dashed orbit line. Her bud still changes colour; it just stops glowing.
4. **Simpler face at the small size?** At 48 px her face gets bolder, with fewer details. Event bubbles show drawn marks instead of emoji.
5. **Amber for "Claude needs permission"?** It's currently almost the same colour as "happy".
