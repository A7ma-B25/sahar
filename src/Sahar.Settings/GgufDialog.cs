using System.Runtime.InteropServices;

namespace Sahar.Settings;

/// <summary>Windows' open-file dialog for a .gguf model (GetOpenFileNameW; no WinForms). Never logs the path.</summary>
internal static unsafe partial class GgufDialog
{
    [StructLayout(LayoutKind.Sequential)]
    private struct OpenFileName
    {
        public int StructSize;
        public nint Owner, Instance;
        public char* Filter, CustomFilter;
        public int MaxCustFilter, FilterIndex;
        public char* File;
        public int MaxFile;
        public char* FileTitle;
        public int MaxFileTitle;
        public char* InitialDir, Title;
        public int Flags;
        public short FileOffset, FileExtension;
        public char* DefExt;
        public nint CustData, Hook;
        public char* TemplateName;
        public nint Reserved1;
        public int Reserved2, FlagsEx;
    }

    [LibraryImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetOpenFileName(OpenFileName* ofn);

    public static string? Pick(nint owner)
    {
        const int OFN_FILEMUSTEXIST = 0x1000, OFN_PATHMUSTEXIST = 0x800, OFN_NOCHANGEDIR = 0x8, OFN_EXPLORER = 0x80000;
        var filter = "GGUF (*.gguf)\0*.gguf\0\0";
        var buffer = new char[1024];
        fixed (char* f = filter)
        fixed (char* b = buffer)
        fixed (char* t = "اختاري ملف النموذج")
        {
            var ofn = new OpenFileName
            {
                StructSize = sizeof(OpenFileName), Owner = owner, Filter = f, FilterIndex = 1, File = b, MaxFile = buffer.Length,
                Title = t, Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_EXPLORER,
            };
            if (!GetOpenFileName(&ofn)) return null;
            return new string(b);
        }
    }
}
