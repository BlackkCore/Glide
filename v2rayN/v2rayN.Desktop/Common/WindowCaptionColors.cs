using System.Runtime.InteropServices;

namespace v2rayN.Desktop.Common;

/// <summary>
/// Windows 11: paints a window's native title bar with the app surface color,
/// so it blends with the sidebar and follows the light/dark theme.
/// </summary>
internal static class WindowCaptionColors
{
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Attach(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }
        window.Opened += (_, _) => Apply(window);
        window.ActualThemeVariantChanged += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        SetColor(window, hwnd, DwmwaBorderColor, "AppBorderBrush");
        SetColor(window, hwnd, DwmwaCaptionColor, "AppSurfaceBrush");
        SetColor(window, hwnd, DwmwaTextColor, "AppTextBrush");
    }

    private static void SetColor(Window window, IntPtr hwnd, int attribute, string resourceKey)
    {
        if (window.TryFindResource(resourceKey, window.ActualThemeVariant, out var res) && res is ISolidColorBrush brush)
        {
            var c = brush.Color;
            var colorRef = c.R | (c.G << 8) | (c.B << 16);
            _ = DwmSetWindowAttribute(hwnd, attribute, ref colorRef, sizeof(int));
        }
    }
}
