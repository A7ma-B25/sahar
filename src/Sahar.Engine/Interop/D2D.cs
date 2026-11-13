using System.Numerics;
using System.Runtime.InteropServices;
using Sahar.Core.Art;

namespace Sahar.Engine;

// Direct2D through raw vtable calls (blittable structs, function pointers): AOT-safe, no projection needed.
// Slot numbers follow d2d1.h declaration order (IUnknown 0–2, ID2D1Resource::GetFactory 3).

[StructLayout(LayoutKind.Sequential)]
internal struct D2DColor
{
    public float R, G, B, A;
    public D2DColor(Rgba c, float alpha = 1f) { R = c.R / 255f; G = c.G / 255f; B = c.B / 255f; A = c.A / 255f * alpha; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2DPoint { public float X, Y; public D2DPoint(Vector2 v) { X = v.X; Y = v.Y; } }

[StructLayout(LayoutKind.Sequential)]
internal struct D2DRect { public float Left, Top, Right, Bottom; }

[StructLayout(LayoutKind.Sequential)]
internal struct D2DEllipse { public D2DPoint Center; public float Rx, Ry; }

[StructLayout(LayoutKind.Sequential)]
internal struct D2DRoundedRect { public D2DRect Rect; public float Rx, Ry; }

/// <summary>Logical text alignment (start = right in RTL).</summary>
internal enum TextAlign { Start, Centre, End }

[StructLayout(LayoutKind.Sequential)]
internal struct DWriteTextMetrics
{
    public float Left, Top, Width, WidthIncludingTrailingWhitespace, Height, LayoutWidth, LayoutHeight;
    public uint MaxBidiReorderingDepth, LineCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DWriteTextRange { public uint Start, Length; }

[StructLayout(LayoutKind.Sequential)]
internal struct D2DBezier { public D2DPoint P1, P2, P3; }

[StructLayout(LayoutKind.Sequential)]
internal struct D2DQuadBezier { public D2DPoint P1, P2; }

[StructLayout(LayoutKind.Sequential)]
internal struct D2DArc
{
    public D2DPoint Point;
    public float Rx, Ry;
    public float Rotation;
    public int Sweep;   // D2D1_SWEEP_DIRECTION: 0 counter-clockwise, 1 clockwise
    public int ArcSize; // D2D1_ARC_SIZE: 0 small, 1 large
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2DMatrix
{
    public float M11, M12, M21, M22, Dx, Dy;
    public static D2DMatrix From(Matrix3x2 m) => new() { M11 = m.M11, M12 = m.M12, M21 = m.M21, M22 = m.M22, Dx = m.M31, Dy = m.M32 };
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2DBrushProps { public float Opacity; public D2DMatrix Transform; }

[StructLayout(LayoutKind.Sequential)]
internal struct D2DSizeU { public uint Width, Height; }

[StructLayout(LayoutKind.Sequential)]
internal struct D2DBitmapProps { public uint Format, Alpha; public float DpiX, DpiY; }

[StructLayout(LayoutKind.Sequential)]
internal struct D2DBitmapBrushProps { public uint ExtendX, ExtendY, Interpolation; }

[StructLayout(LayoutKind.Sequential)]
internal struct D2DGradientStop { public float Position; public D2DColor Color; }

[StructLayout(LayoutKind.Sequential)]
internal struct D2DRadialProps { public D2DPoint Center, OriginOffset; public float Rx, Ry; }

[StructLayout(LayoutKind.Sequential)]
internal struct D2DLinearProps { public D2DPoint Start, End; }

[StructLayout(LayoutKind.Sequential)]
internal struct D2DStrokeProps
{
    public int StartCap, EndCap, DashCap, LineJoin;
    public float MiterLimit;
    public int DashStyle;
    public float DashOffset;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINTL { public int X, Y; }

/// <summary>Thrown when a Direct2D call fails; carries the HRESULT so device loss can be recognised.</summary>
internal sealed class D2DException(int hr, string call) : Exception($"{call} failed: 0x{hr:X8}")
{
    public int HResult2 { get; } = hr;
}

/// <summary>
/// A Direct2D device context borrowed from a composition drawing surface for one BeginDraw/EndDraw.
/// Every resource it creates is released when it is disposed.
/// </summary>
internal sealed unsafe class D2DContext : IDisposable
{
    private static readonly Guid IID_ICompositionDrawingSurfaceInterop = new("FD04E6E3-FE0C-4C3C-AB19-A07601A576EE");
    private static readonly Guid IID_ID2D1DeviceContext = new("E8F7FE7A-191C-466D-AD95-975678BDA998");

    // ID2D1RenderTarget slots
    private const int CreateSolidColorBrushSlot = 8, CreateGradientStopCollectionSlot = 9,
        CreateLinearGradientBrushSlot = 10, CreateRadialGradientBrushSlot = 11, DrawEllipseSlot = 20,
        FillEllipseSlot = 21, DrawGeometrySlot = 22, FillGeometrySlot = 23, SetTransformSlot = 30,
        ClearSlot = 47, SetDpiSlot = 51;
    // ID2D1Factory slots
    private const int CreatePathGeometrySlot = 10, CreateStrokeStyleSlot = 11;
    // ID2D1Geometry / ID2D1PathGeometry slots
    private const int GetBoundsSlot = 4, OpenSlot = 17;
    // ID2D1GeometrySink slots
    private const int SetFillModeSlot = 3, BeginFigureSlot = 5, EndFigureSlot = 8, CloseSlot = 9, AddLineSlot = 10,
        AddBezierSlot = 11, AddQuadraticBezierSlot = 12, AddArcSlot = 14;

    private nint _surfaceInterop;
    private nint _dc;
    private nint _factory;
    private readonly List<nint> _owned = [];
    private Matrix3x2 _base;

    public static bool TryBegin(object drawingSurface, out D2DContext? ctx, out int hr)
    {
        ctx = null;
        nint interop = Com.QueryInterface(Com.Unknown(drawingSurface), IID_ICompositionDrawingSurfaceInterop);
        nint dc;
        POINT offset;
        var iid = IID_ID2D1DeviceContext;
        // ICompositionDrawingSurfaceInterop::BeginDraw(const RECT* updateRect, REFIID, void** updateObject, POINT* offset)
        var begin = (delegate* unmanaged[Stdcall]<nint, RECT*, Guid*, nint*, POINT*, int>)Com.Slot(interop, 3);
        hr = begin(interop, null, &iid, &dc, &offset);
        if (hr < 0) { Marshal.Release(interop); return false; }
        ctx = new D2DContext(interop, dc, new Vector2(offset.X, offset.Y));
        return true;
    }

    private D2DContext(nint surfaceInterop, nint dc, Vector2 offset)
    {
        _surfaceInterop = surfaceInterop;
        _dc = dc;
        ((delegate* unmanaged[Stdcall]<nint, float, float, void>)Com.Slot(_dc, SetDpiSlot))(_dc, 96f, 96f);
        nint factory;
        ((delegate* unmanaged[Stdcall]<nint, nint*, void>)Com.Slot(_dc, 3))(_dc, &factory);
        _factory = factory;
        _base = Matrix3x2.CreateTranslation(offset);
        SetTransform(Matrix3x2.Identity);
    }

    /// <summary>Sets the drawing transform (applied after the surface's update offset).</summary>
    public void SetTransform(Matrix3x2 m)
    {
        _current = m;
        var d = D2DMatrix.From(m * _base);
        ((delegate* unmanaged[Stdcall]<nint, D2DMatrix*, void>)Com.Slot(_dc, SetTransformSlot))(_dc, &d);
    }

    private Matrix3x2 _current = Matrix3x2.Identity;
    private readonly Stack<Matrix3x2> _saved = new();

    /// <summary>Draws in a local space (<paramref name="m"/> then the current transform) until <see cref="PopTransform"/>.</summary>
    public void PushTransform(Matrix3x2 m)
    {
        _saved.Push(_current);
        SetTransform(m * _current);
    }

    public void PopTransform() => SetTransform(_saved.Pop());

    public void Clear()
    {
        var c = default(D2DColor);
        ((delegate* unmanaged[Stdcall]<nint, D2DColor*, void>)Com.Slot(_dc, ClearSlot))(_dc, &c);
    }

    public nint SolidBrush(D2DColor color, float opacity = 1f)
    {
        var props = new D2DBrushProps { Opacity = opacity, Transform = D2DMatrix.From(Matrix3x2.Identity) };
        nint brush;
        Check(((delegate* unmanaged[Stdcall]<nint, D2DColor*, D2DBrushProps*, nint*, int>)Com.Slot(_dc, CreateSolidColorBrushSlot))(_dc, &color, &props, &brush), "CreateSolidColorBrush");
        return Own(brush);
    }

    public nint RadialBrush(ReadOnlySpan<D2DGradientStop> stops, Vector2 center, Vector2 radii, float opacity = 1f)
    {
        nint collection = Stops(stops);
        var rp = new D2DRadialProps { Center = new D2DPoint(center), Rx = radii.X, Ry = radii.Y };
        var bp = new D2DBrushProps { Opacity = opacity, Transform = D2DMatrix.From(Matrix3x2.Identity) };
        nint brush;
        Check(((delegate* unmanaged[Stdcall]<nint, D2DRadialProps*, D2DBrushProps*, nint, nint*, int>)Com.Slot(_dc, CreateRadialGradientBrushSlot))(_dc, &rp, &bp, collection, &brush), "CreateRadialGradientBrush");
        return Own(brush);
    }

    public nint LinearBrush(ReadOnlySpan<D2DGradientStop> stops, Vector2 start, Vector2 end, float opacity = 1f)
    {
        nint collection = Stops(stops);
        var lp = new D2DLinearProps { Start = new D2DPoint(start), End = new D2DPoint(end) };
        var bp = new D2DBrushProps { Opacity = opacity, Transform = D2DMatrix.From(Matrix3x2.Identity) };
        nint brush;
        Check(((delegate* unmanaged[Stdcall]<nint, D2DLinearProps*, D2DBrushProps*, nint, nint*, int>)Com.Slot(_dc, CreateLinearGradientBrushSlot))(_dc, &lp, &bp, collection, &brush), "CreateLinearGradientBrush");
        return Own(brush);
    }

    private nint Stops(ReadOnlySpan<D2DGradientStop> stops)
    {
        nint collection;
        fixed (D2DGradientStop* p = stops)
            Check(((delegate* unmanaged[Stdcall]<nint, D2DGradientStop*, uint, int, int, nint*, int>)Com.Slot(_dc, CreateGradientStopCollectionSlot))(_dc, p, (uint)stops.Length, 0 /* gamma 2.2 */, 0 /* clamp */, &collection), "CreateGradientStopCollection");
        return Own(collection);
    }

    /// <summary>A stroke style with the given cap ("butt", "round", "square") and optional dashes (in stroke widths).</summary>
    public nint StrokeStyle(string lineCap, ReadOnlySpan<float> dashes = default)
    {
        int cap = lineCap switch { "round" => 2, "square" => 1, _ => 0 };
        var props = new D2DStrokeProps { StartCap = cap, EndCap = cap, DashCap = cap, LineJoin = 2 /* round */, MiterLimit = 4, DashStyle = dashes.IsEmpty ? 0 : 5 /* custom */ };
        nint style;
        fixed (float* d = dashes)
            Check(((delegate* unmanaged[Stdcall]<nint, D2DStrokeProps*, float*, uint, nint*, int>)Com.Slot(_factory, CreateStrokeStyleSlot))(_factory, &props, d, (uint)dashes.Length, &style), "CreateStrokeStyle");
        return Own(style);
    }

    /// <summary>A path geometry from parsed SVG path commands (nonzero fill, like SVG).</summary>
    public nint Path(IReadOnlyList<PathCommand> commands)
    {
        nint geometry, sink;
        Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(_factory, CreatePathGeometrySlot))(_factory, &geometry), "CreatePathGeometry");
        Own(geometry);
        Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Slot(geometry, OpenSlot))(geometry, &sink), "Open");
        try
        {
            ((delegate* unmanaged[Stdcall]<nint, int, void>)Com.Slot(sink, SetFillModeSlot))(sink, 1 /* winding */);
            var beginFigure = (delegate* unmanaged[Stdcall]<nint, D2DPoint, int, void>)Com.Slot(sink, BeginFigureSlot);
            var endFigure = (delegate* unmanaged[Stdcall]<nint, int, void>)Com.Slot(sink, EndFigureSlot);
            var addLine = (delegate* unmanaged[Stdcall]<nint, D2DPoint, void>)Com.Slot(sink, AddLineSlot);
            var addBezier = (delegate* unmanaged[Stdcall]<nint, D2DBezier*, void>)Com.Slot(sink, AddBezierSlot);
            var addQuad = (delegate* unmanaged[Stdcall]<nint, D2DQuadBezier*, void>)Com.Slot(sink, AddQuadraticBezierSlot);
            var addArc = (delegate* unmanaged[Stdcall]<nint, D2DArc*, void>)Com.Slot(sink, AddArcSlot);

            bool open = false;
            foreach (var c in commands)
            {
                switch (c.Op)
                {
                    case PathOp.MoveTo:
                        if (open) endFigure(sink, 0);
                        beginFigure(sink, new D2DPoint(c.P3), 0 /* filled */);
                        open = true;
                        break;
                    case PathOp.LineTo:
                        addLine(sink, new D2DPoint(c.P3));
                        break;
                    case PathOp.CubicTo:
                        var b = new D2DBezier { P1 = new(c.P1), P2 = new(c.P2), P3 = new(c.P3) };
                        addBezier(sink, &b);
                        break;
                    case PathOp.QuadTo:
                        var q = new D2DQuadBezier { P1 = new(c.P1), P2 = new(c.P3) };
                        addQuad(sink, &q);
                        break;
                    case PathOp.ArcTo:
                        var a = new D2DArc { Point = new(c.P3), Rx = c.Radii.X, Ry = c.Radii.Y, Rotation = c.XRotation, Sweep = c.Sweep ? 1 : 0, ArcSize = c.LargeArc ? 1 : 0 };
                        addArc(sink, &a);
                        break;
                    case PathOp.Close:
                        if (open) endFigure(sink, 1 /* closed */);
                        open = false;
                        break;
                }
            }
            if (open) endFigure(sink, 0);
            Check(((delegate* unmanaged[Stdcall]<nint, int>)Com.Slot(sink, CloseSlot))(sink), "GeometrySink.Close");
        }
        finally
        {
            Marshal.Release(sink);
        }
        return geometry;
    }

    public D2DRect Bounds(nint geometry)
    {
        D2DRect r;
        Check(((delegate* unmanaged[Stdcall]<nint, D2DMatrix*, D2DRect*, int>)Com.Slot(geometry, GetBoundsSlot))(geometry, null, &r), "GetBounds");
        return r;
    }

    public void FillGeometry(nint geometry, nint brush) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, nint, void>)Com.Slot(_dc, FillGeometrySlot))(_dc, geometry, brush, 0);

    public void DrawGeometry(nint geometry, nint brush, float width, nint strokeStyle) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, float, nint, void>)Com.Slot(_dc, DrawGeometrySlot))(_dc, geometry, brush, width, strokeStyle);

    public void FillEllipse(Vector2 center, Vector2 radii, nint brush)
    {
        var e = new D2DEllipse { Center = new D2DPoint(center), Rx = radii.X, Ry = radii.Y };
        ((delegate* unmanaged[Stdcall]<nint, D2DEllipse*, nint, void>)Com.Slot(_dc, FillEllipseSlot))(_dc, &e, brush);
    }

    public void DrawEllipse(Vector2 center, Vector2 radii, nint brush, float width, nint strokeStyle)
    {
        var e = new D2DEllipse { Center = new D2DPoint(center), Rx = radii.X, Ry = radii.Y };
        ((delegate* unmanaged[Stdcall]<nint, D2DEllipse*, nint, float, nint, void>)Com.Slot(_dc, DrawEllipseSlot))(_dc, &e, brush, width, strokeStyle);
    }

    /// <summary>
    /// Draws text centred in <paramref name="box"/> (design units, before the transform). Colour fonts
    /// (Segoe UI Emoji) draw in their own colours; other text uses <paramref name="brush"/>.
    /// </summary>
    public void Text(string text, string family, float size, int weight, D2DRect box, nint brush, bool rightToLeft = false)
    {
        nint format = DWrite.TextFormat(family, size, weight);
        Own(format);
        // IDWriteTextFormat::SetReadingDirection(DWRITE_READING_DIRECTION_RIGHT_TO_LEFT): Arabic lines with Latin names read right.
        if (rightToLeft) ((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Slot(format, 6))(format, 1);
        // ID2D1RenderTarget::DrawText(WCHAR*, UINT32, IDWriteTextFormat*, D2D1_RECT_F*, ID2D1Brush*, options, measuring)
        const int D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT = 4;
        fixed (char* t = text)
            ((delegate* unmanaged[Stdcall]<nint, char*, uint, nint, D2DRect*, nint, int, int, void>)Com.Slot(_dc, 27))(
                _dc, t, (uint)text.Length, format, &box, brush, D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT, 0);
    }

    /// <summary>ID2D1RenderTarget::FillRoundedRectangle (slot 19).</summary>
    public void FillRoundedRect(D2DRect r, float radius, nint brush)
    {
        var rr = new D2DRoundedRect { Rect = r, Rx = radius, Ry = radius };
        ((delegate* unmanaged[Stdcall]<nint, D2DRoundedRect*, nint, void>)Com.Slot(_dc, 19))(_dc, &rr, brush);
    }

    /// <summary>ID2D1RenderTarget::DrawRoundedRectangle (slot 18).</summary>
    public void DrawRoundedRect(D2DRect r, float radius, nint brush, float width)
    {
        var rr = new D2DRoundedRect { Rect = r, Rx = radius, Ry = radius };
        ((delegate* unmanaged[Stdcall]<nint, D2DRoundedRect*, nint, float, nint, void>)Com.Slot(_dc, 18))(_dc, &rr, brush, width, 0);
    }

    /// <summary>
    /// Text in Rubik (or Segoe UI when it did not load) laid out in <paramref name="box"/>, vertically centred, aligned to
    /// the logical start/centre/end (mapped with <paramref name="rtl"/>); every run of digits (0–9, ٠–٩) gets fixed cells of
    /// <paramref name="digitCell"/> em (plan Ruling 5: Rubik's Arabic-Indic digits are proportional).
    /// Returns the laid-out text's size.
    /// </summary>
    public Vector2 TextLayout(string text, float size, int weight, D2DRect box, nint brush, TextAlign align, bool rtl, float digitCell = DWrite.DigitCell,
        bool wrap = false)
    {
        nint layout = DWrite.Layout(text, size, weight, box.Right - box.Left, box.Bottom - box.Top, align, rtl, digitCell, wrap);
        Own(layout);
        var origin = new D2DPoint { X = box.Left, Y = box.Top };
        // ID2D1RenderTarget::DrawTextLayout(origin, layout, brush, options)
        ((delegate* unmanaged[Stdcall]<nint, D2DPoint, nint, nint, int, void>)Com.Slot(_dc, 28))(_dc, origin, layout, brush, 0);
        return DWrite.Size(layout);
    }

    /// <summary>A bitmap from 8-bit premultiplied BGRA pixels (a cover, a thumbnail); released with the context.</summary>
    public nint Bitmap(ReadOnlySpan<byte> bgra, int width, int height)
    {
        // ID2D1RenderTarget::CreateBitmap(D2D1_SIZE_U, const void*, UINT32 pitch, const D2D1_BITMAP_PROPERTIES*, ID2D1Bitmap**)
        var props = new D2DBitmapProps { Format = 87 /* DXGI_FORMAT_B8G8R8A8_UNORM */, Alpha = 1 /* premultiplied */, DpiX = 96, DpiY = 96 };
        var size = new D2DSizeU { Width = (uint)width, Height = (uint)height };
        nint bitmap;
        fixed (byte* px = bgra)
            Check(((delegate* unmanaged[Stdcall]<nint, D2DSizeU, void*, uint, D2DBitmapProps*, nint*, int>)Com.Slot(_dc, 4))(_dc, size, px, (uint)(width * 4), &props, &bitmap), "CreateBitmap");
        return Own(bitmap);
    }

    /// <summary>A bitmap stretched into <paramref name="r"/> with rounded corners (a bitmap brush filling a rounded rect).</summary>
    public void FillRoundedImage(D2DRect r, float radius, nint bitmap, int width, int height)
    {
        // ID2D1RenderTarget::CreateBitmapBrush(ID2D1Bitmap*, const D2D1_BITMAP_BRUSH_PROPERTIES*, const D2D1_BRUSH_PROPERTIES*, ID2D1BitmapBrush**)
        var bbp = new D2DBitmapBrushProps { ExtendX = 0, ExtendY = 0, Interpolation = 1 /* linear */ };
        var m = Matrix3x2.CreateScale((r.Right - r.Left) / width, (r.Bottom - r.Top) / height) * Matrix3x2.CreateTranslation(r.Left, r.Top);
        var bp = new D2DBrushProps { Opacity = 1, Transform = D2DMatrix.From(m) };
        nint brush;
        Check(((delegate* unmanaged[Stdcall]<nint, nint, D2DBitmapBrushProps*, D2DBrushProps*, nint*, int>)Com.Slot(_dc, 7))(_dc, bitmap, &bbp, &bp, &brush), "CreateBitmapBrush");
        Own(brush);
        FillRoundedRect(r, radius, brush);
    }

    private nint Own(nint p) { _owned.Add(p); return p; }

    private static void Check(int hr, string call)
    {
        if (hr < 0) throw new D2DException(hr, call);
    }

    /// <summary>Ends the draw (ICompositionDrawingSurfaceInterop::EndDraw) and releases everything. Returns the HRESULT.</summary>
    public int End()
    {
        if (_surfaceInterop == 0) return 0;
        foreach (var p in _owned) Marshal.Release(p);
        _owned.Clear();
        Com.Release(ref _factory);
        Com.Release(ref _dc);
        int hr = ((delegate* unmanaged[Stdcall]<nint, int>)Com.Slot(_surfaceInterop, 4))(_surfaceInterop);
        Com.Release(ref _surfaceInterop);
        return hr;
    }

    public void Dispose() => End();
}

/// <summary>The shared DirectWrite factory and text formats (centred both ways).</summary>
internal static unsafe partial class DWrite
{
    private static readonly Guid IID_IDWriteFactory = new("B859EE5A-D838-4B5B-A2E8-1ADC7D93DB48");
    private static nint _factory;

    [LibraryImport("dwrite.dll")]
    private static partial int DWriteCreateFactory(int type, Guid* iid, nint* factory);

    private static nint Factory
    {
        get
        {
            if (_factory != 0) return _factory;
            var iid = IID_IDWriteFactory;
            nint f;
            int hr = DWriteCreateFactory(0 /* shared */, &iid, &f);
            if (hr < 0) throw new D2DException(hr, "DWriteCreateFactory");
            _factory = f;
            return f;
        }
    }

    /// <summary>Rubik's widest Arabic-Indic digit is 0.668 em (tools/fonts/make_rubik.py): a cell every digit fits.</summary>
    public const float DigitCell = 0.67f;

    /// <summary>The shared IDWriteFactory (not AddRef'd).</summary>
    public static nint SharedFactory => Factory;

    /// <summary>A new IDWriteTextFormat (caller releases). Weight 400 normal, 600 semibold. <paramref name="collection"/> 0 = system fonts.</summary>
    public static nint TextFormat(string family, float size, int weight, nint collection = 0)
    {
        nint format;
        fixed (char* fam = family)
        fixed (char* locale = "")
        {
            // IDWriteFactory::CreateTextFormat(family, collection, weight, style, stretch, size, locale, out)
            int hr = ((delegate* unmanaged[Stdcall]<nint, char*, nint, int, int, int, float, char*, nint*, int>)Com.Slot(Factory, 15))(
                Factory, fam, collection, weight, 0 /* normal */, 5 /* normal */, size, locale, &format);
            if (hr < 0) throw new D2DException(hr, "CreateTextFormat");
        }
        // SetTextAlignment(CENTER), SetParagraphAlignment(CENTER)
        ((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Slot(format, 3))(format, 2);
        ((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Slot(format, 4))(format, 2);
        return format;
    }

    /// <summary>A text layout in Rubik (caller releases): reading direction, logical alignment, centred vertically, no
    /// wrapping, digit runs in fixed cells (IDWriteTextLayout1::SetCharacterSpacing min advance).</summary>
    public static nint Layout(string text, float size, int weight, float width, float height, TextAlign align, bool rtl, float digitCell, bool wrap = false)
    {
        nint collection = Render.Fonts.Collection;
        nint format = TextFormat(collection != 0 ? Render.Fonts.Family : "Segoe UI", size, weight, collection);
        nint layout;
        try
        {
            // DWRITE_TEXT_ALIGNMENT: leading 0, trailing 1, centre 2 (leading follows the reading direction)
            ((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Slot(format, 3))(format, align switch { TextAlign.Start => 0, TextAlign.End => 1, _ => 2 });
            ((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Slot(format, 4))(format, 2);   // paragraph: centre
            ((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Slot(format, 5))(format, wrap ? 0 : 1);   // wrap, or one line
            ((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Slot(format, 6))(format, rtl ? 1 : 0);
            fixed (char* t = text)
            {
                // IDWriteFactory::CreateTextLayout(string, length, format, maxWidth, maxHeight, out)
                int hr = ((delegate* unmanaged[Stdcall]<nint, char*, uint, nint, float, float, nint*, int>)Com.Slot(Factory, 18))(
                    Factory, t, (uint)text.Length, format, width, height, &layout);
                if (hr < 0) throw new D2DException(hr, "CreateTextLayout");
            }
        }
        finally { Marshal.Release(format); }
        nint layout1 = 0;
        try
        {
            layout1 = Com.QueryInterface(layout, IID_IDWriteTextLayout1);
            int i = 0;
            while (i < text.Length)
            {
                if (!IsDigit(text[i])) { i++; continue; }
                int start = i;
                while (i < text.Length && IsDigit(text[i])) i++;
                var range = new DWriteTextRange { Start = (uint)start, Length = (uint)(i - start) };
                // IDWriteTextLayout1::SetCharacterSpacing(leading, trailing, minimumAdvance, range)
                ((delegate* unmanaged[Stdcall]<nint, float, float, float, DWriteTextRange, int>)Com.Slot(layout1, 69))(layout1, 0, 0, digitCell * size, range);
            }
        }
        catch (Exception) { }   // no IDWriteTextLayout1 (very old Windows): proportional digits
        finally { if (layout1 != 0) Marshal.Release(layout1); }
        return layout;
    }

    private static bool IsDigit(char c) => c is >= '0' and <= '9' or >= '٠' and <= '٩';

    private static readonly Guid IID_IDWriteTextLayout1 = new("9064D822-80A7-465C-A986-DF65F78B8FEB");

    /// <summary>The laid-out text's width and height (IDWriteTextLayout::GetMetrics, slot 60).</summary>
    public static System.Numerics.Vector2 Size(nint layout)
    {
        DWriteTextMetrics m;
        int hr = ((delegate* unmanaged[Stdcall]<nint, DWriteTextMetrics*, int>)Com.Slot(layout, 60))(layout, &m);
        return hr < 0 ? default : new System.Numerics.Vector2(m.WidthIncludingTrailingWhitespace, m.Height);
    }

    /// <summary>Measures text as <see cref="Layout"/> would draw it.</summary>
    public static System.Numerics.Vector2 Measure(string text, float size, int weight, bool rtl)
    {
        nint l = Layout(text, size, weight, 10000, 10000, TextAlign.Start, rtl, DigitCell);
        try { return Size(l); }
        finally { Marshal.Release(l); }
    }
}
