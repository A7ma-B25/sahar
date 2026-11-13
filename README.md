# سَحَر · Sahar

A small magical companion who lives on your Windows desktop. Arabic-first, light, native.

- Design spec: `docs/superpowers/specs/2026-11-06-sahar-windows-v1-design.md`
- Build plans: `docs/superpowers/plans/`

## تثبيتها · Install

1. نزّل `Sahar-Setup-0.11.0.exe` من صفحة النسخة («test-build») على GitHub وشغّلو. بيتثبّت إلك انت بس، بلا أدمن.
2. إذا قلك الويندوز «Windows protected your PC» (هاد SmartScreen، لأن النسخة مش موقّعة لسا):
   كبّس «More info» (معلومات إضافية) ← وبعدين «Run anyway» (تشغيل على أي حال).
3. «التالي» ← «إنهاء». بتلاقي «سحر» بقائمة ابدأ. التحديثات الجاية بتسألك عنها هي بفقاعتها.

The setup is unsigned, so SmartScreen warns on its first run: **More info → Run anyway**. It installs per user into
`%LocalAppData%\Programs\Sahar`; updates come from inside Sahar (she asks first). Uninstall from Settings → Apps.

## Layout

- `src/Sahar.Core` — data, art geometry, brain, physics, pipe protocol (pure .NET, tested on any OS)
- `src/Sahar.Engine` — the always-running Windows app (Native AOT, Composition)
- `tests/` — unit tests and the Windows UI scenario used by CI

## Build

```
dotnet test tests/Sahar.Core.Tests
dotnet publish src/Sahar.Engine -c Release -r win-x64 -o artifacts/engine   # on Windows
./packaging/installer/build.ps1 -Version 0.11.0                             # on Windows: Sahar-Setup-0.11.0.exe
```
