using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls.Notifications;

namespace v2rayN.Desktop.Common;

/// <summary>
/// Follows the Windows "Animation effects" setting (Settings → Accessibility → Visual effects).
/// When it is off, windows get the <c>reduceMotion</c> class so AppStyles can drop transitions,
/// and code-driven animations jump straight to their end state.
/// </summary>
public static partial class AppMotion
{
    public const string ReduceMotionClass = "reduceMotion";
    public const int FastMs = 150;
    public const int RevealMs = 200;
    public const int EntranceMs = 240;

    private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

    private static readonly Lazy<bool> _enabled = new(ReadSystemSetting);
    private static readonly ConditionalWeakTable<Visual, CancellationTokenSource> Entrances = new();

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
    public static void PlayEntrance(Visual? target, double offsetY = 8, double durationMs = EntranceMs,
        double delayMs = 0, double initialOpacity = 0)
    {
        if (!Enabled || target is null)
        {
            return;
        }
        if (offsetY != 0 && target.RenderTransform is not TranslateTransform)
        {
            target.RenderTransform = new TranslateTransform();
        }
        var animation = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Delay = TimeSpan.FromMilliseconds(delayMs),
            Easing = new QuinticEaseOut(),
            FillMode = Avalonia.Animation.FillMode.Backward,
            Children =
            {
                Frame(0, initialOpacity, offsetY, offsetY != 0),
                Frame(1, 1, 0, offsetY != 0),
            },
        };
        if (Entrances.TryGetValue(target, out var previous))
        {
            previous.Cancel();
        }
        var run = new CancellationTokenSource();
        Entrances.AddOrUpdate(target, run);
        RunEntranceAsync(animation, target, run).Forget(nameof(RunEntranceAsync));
    }

    private static async Task RunEntranceAsync(Animation animation, Visual target, CancellationTokenSource run)
    {
        try
        {
            await animation.RunAsync(target, run.Token);
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
        }
        finally
        {
            if (Entrances.TryGetValue(target, out var current) && current == run)
            {
                Entrances.Remove(target);
            }
            run.Dispose();
        }
    }

    private static KeyFrame Frame(double cue, double opacity, double y, bool translate)
    {
        var frame = new KeyFrame
        {
            Cue = new Avalonia.Animation.Cue(cue),
            Setters =
            {
                new Setter(Visual.OpacityProperty, opacity),
            },
        };
        if (translate)
        {
            frame.Setters.Add(new Setter(TranslateTransform.YProperty, y));
        }
        return frame;
    }

    public static Transitions FadeTransition(int durationMs = RevealMs)
        => [new DoubleTransition
        {
            Property = Visual.OpacityProperty,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Easing = new CubicEaseOut(),
        }];

    public static void RegisterToasts()
    {
        Control.LoadedEvent.AddClassHandler<NotificationCard>((card, _) =>
        {
            if (card.IsClosing || card.Transitions != null)
            {
                return;
            }
            var duration = Enabled ? RevealMs : FastMs;
            card.Opacity = 0;
            card.Transitions = FadeTransition(duration);
            card.Opacity = 1;
        });
        NotificationCard.IsClosingProperty.Changed.AddClassHandler<NotificationCard>((card, _) =>
        {
            if (!card.IsClosing)
            {
                return;
            }
            var duration = Enabled ? RevealMs : FastMs;
            card.Transitions ??= FadeTransition(duration);
            card.Opacity = 0;
            card.IsHitTestVisible = false;
            DispatcherTimer.RunOnce(() => card.SetCurrentValue(NotificationCard.IsClosedProperty, true),
                TimeSpan.FromMilliseconds(duration));
        });
    }

    // Keep layout and focus changes at the endpoints. The fade can reverse in place.
    internal sealed class Disclosure(Control content, Action<bool> setVisible)
    {
        private readonly DispatcherTimer _finish = new() { Interval = TimeSpan.FromMilliseconds(RevealMs) };
        private EventHandler? _onFinish;
        public bool IsOpen { get; private set; }

        public void SetOpen(bool open, bool animate)
        {
            IsOpen = open;
            _finish.Stop();
            if (_onFinish != null)
            {
                _finish.Tick -= _onFinish;
                _onFinish = null;
            }
            content.IsHitTestVisible = open;
            if (!animate || !Enabled)
            {
                content.Transitions = null;
                content.Opacity = open ? 1 : 0;
                setVisible(open);
                return;
            }

            if (open && !content.IsVisible)
            {
                content.Transitions = null;
                content.Opacity = 0;
                setVisible(true);
            }
            content.Transitions ??= FadeTransition();
            content.Opacity = open ? 1 : 0;
            if (!open)
            {
                _onFinish = (_, _) =>
                {
                    _finish.Stop();
                    setVisible(false);
                };
                _finish.Tick += _onFinish;
                _finish.Start();
            }
        }
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
