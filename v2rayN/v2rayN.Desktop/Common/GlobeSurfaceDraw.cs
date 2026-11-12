using System.Numerics;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace v2rayN.Desktop.Common;

internal readonly record struct GlobeProjection(Vector3 Right, Vector3 Up, Vector3 Forward)
{
    public static GlobeProjection FromAngles(double latitude, double longitude)
    {
        var (sinLat, cosLat) = Math.SinCos(latitude);
        var (sinLon, cosLon) = Math.SinCos(longitude);
        return new(
            new Vector3((float)-sinLon, (float)cosLon, 0),
            new Vector3((float)(-sinLat * cosLon), (float)(-sinLat * sinLon), (float)cosLat),
            new Vector3((float)(cosLat * cosLon), (float)(cosLat * sinLon), (float)sinLat));
    }

    public Point Project(Vector3 world, Point center, double radius) =>
        new(center.X + (radius * Vector3.Dot(world, Right)), center.Y - (radius * Vector3.Dot(world, Up)));
}

/// <summary>Projects the immutable land mesh on the render thread and batches dots with the same lighting.</summary>
internal sealed class GlobeSurfaceDraw(
    Rect bounds, Point center, double radius, double rimRadius, GlobeProjection projection,
    Vector3[] land, Color ink, Color accent, Color destinationColor, bool light,
    Vector3? origin, Vector3? destination) : ICustomDrawOperation
{
    private const int Shades = 12;
    private const int Groups = Shades + 2;
    private const int BatchSize = 512;
    [ThreadStatic] private static SKPoint[][]? _batches;

    public Rect Bounds { get; } = bounds;

    public void Render(ImmediateDrawingContext context)
    {
        if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature feature)
        {
            return;
        }
        using var lease = feature.Lease();
        var canvas = lease.SkCanvas;
        var batches = _batches ??= Enumerable.Range(0, Groups).Select(_ => new SKPoint[BatchSize]).ToArray();
        Span<int> counts = stackalloc int[Groups];
        counts.Clear();
        using var paint = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round };
        var horizon = (float)(rimRadius * rimRadius);
        var litOrigin = origin.GetValueOrDefault();
        var litDestination = destination.GetValueOrDefault();

        foreach (var world in land)
        {
            var depth = Vector3.Dot(world, projection.Forward);
            if (depth <= 0)
            {
                continue;
            }
            var x = Vector3.Dot(world, projection.Right) * (float)radius;
            var y = Vector3.Dot(world, projection.Up) * (float)radius;
            if ((x * x) + (y * y) > horizon)
            {
                continue;
            }
            // The dots roll from light into shadow as the continents turn.
            var illumination = Math.Clamp((depth * 0.76f) + (0.24f * ((y - x) / (float)radius)), 0, 1);
            var group = Math.Min(Shades - 1, (int)(illumination * Shades));
            if (Vector3.Dot(world, litDestination) > 0.989f)
            {
                group = Shades + 1;
            }
            else if (Vector3.Dot(world, litOrigin) > 0.989f)
            {
                group = Shades;
            }
            var batch = batches[group];
            batch[counts[group]++] = new SKPoint((float)center.X + x, (float)center.Y - y);
            if (counts[group] == batch.Length)
            {
                DrawBatch(canvas, paint, batch, group);
                counts[group] = 0;
            }
        }
        for (var group = 0; group < Groups; group++)
        {
            if (counts[group] > 0)
            {
                DrawBatch(canvas, paint, batches[group].AsSpan(0, counts[group]).ToArray(), group);
            }
        }
    }

    private void DrawBatch(SKCanvas canvas, SKPaint paint, SKPoint[] points, int group)
    {
        var strength = Math.Min(1, (double)group / (Shades - 1));
        var dot = (float)Math.Clamp(radius / 155, 0.85, 2.4);
        var color = group switch
        {
            Shades => accent,
            Shades + 1 => destinationColor,
            _ => ink,
        };
        if (group >= Shades)
        {
            paint.Color = new SKColor(color.R, color.G, color.B, 55);
            paint.StrokeWidth = dot * 2.5f;
            canvas.DrawPoints(SKPointMode.Points, points, paint);
        }
        paint.Color = new SKColor(color.R, color.G, color.B, (byte)((light ? 22 : 18) + (strength * 151)));
        paint.StrokeWidth = dot * (float)(0.68 + (strength * 0.46));
        canvas.DrawPoints(SKPointMode.Points, points, paint);
    }

    public bool HitTest(Point p) => false;
    public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
    public void Dispose() { }
}
