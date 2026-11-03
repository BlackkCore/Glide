namespace v2rayN.Desktop.Common;

/// <summary>
/// Small live chart of the last minute of download (filled, accent) and upload (line, green) speed.
/// Values are pushed once per statistics update; nothing animates while no new data arrives.
/// </summary>
public class SpeedSparkline : Control
{
    private const int Capacity = 60;
    private readonly Queue<(double Up, double Down)> _samples = new();

    // With animations on, the chart slides left by one sample over ~0.6 s instead of jumping,
    // and its vertical scale eases toward the new peak.
    private const double SlideMs = 600;
    private readonly DispatcherTimer _slideTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private DateTime _slideStart;
    private double _shift;
    private double _shownMax;
    private double _fromMax;
    private double _targetMax;

    public SpeedSparkline()
    {
        _slideTimer.Tick += (_, _) => Slide();
        // Hover shows the speeds at that moment, with units, and the minute's peak (the chart has no axis).
        PointerMoved += (_, e) => ShowValueAt(e.GetPosition(this).X);
    }

    private void ShowValueAt(double x)
    {
        var count = _samples.Count;
        if (count == 0 || Bounds.Width <= 0)
        {
            ToolTip.SetTip(this, null);
            return;
        }
        // Newest sample at the right edge, one slot per second back to the left.
        var slotsFromRight = (int)Math.Round((Bounds.Width - x) / Bounds.Width * (Capacity - 1));
        var index = Math.Clamp(count - 1 - slotsFromRight, 0, count - 1);
        var (up, down) = _samples.ElementAt(index);
        var peak = _samples.Max(t => Math.Max(t.Up, t.Down));
        var secondsAgo = count - 1 - index;
        // Samples are in KB/s, the unit Utils.HumanFy starts from.
        ToolTip.SetTip(this,
            $"‎↑ {Utils.HumanFy((long)up)}/s   ↓ {Utils.HumanFy((long)down)}/s‎" +
            $"{Environment.NewLine}{string.Format(ResUI.TipSparklineMoment, secondsAgo, $"‎{Utils.HumanFy((long)peak)}/s‎")}");
    }

    public void Push(double up, double down)
    {
        _samples.Enqueue((Math.Max(0, up), Math.Max(0, down)));
        while (_samples.Count > Capacity)
        {
            _samples.Dequeue();
        }

        _targetMax = Math.Max(16, _samples.Max(t => Math.Max(t.Up, t.Down))) * 1.15;
        if (!AppMotion.Enabled || _shownMax <= 0 || !IsEffectivelyVisible)
        {
            _shownMax = _targetMax;
            _shift = 0;
            InvalidateVisual();
            return;
        }
        _fromMax = _shownMax;
        _slideStart = DateTime.Now;
        _shift = 1;
        _slideTimer.Start();
        InvalidateVisual();
    }

    public void Clear()
    {
        _slideTimer.Stop();
        _samples.Clear();
        _shownMax = 0;
        _shift = 0;
        InvalidateVisual();
    }

    private void Slide()
    {
        var t = Math.Clamp((DateTime.Now - _slideStart).TotalMilliseconds / SlideMs, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3);
        _shift = 1 - eased;
        _shownMax = _fromMax + ((_targetMax - _fromMax) * eased);
        if (t >= 1)
        {
            _slideTimer.Stop();
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (_samples.Count < 2 || w <= 0 || h <= 0)
        {
            return;
        }

        var samples = _samples.ToArray();
        var max = _shownMax > 0 ? _shownMax : _targetMax;
        var step = w / (Capacity - 1);
        // While sliding, everything sits up to one step to the right and drifts into place.
        var x0 = w - ((samples.Length - 1 - _shift) * step);

        Point At(int i, double value) => new(x0 + (i * step), h - 1 - (Math.Min(value, max) / max * (h - 2)));

        using var clip = context.PushClip(new Rect(0, 0, w, h));

        var accent = Resource("AppAccentBrush", Brushes.RoyalBlue);
        var good = Resource("AppGoodDotBrush", Brushes.SeaGreen);

        // Download: soft area plus line.
        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(x0, h), true);
            for (var i = 0; i < samples.Length; i++)
            {
                ctx.LineTo(At(i, samples[i].Down));
            }
            ctx.LineTo(new Point(x0 + ((samples.Length - 1) * step), h));
            ctx.EndFigure(true);
        }
        context.DrawGeometry(new SolidColorBrush(accent.Color, 0.14), null, area);
        DrawLine(context, samples.Select((t, i) => At(i, t.Down)), new Pen(accent, 1.6, lineJoin: PenLineJoin.Round));

        // Upload: thin line on top.
        DrawLine(context, samples.Select((t, i) => At(i, t.Up)), new Pen(good, 1.2, lineJoin: PenLineJoin.Round));
    }

    private static void DrawLine(DrawingContext context, IEnumerable<Point> points, IPen pen)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var first = true;
            foreach (var p in points)
            {
                if (first)
                {
                    ctx.BeginFigure(p, false);
                    first = false;
                }
                else
                {
                    ctx.LineTo(p);
                }
            }
            ctx.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }

    private ISolidColorBrush Resource(string key, ISolidColorBrush fallback)
    {
        return this.TryFindResource(key, ActualThemeVariant, out var value) && value is ISolidColorBrush brush ? brush : fallback;
    }

    /// <summary>
    /// Parses Utils.HumanFy output such as "184.6 KB" back into the amount HumanFy was given
    /// (its first unit is KB), so <c>Utils.HumanFy((long)ParseSize(s))</c> round-trips.
    /// </summary>
    public static double ParseSize(string? text)
    {
        if (text.IsNullOrEmpty())
        {
            return 0;
        }
        var match = System.Text.RegularExpressions.Regex.Match(text, @"([\d.,]+)\s*([KMGT]?)B", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return 0;
        }
        return match.Groups[2].Value.ToUpperInvariant() switch
        {
            "K" => value,
            "M" => value * 1024,
            "G" => value * 1024 * 1024,
            "T" => value * 1024 * 1024 * 1024,
            _ => 0
        };
    }
}
