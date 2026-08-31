using System.Runtime.InteropServices;

namespace CodexSelfCheck;

internal static class NativeMethods
{
    internal const uint CopyFileAllowDecryptedDestination = 0x00000008;
    private const uint WmSettingChange = 0x001A;
    private static readonly nint HwndBroadcast = new(0xffff);

    internal enum CopyProgressResult : uint
    {
        Continue = 0,
        Cancel = 1,
        Stop = 2,
        Quiet = 3
    }

    internal enum CopyProgressCallbackReason : uint
    {
        ChunkFinished = 0,
        StreamSwitch = 1
    }

    internal delegate CopyProgressResult CopyProgressRoutine(
        long totalFileSize,
        long totalBytesTransferred,
        long streamSize,
        long streamBytesTransferred,
        uint streamNumber,
        CopyProgressCallbackReason callbackReason,
        nint sourceFile,
        nint destinationFile,
        nint data);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CopyFileEx(
        string existingFileName,
        string newFileName,
        CopyProgressRoutine? progressRoutine,
        nint data,
        ref int cancel,
        uint copyFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint SendMessageTimeout(
        nint window,
        uint message,
        nint wParam,
        string lParam,
        uint flags,
        uint timeout,
        out nint result);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool AttachConsole(uint processId);

    internal static void BroadcastEnvironmentChange()
    {
        _ = SendMessageTimeout(HwndBroadcast, WmSettingChange, nint.Zero, "Environment", 0x0002, 5000, out _);
    }

    internal static string LongPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith("\\\\?\\", StringComparison.Ordinal))
        {
            return full;
        }

        if (full.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return "\\\\?\\UNC\\" + full[2..];
        }

        return "\\\\?\\" + full;
    }
}
