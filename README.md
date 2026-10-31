# desk-companion

A desktop companion for Windows (working name). Arabic-first, light, and native.

**Right now this repository only holds a throwaway spike** (`spike/`): it answers one question before
the real app is designed. Can a C# Native AOT app draw a smooth, click-through character over the
whole screen while staying light? It compares two drawing approaches:

- **A: Windows Composition.** One transparent overlay; the compositor runs the animations.
- **B: SkiaSharp.** A small per-pixel-alpha window; the app redraws every frame.

## Try the spike

1. Open the **Releases** page of this repository and download `DeskCompanionSpike.zip` from the
   `spike` pre-release.
2. Extract it, then run `Run-A.cmd` (or `Run-B.cmd`). Windows may warn that the app is unrecognised:
   **More info → Run anyway**.
3. A small purple blob sits above the taskbar, and a HUD window shows RAM, CPU and FPS.
   Drag the blob, throw it, click it. Close the HUD to quit.

Metrics are also written to `%LOCALAPPDATA%\DeskCompanionSpike\` as CSV, with a `log.txt`.

The spike code is deleted once the numbers are in.
