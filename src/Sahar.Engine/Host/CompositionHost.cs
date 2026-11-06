using System.Runtime.InteropServices;
using Sahar.Engine.Diagnostics;
using Windows.System;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace Sahar.Engine.Host;

/// <summary>
/// The compositor, the overlay's visual tree and the Direct2D-backed graphics device that drawing surfaces
/// come from. When the GPU goes away (sleep/resume, driver reset, RDP) the device is recreated and
/// <see cref="DeviceLost"/> fires so every surface is redrawn.
/// </summary>
internal sealed unsafe class CompositionHost : IDisposable
{
    private static readonly Guid IID_ICompositorDesktopInterop = new("29E691FA-4567-4DCA-B319-D0F207EB6807");
    private static readonly Guid IID_ICompositorInterop = new("25297D5C-3AD4-4C9C-B5CF-E36A38512330");
    private static readonly Guid IID_ICompositionGraphicsDeviceInterop = new("A116FF71-F8BF-4C8A-9C98-70779A32A9C8");
    private static readonly Guid IID_IDXGIDevice = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    public const int D2DERR_RECREATE_TARGET = unchecked((int)0x8899000C);
    public const int DXGI_ERROR_DEVICE_REMOVED = unchecked((int)0x887A0005);
    public const int DXGI_ERROR_DEVICE_RESET = unchecked((int)0x887A0007);

    private static nint _dispatcherQueueController; // kept for the life of the process

    private readonly DesktopWindowTarget _target;
    private nint _d2dDevice;

    public Compositor Compositor { get; }
    public ContainerVisual Root { get; }
    public CompositionGraphicsDevice GraphicsDevice { get; }
    /// <summary>"hardware" or "warp" (software, e.g. CI runners without a GPU).</summary>
    public string Driver { get; private set; } = "";

    /// <summary>The rendering device was replaced; every drawing surface must be redrawn.</summary>
    public event Action? DeviceLost;

    public CompositionHost(nint overlayHwnd)
    {
        EnsureDispatcherQueue();
        Compositor = new Compositor();
        _target = CreateTarget(Compositor, overlayHwnd);
        Root = Compositor.CreateContainerVisual();
        Root.RelativeSizeAdjustment = System.Numerics.Vector2.One;
        _target.Root = Root;

        _d2dDevice = CreateD2DDevice(out var driver);
        Driver = driver;
        GraphicsDevice = CreateGraphicsDevice(Compositor, _d2dDevice);
        var ui = DispatcherQueue.GetForCurrentThread();
        GraphicsDevice.RenderingDeviceReplaced += (_, _) =>
        {
            // May be raised off the UI thread; surfaces are redrawn on it.
            Log.Write("rendering device replaced");
            ui.TryEnqueue(() => DeviceLost?.Invoke());
        };
        Log.Write($"composition ready ({Driver})");
    }

    /// <summary>The UI thread's dispatcher queue (created on first use).</summary>
    public static DispatcherQueue EnsureDispatcherQueue()
    {
        if (_dispatcherQueueController == 0)
        {
            var options = new DispatcherQueueOptions
            {
                DwSize = sizeof(DispatcherQueueOptions),
                ThreadType = 2,    // DQTYPE_THREAD_CURRENT
                ApartmentType = 0, // DQTAT_COM_NONE: Main is [STAThread]
            };
            nint controller;
            Marshal.ThrowExceptionForHR(Native.CreateDispatcherQueueController(options, &controller));
            _dispatcherQueueController = controller;
        }
        return DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>Call with the HRESULT of a failed draw; recreates the device when the GPU was lost.</summary>
    public bool HandleDrawError(int hr)
    {
        if (hr is not (D2DERR_RECREATE_TARGET or DXGI_ERROR_DEVICE_REMOVED or DXGI_ERROR_DEVICE_RESET)) return false;
        Log.Write($"device lost (0x{hr:X8}), recreating");
        RecreateDevice();
        return true;
    }

    /// <summary>Creates a fresh D2D device and hands it to the graphics device (raises <see cref="DeviceLost"/>).</summary>
    public void RecreateDevice()
    {
        var fresh = CreateD2DDevice(out var driver);
        nint interop = Com.QueryInterface(Com.Unknown(GraphicsDevice), IID_ICompositionGraphicsDeviceInterop);
        try
        {
            // ICompositionGraphicsDeviceInterop::SetRenderingDevice(IUnknown*)
            var set = (delegate* unmanaged[Stdcall]<nint, nint, int>)Com.Slot(interop, 4);
            Marshal.ThrowExceptionForHR(set(interop, fresh));
        }
        finally
        {
            Marshal.Release(interop);
        }
        Com.Release(ref _d2dDevice);
        _d2dDevice = fresh;
        Driver = driver;
    }

    private static DesktopWindowTarget CreateTarget(Compositor compositor, nint hwnd)
    {
        nint interop = Com.QueryInterface(Com.Unknown(compositor), IID_ICompositorDesktopInterop);
        try
        {
            // ICompositorDesktopInterop::CreateDesktopWindowTarget(HWND, BOOL isTopmost, IDesktopWindowTarget**)
            nint target;
            var create = (delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)Com.Slot(interop, 3);
            Marshal.ThrowExceptionForHR(create(interop, hwnd, 1, &target));
            // FromAbi takes its own reference; ours lives as long as the process (one window target).
            return DesktopWindowTarget.FromAbi(target);
        }
        finally
        {
            Marshal.Release(interop);
        }
    }

    private static CompositionGraphicsDevice CreateGraphicsDevice(Compositor compositor, nint d2dDevice)
    {
        nint interop = Com.QueryInterface(Com.Unknown(compositor), IID_ICompositorInterop);
        try
        {
            // ICompositorInterop::CreateGraphicsDevice(IUnknown* renderingDevice, ICompositionGraphicsDevice**)
            nint device;
            var create = (delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Com.Slot(interop, 5);
            Marshal.ThrowExceptionForHR(create(interop, d2dDevice, &device));
            return CompositionGraphicsDevice.FromAbi(device);
        }
        finally
        {
            Marshal.Release(interop);
        }
    }

    /// <summary>D3D11 (hardware, else WARP) → IDXGIDevice → ID2D1Device. The caller owns the result.</summary>
    private static nint CreateD2DDevice(out string driver)
    {
        const int D3D_DRIVER_TYPE_HARDWARE = 1, D3D_DRIVER_TYPE_WARP = 5;
        const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20, D3D11_SDK_VERSION = 7;

        nint d3d = 0;
        int hr = 0;
        driver = "hardware";
        foreach (var (type, name) in new[] { (D3D_DRIVER_TYPE_HARDWARE, "hardware"), (D3D_DRIVER_TYPE_WARP, "warp") })
        {
            hr = Native.D3D11CreateDevice(0, type, 0, D3D11_CREATE_DEVICE_BGRA_SUPPORT, null, 0, D3D11_SDK_VERSION, &d3d, null, null);
            if (hr >= 0) { driver = name; break; }
            Log.Write($"D3D11CreateDevice({name}) failed: 0x{hr:X8}");
        }
        Marshal.ThrowExceptionForHR(hr);

        nint dxgi = 0;
        try
        {
            dxgi = Com.QueryInterface(d3d, IID_IDXGIDevice);
            nint d2d;
            Marshal.ThrowExceptionForHR(Native.D2D1CreateDevice(dxgi, 0, &d2d));
            return d2d;
        }
        finally
        {
            Com.Release(ref dxgi);
            Com.Release(ref d3d);
        }
    }

    public void Dispose()
    {
        Root.Dispose();
        _target.Dispose();
        GraphicsDevice.Dispose();
        Compositor.Dispose();
        Com.Release(ref _d2dDevice);
    }
}
