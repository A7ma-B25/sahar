using Sahar.Core.Geometry;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Windows.Graphics;
using Windows.Graphics.DirectX;
using Windows.UI.Composition;

namespace Sahar.Engine.Render;

/// <summary>
/// Two drawing surfaces for one layer: new content is drawn into the spare one, then layers cross-fade to it.
/// Remembers how its current content was drawn so it can redraw after the GPU device is lost.
/// </summary>
internal sealed class SwapSurface
{
    private readonly CompositionHost _host;
    private readonly CompositionDrawingSurface[] _surfaces = new CompositionDrawingSurface[2];
    private readonly Action<D2DContext>?[] _draws = new Action<D2DContext>?[2];

    public CompositionSurfaceBrush[] Brushes { get; } = new CompositionSurfaceBrush[2];
    public int Current { get; private set; }
    public SizeI Size { get; private set; }

    public SwapSurface(CompositionHost host, SizeI size)
    {
        _host = host;
        Size = size;
        for (int i = 0; i < 2; i++)
        {
            _surfaces[i] = host.GraphicsDevice.CreateDrawingSurface(
                new Windows.Foundation.Size(size.Width, size.Height),
                DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);
            Brushes[i] = host.Compositor.CreateSurfaceBrush(_surfaces[i]);
        }
    }

    /// <summary>Draws into the spare surface and makes it current. Returns the new current index.</summary>
    public int Draw(Action<D2DContext> draw)
    {
        int next = 1 - Current;
        _draws[next] = draw;
        Render(next);
        Current = next;
        return next;
    }

    /// <summary>Draws into the current surface (first content, or a static layer).</summary>
    public void DrawCurrent(Action<D2DContext> draw)
    {
        _draws[Current] = draw;
        Render(Current);
    }

    /// <summary>Redraws both surfaces from their remembered drawings (after device loss or a resize).</summary>
    public void Redraw()
    {
        for (int i = 0; i < 2; i++) if (_draws[i] != null) Render(i);
    }

    public void Resize(SizeI size)
    {
        if (size == Size) return;
        Size = size;
        foreach (var s in _surfaces) s.Resize(new SizeInt32 { Width = size.Width, Height = size.Height });
        Redraw();
    }

    private void Render(int index)
    {
        if (!D2DContext.TryBegin(_surfaces[index], out var dc, out int hr))
        {
            Log.Write($"BeginDraw failed: 0x{hr:X8}");
            _host.HandleDrawError(hr);
            return;
        }
        try
        {
            dc!.Clear();
            _draws[index]!(dc);
        }
        catch (D2DException e)
        {
            Log.Write(e.Message);
            hr = e.HResult2;
        }
        finally
        {
            int end = dc!.End();
            if (end < 0) hr = end;
        }
        if (hr < 0 && !_host.HandleDrawError(hr)) Log.Write($"draw failed: 0x{hr:X8}");
    }
}

/// <summary>A container with two sprites, one per surface of a <see cref="SwapSurface"/>, that cross-fade.</summary>
internal sealed class FadeLayer
{
    private readonly Compositor _c;
    private readonly SpriteVisual[] _sprites = new SpriteVisual[2];
    private int _shown = -1;

    public ContainerVisual Container { get; }

    public FadeLayer(Compositor c, SwapSurface surface)
    {
        _c = c;
        Container = c.CreateContainerVisual();
        for (int i = 0; i < 2; i++)
        {
            _sprites[i] = c.CreateSpriteVisual();
            _sprites[i].Brush = surface.Brushes[i];
            _sprites[i].Opacity = 0;
            Container.Children.InsertAtTop(_sprites[i]);
        }
        SetSize(surface.Size);
    }

    public void SetSize(SizeI size)
    {
        var v = new System.Numerics.Vector2(size.Width, size.Height);
        Container.Size = v;
        foreach (var s in _sprites) s.Size = v;
    }

    /// <summary>Shows surface <paramref name="index"/>: it fades in on top; the old one disappears once covered.</summary>
    public void Show(int index, TimeSpan fade)
    {
        if (index == _shown) return;
        int old = _shown;
        _shown = index;
        var top = _sprites[index];
        Container.Children.Remove(top);
        Container.Children.InsertAtTop(top);
        if (old < 0 || fade <= TimeSpan.Zero)
        {
            top.StopAnimation("Opacity");
            top.Opacity = 1;
            if (old >= 0) _sprites[old].Opacity = 0;
            return;
        }
        var anim = _c.CreateScalarKeyFrameAnimation();
        anim.InsertKeyFrame(0f, 0f);
        anim.InsertKeyFrame(1f, 1f);
        anim.Duration = fade;
        var batch = _c.CreateScopedBatch(CompositionBatchTypes.Animation);
        top.Opacity = 1;
        top.StartAnimation("Opacity", anim);
        batch.End();
        batch.Completed += (_, _) =>
        {
            if (_shown == index) _sprites[old].Opacity = 0;
        };
    }
}
