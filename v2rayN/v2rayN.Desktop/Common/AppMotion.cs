using System.Runtime.InteropServices;

namespace v2rayN.Desktop.Common;

/// <summary>
/// Follows the Windows "Animation effects" setting (Settings → Accessibility → Visual effects).
/// When it is off, windows get the <c>reduceMotion</c> class so AppStyles can drop transitions,
/// and code-driven animations jump straight to their end state.
/// </summary>
public static partial class AppMotion
{
    public const string ReduceMotionClass = "reduceMotion";

    private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

    private static readonly Lazy<bool> _enabled = new(ReadSystemSetting);

    public static bool Enabled => _enabled.Value;

    public static void Apply(StyledElement element)
    {
        element.Classes.Set(ReduceMotionClass, !Enabled);
    }

    /// <summary>
    /// Fades <paramref name="target"/> in while it slides up by <paramref name="offsetY"/> pixels.
    /// Used for window content, settings pages and the main window's cards (with a delay for the stagger).
    /// The target keeps its own opacity and position afterwards; nothing happens when animations are off.
    /// </summary>
    public static void PlayEntrance(Visual? target, double offsetY = 10, double durationMs = 260, double delayMs = 0)
    {
        if (!Enabled || target is null)
        {
            return;
        }
        if (target.RenderTransform is not TranslateTransform)
        {
            target.RenderTransform = new TranslateTransform();
        }
        var animation = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Delay = TimeSpan.FromMilliseconds(delayMs),
            Easing = new Avalonia.Animation.Easings.QuinticEaseOut(),
            FillMode = Avalonia.Animation.FillMode.Backward,
            Children =
            {
                Frame(0, 0, offsetY),
                Frame(1, 1, 0),
            },
        };
        _ = animation.RunAsync(target);
    }

    private static Avalonia.Animation.KeyFrame Frame(double cue, double opacity, double y)
    {
        return new Avalonia.Animation.KeyFrame
        {
            Cue = new Avalonia.Animation.Cue(cue),
            Setters =
            {
                new Setter(Visual.OpacityProperty, opacity),
                new Setter(TranslateTransform.YProperty, y),
            },
        };
    }

    private static bool ReadSystemSetting()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }
        try
        {
            var enabled = 1;
            return !SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, ref enabled, 0) || enabled != 0;
        }
        catch
        {
            return true;
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);
}
