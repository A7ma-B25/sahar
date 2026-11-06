# سَحَر · Sahar

A small magical companion who lives on your Windows desktop. Arabic-first, light, native.

- Design spec: `docs/superpowers/specs/2026-11-06-sahar-windows-v1-design.md`
- Build plans: `docs/superpowers/plans/`

## Layout

- `src/Sahar.Core` — data, art geometry, brain, physics, pipe protocol (pure .NET, tested on any OS)
- `src/Sahar.Engine` — the always-running Windows app (Native AOT, Composition)
- `tests/` — unit tests and the Windows UI scenario used by CI

## Build

```
dotnet test tests/Sahar.Core.Tests
dotnet publish src/Sahar.Engine -c Release -r win-x64 -o artifacts/engine   # on Windows
```
