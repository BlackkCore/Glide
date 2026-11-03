namespace v2rayN.Desktop.Common;

/// <summary>
/// Small live chart of the last minute of download (filled, accent) and upload (line, green) speed.
/// Values are pushed once per statistics update; nothing animates while no new data arrives.
/// </summary>
public class SpeedSparkline : Control
{
    private const int Capacity = 60;
    private readonly Queue<(double Up, double Down)> _samples = new();

    public void Push(double up, double down)
    {
        _samples.Enqueue((Math.Max(0, up), Math.Max(0, down)));
        while (_samples.Count > Capacity)
        {
            _samples.Dequeue();
        }
        InvalidateVisual();
    }

    public void Clear()
    {
        _samples.Clear();
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
        var max = Math.Max(16, samples.Max(t => Math.Max(t.Up, t.Down))) * 1.15;
        var step = w / (Capacity - 1);
        var x0 = w - ((samples.Length - 1) * step);

        Point At(int i, double value) => new(x0 + (i * step), h - 1 - (value / max * (h - 2)));

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
            ctx.LineTo(new Point(w, h));
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
