using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Base;

public class WindowBase<TViewModel> : ReactiveWindow<TViewModel> where TViewModel : class
{
    public WindowBase()
    {
        WindowCaptionColors.Attach(this);
        // Persian and other right-to-left languages get a mirrored layout: navigation on the right,
        // labels before their controls reading right to left.
        if (System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft)
        {
            FlowDirection = FlowDirection.RightToLeft;
        }
        Loaded += OnLoaded;
        Opened += (_, _) =>
        {
            if (AnimateContentOnOpen)
            {
                AppMotion.PlayEntrance(Content as Visual, 8, 240);
            }
        };
        Loaded += (s, e) =>
        {
            if (Owner != null && !ShowInTaskbar)
            {
                CanMinimize = false;
            }
        };
    }

    /// <summary>Dialogs fade and slide in when they open; the main window staggers its cards instead.</summary>
    protected virtual bool AnimateContentOnOpen => true;

    private void ReactiveWindowBase_Closed(object? sender, EventArgs e)
    {
        throw new NotImplementedException();
    }

    protected virtual void OnLoaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            var sizeItem = ConfigHandler.GetWindowSizeItem(AppManager.Instance.Config, GetType().Name);
            // Glide (U-03): the first open (nothing saved yet) is fitted to the screen too: the main
            // window's 1360x820 doesn't fit a 1080p screen at 150 %.
            var (wantWidth, wantHeight) = sizeItem is null ? (Width, Height) : (sizeItem.Width, sizeItem.Height);
            if (double.IsNaN(wantWidth) || double.IsNaN(wantHeight))
            {
                return;
            }

            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null)
            {
                return;
            }
            var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
            var workingArea = screen.WorkingArea;

            var width = Math.Min(wantWidth, workingArea.Width / scaling);
            var height = Math.Min(wantHeight, workingArea.Height / scaling);
            if (sizeItem is null && width >= wantWidth && height >= wantHeight)
            {
                return; // Fits: keep the window's own size and startup position.
            }

            // A minimum size larger than the screen would push the window off it again.
            MinWidth = Math.Min(MinWidth, width);
            MinHeight = Math.Min(MinHeight, height);
            Width = width;
            Height = height;

            var frameDiff = (FrameSize ?? ClientSize) - ClientSize;
            var totalWidth = (width + frameDiff.Width) * scaling;
            var totalHeight = (height + frameDiff.Height) * scaling;

            var x = workingArea.X + ((workingArea.Width - totalWidth) / 2);
            var y = workingArea.Y + ((workingArea.Height - totalHeight) / 2);
            Position = new PixelPoint((int)x, (int)y);
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try
        {
            ConfigHandler.SaveWindowSizeItem(AppManager.Instance.Config, GetType().Name, Width, Height);
        }
        catch { }
    }
}
