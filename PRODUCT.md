# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

Note: the settings app is a web page inside a Windows window (WebView2); the on-click menu is drawn natively inside the Sahar engine (Windows.UI.Composition + Direct2D) and must follow the same visual language. A mobile version comes later and should reuse the settings UI.

## Users
The owner of the laptop, working on it all day, alone. Later possibly other people (so nothing may depend on the owner's personal data or taste being hard-coded). Arabic-first (Levantine), English second; right-to-left by default.

## Product Purpose
سَحَر · Sahar is a small living companion on the Windows desktop: a soft creature (night sky inside her, a leaf and a glowing bud on her head, a pet moon orbiting her) that floats over everything, reacts to what happens on the laptop, plays, and keeps the owner company. The on-click menu is how you reach her; the settings app is how you control everything she does. Success: control feels effortless and the surfaces feel like part of her, not like a system panel.

## Positioning
Not a widget bar or a productivity dashboard: a character with moods (calm, happy, focus, excited, sleepy, mystic) that lives on the screen. Every surface belongs to her world.

## Operating Context
Used beside real work (browsers, code editors, Claude Code), in daylight and at night equally, so both light and dark appearances are first-class. The menu opens on double-click/right-click next to her and must close fast (Esc, click outside). The settings app opens on demand and closes completely.

## Capabilities and Constraints
- Menu tools today: timer, quick note, listening (music + Quran), prayer times, clipboard, play (games), activity level, do-not-disturb, hide, settings. The owner expects to use these tools rarely; the menu may be reduced, regrouped or replaced by something more about Sahar herself.
- Settings pages: Sahar (size, activity, how she addresses you: man/woman/unspecified, what she does by herself), General, Sounds, Prayer, Listening, Privacy, Claude Code, About.
- Engine stays light (RAM < 60 MB at rest); settings app closes completely when closed.
- Must work at 100% and 150% DPI, Windows 10 and 11.
- Undecided: final menu contents.

## Brand Commitments
- Name: سَحَر · Sahar (pet name سحورة). Her body palette per mood is fixed in `src/Sahar.Core/assets/palette.json` (e.g. Calm: #FFFFFF → #F1ECFF → #CBC1F0, glow #B9B0FF, blush #FFB8A0; leaf greens).
- The owner rejects: anything that looks AI-generated (glows, glass-on-navy, neon edges), cluttered screens, and cold formal Windows-settings look.

## Evidence on Hand
- The character art lives in the engine code and the design boards; no photography. No users, testimonials or metrics exist — never invent any.

## Product Principles
1. She is the interface: surfaces read as extensions of her, never a generic control panel.
2. Calm by default: one thing wins the eye; few choices visible at once.
3. Effortless control: every setting is one obvious step, with an immediate visible effect on her.
4. Equal in day and night.
5. Hers, not trendy: no category-default looks.

## Accessibility & Inclusion
Arabic RTL with English LTR; text contrast 4.5:1 in both appearances; keyboard reachable menu and settings.
