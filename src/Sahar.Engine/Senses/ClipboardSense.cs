using System.Runtime.InteropServices;
using Sahar.Core.Senses;
using Sahar.Engine.Diagnostics;

namespace Sahar.Engine.Senses;

/// <summary>
/// Reads what you copied (spec §7.2) when the clipboard changes, by the rules in <see cref="ClipboardRules"/>:
/// nothing marked "don't monitor", nothing from password managers. Text and images go to the history.
/// It also puts a history item back on the clipboard (and ignores that change).
/// </summary>
internal sealed unsafe class ClipboardSense
{
    private const uint CF_UNICODETEXT = 13, CF_DIB = 8, GMEM_MOVEABLE = 2;

    private readonly nint _owner;
    private readonly ClipboardHistory _history;
    private readonly uint _historyFormat;
    private uint _ourSequence;

    /// <summary>Something new was kept.</summary>
    public event Action? Kept;

    public ClipboardSense(nint ownerWindow, ClipboardHistory history)
    {
        _owner = ownerWindow;
        _history = history;
        fixed (char* n = "CanIncludeInClipboardHistory") _historyFormat = Native.RegisterClipboardFormat(n);
    }

    public ClipboardHistory History => _history;

    public void Changed()
    {
        try
        {
            if (Native.GetClipboardSequenceNumber() == _ourSequence) return;   // our own put-back
            if (!Open()) return;
            string? copiedText = null, owner = null;
            (byte[] Bytes, int W, int H)? copiedImage = null;
            // Only read while the clipboard is open; keep it open as briefly as possible (other apps wait for it).
            try
            {
                var names = new List<string>();
                bool text = false, dib = false;
                uint? canInclude = null;
                char* buffer = stackalloc char[128];
                for (uint f = Native.EnumClipboardFormats(0); f != 0; f = Native.EnumClipboardFormats(f))
                {
                    if (f == CF_UNICODETEXT) text = true;
                    else if (f == CF_DIB) dib = true;
                    else if (f >= 0xC000)
                    {
                        int n = Native.GetClipboardFormatName(f, buffer, 128);
                        if (n > 0) names.Add(new string(buffer, 0, n));
                        if (f == _historyFormat && Native.GetClipboardData(f) is var h && h != 0 && Native.GlobalLock(h) is var p && p != 0)
                        {
                            canInclude = *(uint*)p;
                            Native.GlobalUnlock(h);
                        }
                    }
                }
                owner = Native.WindowProcessPath(Native.GetClipboardOwner());
                if (!ClipboardRules.ShouldKeep(names, canInclude, owner)) return;
                if (text) copiedText = ReadText();
                else if (dib) copiedImage = ReadDib();
            }
            finally { Native.CloseClipboard(); }
            var now = DateTimeOffset.Now;
            bool kept = copiedText is { } t ? _history.AddText(t, now, secret: ClipboardRules.KeepInMemoryOnly(owner ?? "", t))
                : copiedImage is { } d && _history.AddImage(d.Bytes, d.W, d.H, now);
            if (kept) Kept?.Invoke();
        }
        catch (Exception e) { Log.Write($"clipboard error: {e.GetType().Name}"); }
    }

    private bool Open()
    {
        for (int i = 0; i < 5; i++)
        {
            if (Native.OpenClipboard(_owner) != 0) return true;
            Thread.Sleep(20);
        }
        return false;
    }

    private static string? ReadText()
    {
        nint h = Native.GetClipboardData(CF_UNICODETEXT);
        if (h == 0) return null;
        nint p = Native.GlobalLock(h);
        if (p == 0) return null;
        try
        {
            int max = (int)Math.Min((ulong)Native.GlobalSize(h) / 2, ClipboardHistory.MaxTextChars + 1UL);
            var span = new ReadOnlySpan<char>((void*)p, max);
            int end = span.IndexOf('\0');
            return new string(end >= 0 ? span[..end] : span);
        }
        finally { Native.GlobalUnlock(h); }
    }

    private static (byte[] Bytes, int W, int H)? ReadDib()
    {
        nint h = Native.GetClipboardData(CF_DIB);
        if (h == 0) return null;
        nint p = Native.GlobalLock(h);
        if (p == 0) return null;
        try
        {
            ulong size = Native.GlobalSize(h);
            if (size < 40 || size > (ulong)ClipboardHistory.MaxImageBytes) return null;
            var bytes = new byte[size];
            Marshal.Copy(p, bytes, 0, bytes.Length);
            return (bytes, Math.Abs(BitConverter.ToInt32(bytes, 4)), Math.Abs(BitConverter.ToInt32(bytes, 8)));
        }
        finally { Native.GlobalUnlock(h); }
    }

    /// <summary>Puts a history item back on the clipboard.</summary>
    public bool PutBack(ClipItem item)
    {
        try
        {
            byte[] data;
            uint format;
            if (item.Kind == ClipKind.Image && item.ImageFile is { } file && File.Exists(file))
            {
                var all = File.ReadAllBytes(file);
                data = all[14..];   // drop BITMAPFILEHEADER
                format = CF_DIB;
            }
            else if (item.Text is { } text)
            {
                data = System.Text.Encoding.Unicode.GetBytes(text + "\0");
                format = CF_UNICODETEXT;
            }
            else return false;
            if (!Open()) return false;
            try
            {
                Native.EmptyClipboard();
                nint mem = Native.GlobalAlloc(GMEM_MOVEABLE, (nuint)data.Length);
                nint p = Native.GlobalLock(mem);
                Marshal.Copy(data, 0, p, data.Length);
                Native.GlobalUnlock(mem);
                if (Native.SetClipboardData(format, mem) == 0) { Native.GlobalFree(mem); return false; }
            }
            finally { Native.CloseClipboard(); }
            _ourSequence = Native.GetClipboardSequenceNumber();
            return true;
        }
        catch (Exception e)
        {
            Log.Write($"clipboard put-back error: {e.GetType().Name}");
            return false;
        }
    }
}
