using System.Runtime.InteropServices;

namespace Sahar.Settings;

/// <summary>Windows' open-file dialog for a .gguf model (0.19: or an MCP server's .exe) (GetOpenFileNameW; no WinForms). Never logs the path.</summary>
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

    public static string? Pick(nint owner) => Pick(owner, "GGUF (*.gguf)\0*.gguf\0\0", "اختاري ملف النموذج");

    /// <summary>0.19 «الأدوات»: an MCP server program (.exe only).</summary>
    public static string? PickExe(nint owner) => Pick(owner, "Program (*.exe)\0*.exe\0\0", "اختار البرنامج");

    private static string? Pick(nint owner, string filter, string title)
    {
        const int OFN_FILEMUSTEXIST = 0x1000, OFN_PATHMUSTEXIST = 0x800, OFN_NOCHANGEDIR = 0x8, OFN_EXPLORER = 0x80000;
        var buffer = new char[1024];
        fixed (char* f = filter)
        fixed (char* b = buffer)
        fixed (char* t = title)
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
