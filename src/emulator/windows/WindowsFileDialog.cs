using System.Runtime.InteropServices;
using System.Runtime.Versioning;

/// <summary>
/// Native "Open File" dialog for Windows, using the classic comdlg32
/// GetOpenFileNameW common dialog. This avoids taking a dependency on
/// System.Windows.Forms / WPF just for a file picker.
///
/// Only ever call TryOpen() behind an OperatingSystem.IsWindows() check -
/// on other platforms comdlg32.dll doesn't exist, so the call would throw.
/// The P/Invoke declaration itself is harmless to have in a cross-platform
/// build; it's only resolved at the point it's actually invoked.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsFileDialog
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAME ofn);

    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_NOCHANGEDIR = 0x00000008; // don't let the dialog change the process CWD

    private const int MaxPathChars = 4096; // generous, handles long paths

    /// <summary>
    /// Shows the native Windows "Open" dialog, filtered to .nes ROMs by
    /// default. This call blocks (it pumps its own message loop) until the
    /// user picks a file or cancels.
    /// </summary>
    public static bool TryOpen(out string filePath, string initialDir = null,
        string filter = "NES ROMs (*.nes)\0*.nes\0All Files (*.*)\0*.*\0\0")
    {
        filePath = "";

        IntPtr fileBuffer = Marshal.AllocHGlobal(MaxPathChars * sizeof(char));
        try
        {
            Marshal.WriteInt16(fileBuffer, 0, 0); // start with an empty string

            var ofn = new OPENFILENAME
            {
                lStructSize = Marshal.SizeOf<OPENFILENAME>(),
                hwndOwner = IntPtr.Zero,
                lpstrFilter = filter,
                lpstrFile = fileBuffer,
                nMaxFile = MaxPathChars,
                lpstrInitialDir = string.IsNullOrEmpty(initialDir) ? null : initialDir,
                lpstrTitle = "Open ROM",
                Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR
            };

            if (!GetOpenFileNameW(ref ofn))
                return false; // user cancelled, or dialog failed

            filePath = Marshal.PtrToStringUni(fileBuffer) ?? "";
            return !string.IsNullOrEmpty(filePath);
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuffer);
        }
    }
}