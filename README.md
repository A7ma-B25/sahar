# desk-companion

A desktop companion for Windows (working name). Arabic-first, light, and native.

The rendering spike is done and removed. It showed that a C# Native AOT app drawing with
Windows.UI.Composition stays around 19 MB of RAM and near-zero CPU at rest, with smooth motion,
and can be hidden from screenshots. The real app is designed next.
