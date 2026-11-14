using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Sahar.Core.Mind.Memory;

/// <summary>The box refuses a write rather than lose what is there: <c>locked</c> (the key cannot be opened),
/// <c>forgotten</c> (this box saw a key that «انسي كل شي» removed under it), <c>busy</c> (the other writer holds the
/// mutex). Callers treat it as "not kept".</summary>
public sealed class MindStoreException(string code) : IOException(code)
{
    public string Code { get; } = code;
}

/// <summary>What a read found.</summary>
public enum ReadState { Missing, Ok, KeyLocked, Damaged }

/// <summary>How the store is (the «عنك» page and her bubble say it calmly).</summary>
public enum StoreStatus { Ok, Empty, Locked, Recovered }

/// <summary>Wraps the store's 256-bit key for rest (spec §7.8). <see cref="Unwrap"/> answers null for anything it
/// cannot open (another user, a copied disk, a damaged file), never throws.</summary>
public interface IKeyWrap
{
    byte[] Wrap(byte[] key);
    byte[]? Unwrap(byte[] wrapped);
}

public static class KeyWrap
{
    /// <summary>DPAPI on Windows (the only place the Mind and the settings app run); the plain test wrap elsewhere.</summary>
    public static IKeyWrap Default => OperatingSystem.IsWindows() ? new DpapiKeyWrap() : new TestKeyWrap();
}

/// <summary>DPAPI CurrentUser with the entropy <c>Sahar.Mind.v1</c> (spec §7.8): other Windows users and a copied disk
/// cannot open it; malware running as him can (the «عنك» page says so in one line).</summary>
public sealed class DpapiKeyWrap : IKeyWrap
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Sahar.Mind.v1");
    private const int UiForbidden = 0x1;

    public byte[] Wrap(byte[] key) =>
        Call(key, protect: true) ?? throw new CryptographicException("dpapi-protect");

    public byte[]? Unwrap(byte[] wrapped) => Call(wrapped, protect: false);

    private static byte[]? Call(byte[] data, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI is Windows only");
        var input = Pin(data);
        var entropy = Pin(Entropy);
        var output = default(Blob);
        try
        {
            bool ok = protect
                ? CryptProtectData(ref input, null, ref entropy, 0, 0, UiForbidden, ref output)
                : CryptUnprotectData(ref input, 0, ref entropy, 0, 0, UiForbidden, ref output);
            if (!ok || output.Data == 0) return null;
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            Free(input);
            Free(entropy);
            if (output.Data != 0)
            {
                // The key in clear: wiped before the memory goes back.
                for (int i = 0; i < output.Size; i++) Marshal.WriteByte(output.Data, i, 0);
                LocalFree(output.Data);
            }
        }
    }

    private static Blob Pin(byte[] bytes)
    {
        var p = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
        Marshal.Copy(bytes, 0, p, bytes.Length);
        return new Blob { Size = bytes.Length, Data = p };
    }

    private static void Free(Blob b)
    {
        if (b.Data == 0) return;
        for (int i = 0; i < b.Size; i++) Marshal.WriteByte(b.Data, i, 0);
        Marshal.FreeHGlobal(b.Data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int Size;
        public nint Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob dataIn, string? description, ref Blob entropy, nint reserved, nint prompt, int flags, ref Blob dataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref Blob dataIn, nint description, ref Blob entropy, nint reserved, nint prompt, int flags, ref Blob dataOut);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint mem);
}

/// <summary>Off Windows (unit tests on Linux): the key kept as is behind a marker. It protects nothing and is never
/// used where the Mind runs.</summary>
public sealed class TestKeyWrap : IKeyWrap
{
    private static readonly byte[] Marker = "SAHAR-TEST-WRAP:"u8.ToArray();

    public byte[] Wrap(byte[] key) => [.. Marker, .. key];

    public byte[]? Unwrap(byte[] wrapped) =>
        wrapped.Length == Marker.Length + 32 && wrapped.AsSpan(0, Marker.Length).SequenceEqual(Marker) ? wrapped[Marker.Length..] : null;
}

/// <summary>
/// The Mind's encrypted files (plan 0.14 Task B1; spec §7.8): <c>&lt;name&gt;.enc</c> under one folder, each sealed with
/// AES-256-GCM under a random key that lives only wrapped (<see cref="KeyFile"/>). The file's name is bound to it, so a
/// file renamed or swapped does not open. Two writers (the settings app and the Mind) share a named mutex, like
/// SettingsStore. The key is read under the mutex for every call, so once <see cref="ForgetAll"/> deleted it nothing
/// written before opens again, in any process. Never logs, never throws on reading.
/// </summary>
public sealed class SecretBox(string folder, IKeyWrap wrap, TimeSpan? mutexWait = null) : ISecretBox
{
    public const string KeyFile = "key.dpapi";
    public const string MutexName = @"Local\Sahar.Mind.Store";
    private readonly TimeSpan _wait = mutexWait ?? TimeSpan.FromSeconds(5);
    private bool _sawKey;   // this box opened a key once: a missing key later means a forget, never "make a new one"
    private static readonly byte[] Magic = "SMB1"u8.ToArray();
    private const int NonceSize = 12, TagSize = 16;

    public string Folder { get; } = folder;

    /// <summary>Tests: each entry as <see cref="ForgetAll"/> removes it (the key comes first).</summary>
    public event Action<string>? Deleting;

    public byte[]? Read(string name)
    {
        try
        {
            using var gate = Lock();
            return ReadLocked(name, out _);
        }
        catch (MindStoreException) { return null; }
    }

    /// <summary>Locked: a key is there and cannot be opened. Recovered: a damaged file was kept aside (*.bad).</summary>
    public StoreStatus Status()
    {
        try
        {
            using var _ = Lock();
            var key = Path.Combine(Folder, KeyFile);
            if (File.Exists(key) && wrap.Unwrap(File.ReadAllBytes(key)) is not { Length: 32 }) return StoreStatus.Locked;
            if (Directory.Exists(Folder) && Directory.EnumerateFiles(Folder, "*.bad").Any()) return StoreStatus.Recovered;
            return File.Exists(key) ? StoreStatus.Ok : StoreStatus.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return StoreStatus.Locked; }
    }

    /// <summary>A file that does not open is moved aside as <c>name-&lt;time&gt;.bad</c>, never written over.</summary>
    internal void KeepAside(string name)
    {
        var path = PathOf(name);
        try { File.Move(path, Path.Combine(Folder, $"{name}-{DateTime.UtcNow:yyyyMMddHHmmss}.bad"), overwrite: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new MindStoreException("locked"); }
    }

    public void Write(string name, ReadOnlySpan<byte> data)
    {
        using var _ = Lock();
        WriteLocked(name, data);
    }

    /// <summary>Throws <see cref="MindStoreException"/> rather than write without the lock or over a key it cannot open.</summary>
    public void Delete(string name)
    {
        using var _ = Lock();
        try { File.Delete(PathOf(name)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>«انسي كل شي» (spec §7.8): the key first (crypto-shred: what is left can never open), then everything
    /// in the folder, then the folder. Files another process holds stay, unreadable.</summary>
    public void ForgetAll()
    {
        using var _ = Lock();
        Shred(Folder, Deleting);
        _sawKey = false;   // forgetting is this box's own doing: its next write starts fresh
    }

    /// <summary>The same order for a folder this box does not own (erase-all), with no lock: the caller holds none.</summary>
    public static void Shred(string folder, Action<string>? deleting = null)
    {
        if (!Directory.Exists(folder)) return;
        var key = Path.Combine(folder, KeyFile);
        try
        {
            if (File.Exists(key))
            {
                deleting?.Invoke(KeyFile);
                // Overwrite then delete: the wrapped key does not linger in a free block of a small file.
                try { File.WriteAllBytes(key, new byte[64]); } catch (IOException) { }
                File.Delete(key);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder).ToList())
            {
                deleting?.Invoke(Path.GetFileName(entry));
                try
                {
                    if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                    else File.Delete(entry);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            Directory.Delete(folder, recursive: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ---- under the lock (MindStore reads, changes and writes in one hold) ----

    /// <summary>The cross-process gate: three tries, then <c>busy</c> (never on without it). An abandoned mutex is ours.</summary>
    internal IDisposable Lock()
    {
        for (int i = 0; i < 3; i++)
        {
            var m = new Mutex(false, MutexName);
            bool owned;
            try { owned = m.WaitOne(_wait); }
            catch (AbandonedMutexException) { owned = true; }
            if (owned) return new Gate(m);
            m.Dispose();
        }
        throw new MindStoreException("busy");
    }

    internal byte[]? ReadLocked(string name, out ReadState state)
    {
        state = ReadState.Missing;
        var path = PathOf(name);
        byte[] blob;
        try
        {
            if (!File.Exists(path)) return null;
            blob = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { state = ReadState.KeyLocked; return null; }
        state = ReadState.Damaged;
        if (blob.Length < Magic.Length + NonceSize + TagSize || !blob.AsSpan(0, Magic.Length).SequenceEqual(Magic)) return null;
        var key = LoadKey(create: false);
        if (key is null) { state = File.Exists(Path.Combine(Folder, KeyFile)) ? ReadState.KeyLocked : ReadState.Damaged; return null; }
        try
        {
            var nonce = blob.AsSpan(Magic.Length, NonceSize);
            var tag = blob.AsSpan(Magic.Length + NonceSize, TagSize);
            var cipher = blob.AsSpan(Magic.Length + NonceSize + TagSize);
            var plain = new byte[cipher.Length];
            using var gcm = new AesGcm(key, TagSize);
            gcm.Decrypt(nonce, cipher, tag, plain, Aad(name));
            state = ReadState.Ok;
            return plain;
        }
        catch (CryptographicException) { return null; }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal void WriteLocked(string name, ReadOnlySpan<byte> data)
    {
        var path = PathOf(name);
        var key = LoadKey(create: true)!;
        try
        {
            var blob = new byte[Magic.Length + NonceSize + TagSize + data.Length];
            Magic.CopyTo(blob, 0);
            var nonce = blob.AsSpan(Magic.Length, NonceSize);
            RandomNumberGenerator.Fill(nonce);
            using (var gcm = new AesGcm(key, TagSize))
                gcm.Encrypt(nonce, data, blob.AsSpan(Magic.Length + NonceSize + TagSize), blob.AsSpan(Magic.Length + NonceSize, TagSize), Aad(name));
            Directory.CreateDirectory(Folder);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, blob);
            File.Move(tmp, path, overwrite: true);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    /// <summary>The key, unwrapped. A new one ONLY when there is none and this box never saw one (review B1, B2): a key
    /// that cannot be opened is never replaced (<c>locked</c>), and a key that vanished under this box (a forget while
    /// it worked) is never recreated (<c>forgotten</c>).</summary>
    private byte[]? LoadKey(bool create)
    {
        var path = Path.Combine(Folder, KeyFile);
        if (File.Exists(path))
        {
            byte[]? key = null;
            try { key = wrap.Unwrap(File.ReadAllBytes(path)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            if (key is { Length: 32 }) { _sawKey = true; return key; }
            if (!create) return null;
            throw new MindStoreException("locked");
        }
        if (!create) return null;
        if (_sawKey) throw new MindStoreException("forgotten");
        var fresh = RandomNumberGenerator.GetBytes(32);
        Directory.CreateDirectory(Folder);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, wrap.Wrap(fresh));
        File.Move(tmp, path, overwrite: true);
        _sawKey = true;
        return fresh;
    }

    private string PathOf(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 64 || !name.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_'))
            throw new ArgumentException("a box name is lower-case letters, digits, '-' or '_'", nameof(name));
        return Path.Combine(Folder, name + ".enc");
    }

    private static byte[] Aad(string name) => Encoding.UTF8.GetBytes("sahar-mind:" + name);

    private sealed class Gate(Mutex mutex) : IDisposable
    {
        public void Dispose()
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
