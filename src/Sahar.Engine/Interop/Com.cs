using System.Runtime.InteropServices;
using WinRT;

namespace Sahar.Engine;

/// <summary>Raw COM helpers: calls go through the vtable with function pointers (AOT-safe, no marshalling).</summary>
internal static unsafe class Com
{
    /// <summary>The function pointer at <paramref name="index"/> in the object's vtable.</summary>
    public static void* Slot(nint obj, int index) => (*(void***)obj)[index];

    public static nint QueryInterface(nint unknown, Guid iid)
    {
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out nint p));
        return p;
    }

    /// <summary>The IUnknown behind a projected WinRT object (not AddRef'd).</summary>
    public static nint Unknown(object winrt) => ((IWinRTObject)winrt).NativeObject.ThisPtr;

    public static void Release(ref nint p)
    {
        if (p != 0) Marshal.Release(p);
        p = 0;
    }
}
