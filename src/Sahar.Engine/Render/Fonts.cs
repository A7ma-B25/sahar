using System.Runtime.InteropServices;
using Sahar.Engine.Diagnostics;

namespace Sahar.Engine.Render;

/// <summary>
/// Rubik inside the engine (spec §5, plan 0.10b Ruling 6): three static faces (400, 500, 600) embedded as resources,
/// loaded once on the UI thread into a DirectWrite in-memory font set (IDWriteFactory5) → a font collection with the
/// family "Rubik"; and the Regular face for GDI (the chat field's EDIT), private to the process. A failure is logged
/// once and the card falls back to Segoe UI.
/// </summary>
internal static unsafe class Fonts
{
    public const string Family = "Rubik";
    private static readonly string[] Faces = ["Regular", "Medium", "SemiBold"];
    private static readonly Guid IID_IDWriteFactory5 = new("958DB99A-BE2A-4F09-AF7D-65189803D1D3");
    private static bool _tried;
    private static nint _collection;
    private static int _families;
    private static bool _gdi;
    private static readonly Dictionary<int, nint> GdiFonts = [];

    /// <summary>The IDWriteFontCollection1 with Rubik, or 0 (then use the system's Segoe UI).</summary>
    public static nint Collection { get { Load(); return _collection; } }
    public static bool Loaded => Collection != 0;
    /// <summary>The family to ask for: Rubik when it loaded.</summary>
    public static string Name => Loaded ? Family : "Segoe UI";

    /// <summary>A GDI font of Rubik Regular <paramref name="pxHeight"/> px tall (cached; the process keeps it).</summary>
    public static nint GdiFont(float pxHeight)
    {
        Load();
        int h = (int)MathF.Round(pxHeight);
        if (GdiFonts.TryGetValue(h, out var f)) return f;
        fixed (char* face = _gdi ? Family : "Segoe UI")
            f = Native.CreateFont(-h, 0, 0, 0, 400, 0, 0, 0, 1 /* DEFAULT_CHARSET */, 0, 0, 5 /* CLEARTYPE */, 0, face);
        GdiFonts[h] = f;
        return f;
    }

    public static string Describe()
    {
        Load();
        return $"rubik={(_collection != 0 ? "true" : "false")} families={_families} gdi={(_gdi ? "true" : "false")}";
    }

    private static byte[] Bytes(string face)
    {
        using var s = typeof(Fonts).Assembly.GetManifestResourceStream($"Sahar.Fonts.Rubik-{face}.ttf")
            ?? throw new InvalidOperationException($"font resource {face} missing");
        var b = new byte[s.Length];
        s.ReadExactly(b);
        return b;
    }

    private static void Load()
    {
        if (_tried) return;
        _tried = true;
        var data = new List<byte[]>();
        try { foreach (var f in Faces) data.Add(Bytes(f)); }
        catch (Exception e) { Log.Write($"font: Rubik not loaded ({e.GetType().Name})"); return; }

        // GDI: private to the process (FR_PRIVATE semantics), for the chat field.
        try
        {
            uint count = 0;
            fixed (byte* p = data[0]) _gdi = Native.AddFontMemResourceEx(p, (uint)data[0].Length, 0, &count) != 0;
        }
        catch (Exception e) { Log.Write($"font: GDI Rubik not added ({e.GetType().Name})"); }

        nint factory5 = 0, loader = 0, builder = 0, set = 0, collection = 0;
        var files = new List<nint>();
        int hr = 0;
        string step = "";
        try
        {
            step = "QueryInterface(IDWriteFactory5)";
            factory5 = Com.QueryInterface(DWrite.SharedFactory, IID_IDWriteFactory5);
            step = "CreateInMemoryFontFileLoader";
            hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(factory5, 44))(factory5, &loader);
            if (hr < 0) throw new COMException(step, hr);
            step = "RegisterFontFileLoader";
            hr = ((delegate* unmanaged[Stdcall]<nint, nint, int>)Com.Slot(factory5, 13))(factory5, loader);
            if (hr < 0) throw new COMException(step, hr);
            step = "CreateFontSetBuilder";
            hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(factory5, 43))(factory5, &builder);
            if (hr < 0) throw new COMException(step, hr);
            foreach (var bytes in data)
            {
                nint file;
                step = "CreateInMemoryFontFileReference";
                // IDWriteInMemoryFontFileLoader::CreateInMemoryFontFileReference(factory, data, size, owner (null = copy), out)
                fixed (byte* p = bytes)
                    hr = ((delegate* unmanaged[Stdcall]<nint, nint, byte*, uint, nint, nint*, int>)Com.Slot(loader, 4))(
                        loader, factory5, p, (uint)bytes.Length, 0, &file);
                if (hr < 0) throw new COMException(step, hr);
                files.Add(file);
                step = "AddFontFile";
                hr = ((delegate* unmanaged[Stdcall]<nint, nint, int>)Com.Slot(builder, 7))(builder, file);
                if (hr < 0) throw new COMException(step, hr);
            }
            step = "CreateFontSet";
            hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(builder, 6))(builder, &set);
            if (hr < 0) throw new COMException(step, hr);
            step = "CreateFontCollectionFromFontSet";
            hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Com.Slot(factory5, 37))(factory5, set, &collection);
            if (hr < 0) throw new COMException(step, hr);
            // IDWriteFontCollection::GetFontFamilyCount
            _families = (int)((delegate* unmanaged[Stdcall]<nint, uint>)Com.Slot(collection, 3))(collection);
            _collection = collection;
            collection = 0;
            Log.Write($"font: Rubik loaded ({_families} family)");
        }
        catch (Exception e)
        {
            Log.Write($"font: Rubik not loaded ({step}, 0x{(e is COMException c ? c.ErrorCode : e.HResult):X8})");
        }
        finally
        {
            foreach (var f in files) Marshal.Release(f);
            if (set != 0) Marshal.Release(set);
            if (builder != 0) Marshal.Release(builder);
            if (collection != 0) Marshal.Release(collection);
            if (factory5 != 0) Marshal.Release(factory5);
            // The loader stays registered (and referenced) for the process: the collection's files read through it.
        }
    }
}
