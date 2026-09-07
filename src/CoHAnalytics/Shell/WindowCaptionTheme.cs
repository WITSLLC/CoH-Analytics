using System.Runtime.InteropServices;

namespace CoHAnalytics.Shell;

internal static class WindowCaptionTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaCaptionButtonBounds = 5;
    private const int DwmwaCaptionColor = 35;

    // DWM COLORREF stores red in the low byte: RGB #6B2430 becomes 0x0030246B.
    private const int ApplicationChromeColorRef = 0x0030246B;

    public static void Apply(nint windowHandle)
    {
        var useDarkCaptionControls = 1;
        var captionColor = ApplicationChromeColorRef;

        _ = DwmSetWindowAttribute(
            windowHandle,
            DwmwaUseImmersiveDarkMode,
            ref useDarkCaptionControls,
            sizeof(int));
        _ = DwmSetWindowAttribute(
            windowHandle,
            DwmwaCaptionColor,
            ref captionColor,
            sizeof(int));
    }

    public static bool TryGetCaptionButtonAreaWidth(nint windowHandle, out double width)
    {
        width = 0;
        if (DwmGetWindowAttribute(
                windowHandle,
                DwmwaCaptionButtonBounds,
                out var bounds,
                Marshal.SizeOf<WindowBounds>()) != 0
            || bounds.Right <= bounds.Left)
        {
            return false;
        }

        var dpiScale = GetDpiForWindow(windowHandle) / 96d;
        width = (bounds.Right - bounds.Left) / dpiScale;
        return width > 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint windowHandle,
        int attribute,
        out WindowBounds attributeValue,
        int attributeSize);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowBounds
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
