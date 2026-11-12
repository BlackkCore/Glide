using Avalonia.Input.Platform;

namespace v2rayN.Desktop.Common;

internal class AvaUtils
{
    public static async Task<string?> GetClipboardData(Window owner)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
            if (clipboard == null)
            {
                return null;
            }

            return await clipboard.TryGetTextAsync();
        }
        catch
        {
            return null;
        }
    }

    public static async Task SetClipboardData(Visual? visual, string strData)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(visual)?.Clipboard;
            if (clipboard == null)
            {
                return;
            }

            await clipboard.SetTextAsync(strData);
        }
        catch
        {
        }
    }

    public static WindowIcon GetAppIcon(ESysProxyType sysProxyType)
    {
        var index = (int)sysProxyType + 1;
        var fileName = Utils.GetPath($"NotifyIcon{index}.ico");
        if (File.Exists(fileName))
        {
            return new(fileName);
        }

        var uri = new Uri(Path.Combine(Global.AvaAssets, $"NotifyIcon{index}.ico"));
        using var bitmap = new Bitmap(AssetLoader.Open(uri));
        return new(bitmap);
    }

    /// <summary>
    /// Glide: the same icon with a coloured dot in the corner (a connection problem), so the tray shows
    /// trouble without opening the window. Null dot: the plain icon.
    /// </summary>
    public static WindowIcon GetAppIcon(ESysProxyType sysProxyType, Color? dot)
    {
        if (dot is not { } color)
        {
            return GetAppIcon(sysProxyType);
        }
        try
        {
            var index = (int)sysProxyType + 1;
            var fileName = Utils.GetPath($"NotifyIcon{index}.ico");
            using var source = File.Exists(fileName)
                ? new Bitmap(fileName)
                : new Bitmap(AssetLoader.Open(new Uri(Path.Combine(Global.AvaAssets, $"NotifyIcon{index}.ico"))));
            var target = new RenderTargetBitmap(new PixelSize(64, 64));
            using (var context = target.CreateDrawingContext())
            {
                context.DrawImage(source, new Rect(0, 0, 64, 64));
                context.DrawEllipse(Brushes.White, null, new Point(48, 48), 16, 16);
                context.DrawEllipse(new SolidColorBrush(color), null, new Point(48, 48), 12, 12);
            }
            return new(target);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("GetAppIcon", ex);
            return GetAppIcon(sysProxyType);
        }
    }
}
