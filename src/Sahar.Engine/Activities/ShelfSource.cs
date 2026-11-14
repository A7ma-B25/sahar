using System.IO.Compression;
using System.Runtime.InteropServices;
using Sahar.Core.Activities;
using Sahar.Core.Senses;
using Sahar.Core.Settings;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Sahar.Engine.Senses;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;

namespace Sahar.Engine.Activities;

/// <summary>
/// Her shelf (spec 0.13 §2): files dropped on her (paths kept, no copies; she catches them with a squish), new
/// screenshots from Windows' Screenshots folder (once their size settles), dragged out again as copies only, and four
/// small actions on the selection (or all): share (Explorer, with the file picked), copy the text (Windows OCR), shrink
/// (a 1920 px JPEG next to the original) and zip (next to the originals). Missing files drop out by themselves.
/// Logs say counts and kinds, never names.
/// </summary>
internal sealed partial class ShelfSource : IDisposable
{
    private static readonly Guid FolderScreenshots = new("B7BEDE81-DF94-4682-A7D8-57A52620B86F");
    private readonly DispatcherQueue _ui;
    private readonly ActivityHub _hub;
    private readonly Shelf _shelf;
    private readonly Func<SaharSettings> _settings;
    private readonly Action<string> _say;
    private readonly Action _catch;
    private readonly ClipboardSense _clipboard;
    private readonly FileSystemWatcher? _watcher;
    private readonly string? _screenshots;
    private readonly HashSet<int> _selected = [];
    private readonly Dictionary<string, Pixels?> _thumbs = new(StringComparer.OrdinalIgnoreCase);

    public ShelfSource(DispatcherQueue ui, ActivityHub hub, string shelfJson, Func<SaharSettings> settings, Action<string> say, Action caught, ClipboardSense clipboard)
    {
        _ui = ui;
        _hub = hub;
        _shelf = new Shelf(shelfJson);
        _settings = settings;
        _say = say;
        _catch = caught;
        _clipboard = clipboard;
        hub.Pressed += (id, _) => Safe(() => Press(id));
        hub.Card.DragOut += id => Safe(() => DragOut(id));
        _screenshots = ActCom.KnownFolder(FolderScreenshots);
        try
        {
            if (_screenshots is not null && Directory.Exists(_screenshots))
            {
                _watcher = new FileSystemWatcher(_screenshots) { IncludeSubdirectories = false, EnableRaisingEvents = true };
                _watcher.Created += (_, e) => _ = SettleAsync(e.FullPath);
            }
        }
        catch (Exception e) { Log.Write($"shelf: screenshots not watched ({e.GetType().Name})"); }
        Show();   // what was on the shelf last time is still there
    }

    /// <summary>WM_DROPFILES on her input window: the files land on the shelf.</summary>
    public unsafe void Drop(nint drop)
    {
        var paths = new List<string>();
        try
        {
            uint n = ActNative.DragQueryFile(drop, 0xFFFFFFFF, null, 0);
            char* buffer = stackalloc char[1024];
            for (uint i = 0; i < n; i++)
            {
                uint len = ActNative.DragQueryFile(drop, i, buffer, 1024);
                if (len > 0) paths.Add(new string(buffer, 0, (int)len));
            }
        }
        finally { ActNative.DragFinish(drop); }
        if (paths.Count == 0 || !_settings().ActShelf) return;
        _shelf.Add(paths, DateTimeOffset.Now);
        Log.Write($"shelf: {paths.Count} dropped, {_shelf.Items.Count} items");
        _catch();
        Show();
    }

    private async Task SettleAsync(string path)
    {
        try
        {
            if (!ShelfThings.IsImage(path)) return;
            long before = -1;
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(500);
                long now = File.Exists(path) ? new FileInfo(path).Length : 0;
                if (Settle.Stable(before, now)) break;
                before = now;
            }
            _ui.TryEnqueue(() => Safe(() =>
            {
                if (!_settings().ActShelf) return;
                Log.Write("shelf: new screenshot");
                _hub.Moment(new Activity("shot", ActivityKind.Screenshot, ActivityText.Screenshot), Card);
                if (_settings().ShelfScreenshots) { _shelf.Add([path], DateTimeOffset.Now); Show(); }
            }));
        }
        catch (Exception e) { Log.Write($"shelf: screenshot wait failed ({e.GetType().Name})"); }
    }

    /// <summary>0.14 (her brain's shelf tools): whether anything is on her shelf; tidy (drop what is gone); empty it.</summary>
    public bool HasItems => _shelf.Items.Count > 0;

    public bool Tidy()
    {
        int before = _shelf.Items.Count;
        Show();
        return _shelf.Items.Count != before;
    }

    public bool Empty()
    {
        if (_shelf.Items.Count == 0) return false;
        _shelf.Clear();
        _selected.Clear();
        Log.Write("shelf: emptied (asked)");
        Show();
        return true;
    }

    private void Prune() => _shelf.Prune(p => File.Exists(p) || Directory.Exists(p));

    private void Show()
    {
        Prune();
        int n = _shelf.Items.Count;
        if (n == 0 || !_settings().ActShelf) { _hub.Remove("shelf"); return; }
        _hub.Set(new Activity("shelf", ActivityKind.Shelf, ActivityText.Shelf(n), null, "stack"), Card);
        foreach (var item in Tiles()) if (!_thumbs.ContainsKey(item.Path)) _ = ThumbAsync(item.Path);
    }

    /// <summary>The four newest, as the card shows them.</summary>
    private List<ShelfItem> Tiles() => _shelf.Items.Reverse().Take(4).ToList();

    private CardData? Card()
    {
        Prune();
        var tiles = Tiles().Select(i =>
        {
            var (thing, ext) = ShelfThings.Of(i.Path, _screenshots);
            return new ShelfTile(ShelfThings.Label(i.Path), thing, ext, _thumbs.GetValueOrDefault(i.Path));
        }).ToList();
        _selected.RemoveWhere(i => i >= tiles.Count);
        return new CardData(CardKind.Shelf, Shelf: new ShelfCard(tiles, _shelf.Items.Count, new HashSet<int>(_selected)));
    }

    private async Task ThumbAsync(string path)
    {
        _thumbs[path] = null;
        if (!ShelfThings.IsImage(path)) return;
        try
        {
            using var stream = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read).AsTask();
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask();
            uint w = decoder.PixelWidth, h = decoder.PixelHeight, size = 120;
            double k = (double)size / Math.Min(w, h);
            uint sw = (uint)Math.Ceiling(w * k), sh = (uint)Math.Ceiling(h * k);
            var t = new BitmapTransform { ScaledWidth = sw, ScaledHeight = sh, InterpolationMode = BitmapInterpolationMode.Fant,
                Bounds = new BitmapBounds { X = (sw - size) / 2, Y = (sh - size) / 2, Width = size, Height = size } };
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, t,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage).AsTask();
            var px = new Pixels(data.DetachPixelData(), (int)size, (int)size);
            _ui.TryEnqueue(() => Safe(() => { _thumbs[path] = px; _hub.CardChanged(); }));
        }
        catch (Exception e) { Log.Write($"shelf: thumbnail failed ({e.GetType().Name})"); }
    }

    private List<string> Chosen()
    {
        var tiles = Tiles();
        var picked = _selected.Where(i => i < tiles.Count).Select(i => tiles[i].Path).ToList();
        return picked.Count > 0 ? picked : _shelf.Items.Select(i => i.Path).ToList();
    }

    private unsafe void Press(string id)
    {
        if (id.StartsWith("tile-", StringComparison.Ordinal) && int.TryParse(id[5..], out int i))
        {
            if (!_selected.Remove(i)) _selected.Add(i);
            _hub.CardChanged();
            return;
        }
        switch (id)
        {
            case "shelf-clear":
                _shelf.Clear();
                _selected.Clear();
                Log.Write("shelf: emptied");
                _hub.CloseCard();
                Show();
                break;
            case "act-share":
                // Share: Explorer with the file picked (its own Share is one right-click away). Paths go to Explorer only.
                if (Chosen().FirstOrDefault() is { } first)
                {
                    var args = $"/select,\"{first}\"";
                    fixed (char* verb = "open") fixed (char* exe = "explorer.exe") fixed (char* a = args)
                        Native.ShellExecute(0, verb, exe, a, null, 1);
                    Log.Write("shelf: share (explorer)");
                    _hub.CloseCard();
                }
                break;
            case "act-text": _ = OcrAsync(Chosen().Where(ShelfThings.IsImage).ToList()); break;
            case "act-shrink": _ = ShrinkAsync(Chosen().Where(ShelfThings.IsImage).ToList()); break;
            case "act-zip": Zip(Chosen()); break;
        }
    }

    private async Task OcrAsync(List<string> images)
    {
        if (images.Count == 0) { _say("ما في صور لإقرا منها 🙃"); return; }
        try
        {
            var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null) { _say("قراءة النص مو منزّلة بالويندوز"); return; }
            var parts = new List<string>();
            foreach (var path in images)
            {
                using var stream = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read).AsTask();
                var decoder = await BitmapDecoder.CreateAsync(stream).AsTask();
                uint max = Windows.Media.Ocr.OcrEngine.MaxImageDimension, w = decoder.PixelWidth, h = decoder.PixelHeight;
                double k = Math.Min(1, (double)max / Math.Max(w, h));
                using var bmp = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    new BitmapTransform { ScaledWidth = (uint)(w * k), ScaledHeight = (uint)(h * k) }, ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage).AsTask();
                var result = await engine.RecognizeAsync(bmp).AsTask();
                if (!string.IsNullOrWhiteSpace(result.Text)) parts.Add(result.Text);
            }
            bool arabic = Windows.Globalization.ApplicationLanguages.Languages.Any(l => l.StartsWith("ar", StringComparison.OrdinalIgnoreCase));
            bool arabicOcr = Windows.Media.Ocr.OcrEngine.IsLanguageSupported(new Windows.Globalization.Language("ar"));
            _ui.TryEnqueue(() => Safe(() =>
            {
                Log.Write($"shelf: text from {images.Count} images ({parts.Count} with text)");
                if (parts.Count == 0) { _say(arabic && !arabicOcr ? "ما لقيت نص، وقراءة العربي مو منزّلة بالويندوز" : "ما لقيت نص بالصورة"); return; }
                bool ok = _clipboard.PutBack(new ClipItem(ClipKind.Text, string.Join("\n\n", parts), null, 0, 0, DateTimeOffset.Now));
                _say(ok ? (arabic && !arabicOcr ? "نسخت النص (العربي ما بعرف إقراه لسا)" : "نسخت النص 📋") : "ما قدرت انسخ النص");
                _hub.CloseCard();
            }));
        }
        catch (Exception e)
        {
            Log.Write($"shelf: text failed ({e.GetType().Name})");
            _ui.TryEnqueue(() => _say("ما قدرت إقرا الصورة"));
        }
    }

    private async Task ShrinkAsync(List<string> images)
    {
        if (images.Count == 0) { _say("ما في صور لصغّرها"); return; }
        int done = 0;
        foreach (var path in images)
        {
            try
            {
                using var input = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read).AsTask();
                var decoder = await BitmapDecoder.CreateAsync(input).AsTask();
                var (w, h) = ShelfThings.Shrunk((int)decoder.PixelWidth, (int)decoder.PixelHeight);
                using var bmp = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                    new BitmapTransform { ScaledWidth = (uint)w, ScaledHeight = (uint)h, InterpolationMode = BitmapInterpolationMode.Fant },
                    ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage).AsTask();
                var target = Free(ShelfThings.ShrunkPath(path));
                using var output = await FileRandomAccessStream.OpenAsync(target, FileAccessMode.ReadWrite).AsTask();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output).AsTask();
                encoder.SetSoftwareBitmap(bmp);
                await encoder.FlushAsync().AsTask();
                done++;
            }
            catch (Exception e) { Log.Write($"shelf: shrink failed ({e.GetType().Name})"); }
        }
        _ui.TryEnqueue(() => Safe(() =>
        {
            Log.Write($"shelf: shrank {done}");
            _say(done > 0 ? "صغّرتها، حطيتها جنبها 🤏" : "ما قدرت صغّرها");
            _hub.CloseCard();
        }));
    }

    private void Zip(List<string> files)
    {
        files = files.Where(File.Exists).ToList();
        if (files.Count == 0) return;
        try
        {
            var target = Free(ShelfThings.Beside(files[0], ShelfThings.ZipName(files)));
            using (var zip = ZipFile.Open(target, ZipArchiveMode.Create))
                foreach (var f in files) zip.CreateEntryFromFile(f, Path.GetFileName(f), CompressionLevel.Optimal);
            Log.Write($"shelf: zipped {files.Count}");
            _say("ضغطتهن، الملف جنبهن 📦");
        }
        catch (Exception e)
        {
            Log.Write($"shelf: zip failed ({e.GetType().Name})");
            _say("ما قدرت اضغطهن");
        }
        _hub.CloseCard();
    }

    /// <summary>A name that doesn't exist yet ("x.zip", then "x (2).zip"…): nothing of yours is ever overwritten.</summary>
    private static string Free(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var p = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
    }

    // ---- drag out (copies only) ----

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int SHCreateItemFromParsingName(string path, nint bindCtx, Guid* iid, nint* item);

    [LibraryImport("shell32.dll")]
    private static unsafe partial int SHDoDragDrop(nint hwnd, nint dataObject, nint dropSource, uint okEffects, uint* effect);

    [LibraryImport("ole32.dll")]
    private static partial int OleInitialize(nint reserved);

    private static readonly Guid IidShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid BhidDataObject = new("B8C0BD9F-ED24-455C-83E6-D5390C4FE8C4");
    private static readonly Guid IidDataObject = new("0000010E-0000-0000-C000-000000000046");
    private static bool _ole;

    /// <summary>A tile pulled out of the card: the card closes and the file goes where you drop it, as a copy
    /// (DROPEFFECT_COPY only, so a move can never take the original away).</summary>
    private unsafe void DragOut(string id)
    {
        if (!id.StartsWith("tile-", StringComparison.Ordinal) || !int.TryParse(id[5..], out int i)) return;
        var tiles = Tiles();
        if (i >= tiles.Count || !File.Exists(tiles[i].Path)) return;
        _hub.CloseCard();
        if (!_ole) { OleInitialize(0); _ole = true; }
        nint item = 0, data = 0;
        try
        {
            Guid iid = IidShellItem, bhid = BhidDataObject, iidData = IidDataObject;
            if (SHCreateItemFromParsingName(tiles[i].Path, 0, &iid, &item) < 0) return;
            // IShellItem::BindToHandler(IBindCtx*, REFGUID, REFIID, void**)
            if (((delegate* unmanaged[Stdcall]<nint, nint, Guid*, Guid*, nint*, int>)Com.Slot(item, 3))(item, 0, &bhid, &iidData, &data) < 0) return;
            uint effect;
            int hr = SHDoDragDrop(0, data, 0, 1 /* DROPEFFECT_COPY */, &effect);
            Log.Write($"shelf: dragged out ({(hr == 0x40100 ? "dropped" : "cancelled")})");
        }
        finally
        {
            Com.Release(ref data);
            Com.Release(ref item);
        }
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"shelf error: {e.GetType().Name}"); }
    }

    public void Dispose() => _watcher?.Dispose();
}
