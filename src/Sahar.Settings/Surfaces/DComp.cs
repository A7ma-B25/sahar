using System.Runtime.InteropServices;

namespace Sahar.Settings.Surfaces;

/// <summary>
/// The smallest DirectComposition the surface windows need: a device, a target on a window, one visual that WebView2
/// draws into (CoreWebView2CompositionController.RootVisualTarget). Per-pixel transparent: the card's rounded corners
/// and CSS shadow are drawn as designed, with nothing behind them.
/// </summary>
internal static partial class DComp
{
    [ComImport, Guid("C37EA93A-E7AA-450D-B16F-9746CB0407F3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDCompositionDevice
    {
        void Commit();
        void WaitForCommitCompletion();
        void GetFrameStatistics(IntPtr statistics);
        [return: MarshalAs(UnmanagedType.Interface)]
        IDCompositionTarget CreateTargetForHwnd(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool topmost);
        [return: MarshalAs(UnmanagedType.Interface)]
        IDCompositionVisual CreateVisual();
    }

    [ComImport, Guid("EACDD04C-117E-4E17-88F4-D1B12B0E3D89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDCompositionTarget
    {
        void SetRoot([MarshalAs(UnmanagedType.Interface)] IDCompositionVisual visual);
    }

    [ComImport, Guid("4D93059D-097B-4651-9A60-F0F25116E2F3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDCompositionVisual { }

    [DllImport("dcomp.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void DCompositionCreateDevice2(IntPtr renderingDevice, [In] ref Guid iid,
        [MarshalAs(UnmanagedType.IUnknown)] out object device);

    public static IDCompositionDevice CreateDevice()
    {
        var iid = typeof(IDCompositionDevice).GUID;
        DCompositionCreateDevice2(IntPtr.Zero, ref iid, out var device);
        return (IDCompositionDevice)device;
    }
}
