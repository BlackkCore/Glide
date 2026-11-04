using Avalonia.Media.Immutable;

namespace v2rayN.Desktop.Common;

/// <summary>
/// Small round flags for the server list and status card. Windows can't draw flag emoji, so the
/// common server countries are drawn here as simple vector shapes on a 24×24 square (shown clipped
/// to a circle). Detail that can't be read at 24 px (emblems, stars) is left out or reduced to a dot.
/// Countries not listed return null and keep their two-letter code chip.
/// </summary>
public static class FlagImages
{
    private static readonly Dictionary<string, IImage?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IImage? Get(string? code)
    {
        if (code.IsNullOrEmpty() || code.Length != 2)
        {
            return null;
        }
        lock (Cache)
        {
            if (Cache.TryGetValue(code, out var cached))
            {
                return cached;
            }
            IImage? image = null;
            try
            {
                if (Build(code.ToUpperInvariant()) is { } drawing)
                {
                    drawing.ClipGeometry = new RectangleGeometry(new Rect(0, 0, S, S));
                    image = new DrawingImage(drawing);
                }
            }
            catch (Exception ex)
            {
                Logging.SaveLog("FlagImages", ex);
            }
            Cache[code] = image;
            return image;
        }
    }

    private const double S = 24;

    private static DrawingGroup? Build(string code) => code switch
    {
        // Horizontal stripes, top to bottom
        "DE" => H("#000000", "#DD0000", "#FFCE00"),
        "NL" => H("#AE1C28", "#FFFFFF", "#21468B"),
        "RU" => H("#FFFFFF", "#0039A6", "#D52B1E"),
        "AT" => H("#ED2939", "#FFFFFF", "#ED2939"),
        "PL" => H("#FFFFFF", "#DC143C"),
        "UA" => H("#0057B7", "#FFD700"),
        "ID" or "MC" => H("#E70011", "#FFFFFF"),
        "EE" => H("#0072CE", "#000000", "#FFFFFF"),
        "LT" => H("#FDB913", "#006A44", "#C1272D"),
        "LV" => H(("#9E3039", 2), ("#FFFFFF", 1), ("#9E3039", 2)),
        "HU" => H("#CD2A3E", "#FFFFFF", "#436F4D"),
        "BG" => H("#FFFFFF", "#00966E", "#D62612"),
        "LU" => H("#ED2939", "#FFFFFF", "#00A1DE"),
        "AM" => H("#D90012", "#0033A0", "#F2A800"),
        "AZ" => H("#0092BC", "#E4002B", "#00AF66"),
        "IR" => H("#239F40", "#FFFFFF", "#DA0000"),
        "BY" => H(("#C8313E", 2), ("#4AA657", 1)),
        "SK" or "SI" => H("#FFFFFF", "#0B4EA2", "#EE1C25"),
        "RS" => H("#C6363C", "#0C4076", "#FFFFFF"),
        "HR" => H("#FF0000", "#FFFFFF", "#171796"),
        "CO" => H(("#FCD116", 2), ("#003893", 1), ("#CE1126", 1)),
        "VE" => H("#FFCC00", "#00247D", "#CF142B"),
        "ES" => H(("#AA151B", 1), ("#F1BF00", 2), ("#AA151B", 1)),
        "TH" => H(("#A51931", 1), ("#F4F5F8", 1), ("#2D2A4A", 2), ("#F4F5F8", 1), ("#A51931", 1)),
        "IN" => With(H("#FF9933", "#FFFFFF", "#138808"), Ring(12, 12, 2.6, "#000080", 1)),
        "AR" => With(H("#74ACDF", "#FFFFFF", "#74ACDF"), Dot(12, 12, 2.4, "#F6B40E")),
        "EG" => With(H("#CE1126", "#FFFFFF", "#000000"), Dot(12, 12, 2.2, "#C09300")),
        "AE" => With(H("#00732F", "#FFFFFF", "#000000"), Box(0, 0, 7, 24, "#FF0000")),
        "CZ" => With(H("#FFFFFF", "#D7141A"), Poly("M0,0 L12,12 L0,24 Z", "#11457E")),
        "PH" => With(H("#0038A8", "#CE1126"), Poly("M0,0 L13,12 L0,24 Z", "#FFFFFF"), Dot(4.5, 12, 1.8, "#FCD116")),
        "SG" => With(H("#EF3340", "#FFFFFF"), Dot(7, 6, 3, "#FFFFFF"), Dot(8.4, 6, 2.6, "#EF3340")),
        "CL" => With(H("#FFFFFF", "#D52B1E"), Box(0, 0, 10, 12, "#0039A6"), Dot(5, 6, 1.6, "#FFFFFF")),
        "MY" => With(H("#CC0001", "#FFFFFF", "#CC0001", "#FFFFFF", "#CC0001", "#FFFFFF", "#CC0001"), Box(0, 0, 13, 13.7, "#010066"), Dot(6, 6.8, 3, "#FFCC00"), Dot(7.2, 6.8, 2.5, "#010066")),
        "GR" => With(H("#0D5EAF", "#FFFFFF", "#0D5EAF", "#FFFFFF", "#0D5EAF", "#FFFFFF", "#0D5EAF", "#FFFFFF", "#0D5EAF"), Box(0, 0, 13.3, 13.3, "#0D5EAF"), Box(5.3, 0, 2.7, 13.3, "#FFFFFF"), Box(0, 5.3, 13.3, 2.7, "#FFFFFF")),
        "US" => With(H("#B22234", "#FFFFFF", "#B22234", "#FFFFFF", "#B22234", "#FFFFFF", "#B22234", "#FFFFFF", "#B22234", "#FFFFFF", "#B22234", "#FFFFFF", "#B22234"), Box(0, 0, 11, 13, "#3C3B6E")),

        // Vertical stripes, left to right
        "FR" => V("#0055A4", "#FFFFFF", "#EF4135"),
        "IT" => V("#009246", "#FFFFFF", "#CE2B37"),
        "IE" => V("#169B62", "#FFFFFF", "#FF883E"),
        "BE" => V("#000000", "#FDDA24", "#EF3340"),
        "RO" => V("#002B7F", "#FCD116", "#CE1126"),
        "MD" => V("#0046AE", "#FFD200", "#CC092F"),
        "NG" => V("#008751", "#FFFFFF", "#008751"),
        "PE" => V("#D91023", "#FFFFFF", "#D91023"),
        "MX" => With(V("#006847", "#FFFFFF", "#CE1126"), Dot(12, 12, 2.2, "#8C5A2B")),
        "CA" => With(V(("#FF0000", 1), ("#FFFFFF", 2), ("#FF0000", 1)), Poly("M12,6.5 L15.5,12 L12,17.5 L8.5,12 Z", "#FF0000")),
        "PT" => With(V(("#046A38", 2), ("#DA291C", 3)), Dot(9.6, 12, 3.2, "#FFE900")),

        // Nordic crosses
        "SE" => Nordic("#006AA7", "#FECC02"),
        "FI" => Nordic("#FFFFFF", "#002F6C"),
        "DK" => Nordic("#C8102E", "#FFFFFF"),
        "NO" => Nordic("#BA0C2F", "#FFFFFF", "#00205B"),
        "IS" => Nordic("#02529C", "#FFFFFF", "#DC1E35"),

        // Plain field with a mark
        "JP" => With(Field("#FFFFFF"), Dot(12, 12, 6, "#BC002D")),
        "BD" => With(Field("#006A4E"), Dot(11, 12, 5.5, "#F42A41")),
        "VN" => With(Field("#DA251D"), Star(12, 12, 6, "#FFFF00")),
        "CN" => With(Field("#EE1C25"), Star(6.5, 6.5, 4, "#FFFF00")),
        "HK" => With(Field("#DE2910"), Star(12, 12, 6, "#FFFFFF")),
        "TW" => With(Field("#FE0000"), Box(0, 0, 13, 12, "#000095"), Dot(6.5, 6, 2.6, "#FFFFFF")),
        "TR" => With(Field("#E30A17"), Dot(10, 12, 6, "#FFFFFF"), Dot(11.6, 12, 4.8, "#E30A17"), Star(17.2, 12, 2.6, "#FFFFFF")),
        "PK" => With(Field("#01411C"), Box(0, 0, 6, 24, "#FFFFFF"), Dot(15, 12, 5, "#FFFFFF"), Dot(16.4, 11, 4.4, "#01411C")),
        "SA" => With(Field("#006C35"), Box(6, 14.5, 12, 1.6, "#FFFFFF")),
        "KZ" => With(Field("#00AFCA"), Dot(12, 11, 4, "#FEC50C")),
        "CH" => With(Field("#DA291C"), Box(10, 5, 4, 14, "#FFFFFF"), Box(5, 10, 14, 4, "#FFFFFF")),
        "GE" => With(Field("#FFFFFF"), Box(10, 0, 4, 24, "#FF0000"), Box(0, 10, 24, 4, "#FF0000")),
        "IL" => With(Field("#FFFFFF"), Box(0, 3.5, 24, 2.6, "#0038B8"), Box(0, 17.9, 24, 2.6, "#0038B8"), Ring(12, 12, 3.4, "#0038B8", 1.2)),
        "KR" => With(Field("#FFFFFF"), Half(12, 12, 5.5, "#CD2E3A", top: true), Half(12, 12, 5.5, "#0047A0", top: false)),
        "BR" => With(Field("#009C3B"), Poly("M12,3.5 L21.5,12 L12,20.5 L2.5,12 Z", "#FFDF00"), Dot(12, 12, 4.2, "#002776")),
        "AU" => With(Field("#012169"), MiniUnion(), Star(17.5, 15.5, 2.2, "#FFFFFF"), Star(8, 18.5, 2.4, "#FFFFFF")),
        "NZ" => With(Field("#012169"), MiniUnion(), Star(17.5, 9, 1.8, "#CC142B"), Star(16, 16.5, 2, "#CC142B")),
        "GB" or "UK" => Union(),
        _ => null,
    };

    // ---- building blocks -------------------------------------------------------------------------

    private static IBrush B(string hex) => new ImmutableSolidColorBrush(Color.Parse(hex));

    private static GeometryDrawing Box(double x, double y, double w, double h, string color) =>
        new() { Brush = B(color), Geometry = new RectangleGeometry(new Rect(x, y, w, h)) };

    private static GeometryDrawing Dot(double cx, double cy, double r, string color) =>
        new() { Brush = B(color), Geometry = new EllipseGeometry(new Rect(cx - r, cy - r, r * 2, r * 2)) };

    private static GeometryDrawing Ring(double cx, double cy, double r, string color, double thickness) =>
        new() { Pen = new Pen(B(color), thickness), Geometry = new EllipseGeometry(new Rect(cx - r, cy - r, r * 2, r * 2)) };

    private static GeometryDrawing Poly(string path, string color) =>
        new() { Brush = B(color), Geometry = Geometry.Parse(path) };

    private static GeometryDrawing Line(double x1, double y1, double x2, double y2, string color, double thickness) =>
        new() { Pen = new Pen(B(color), thickness), Geometry = new LineGeometry(new Point(x1, y1), new Point(x2, y2)) };

    private static GeometryDrawing Half(double cx, double cy, double r, string color, bool top)
    {
        var sweep = top ? "0 0 1" : "0 0 0";
        var path = FormattableString.Invariant($"M{cx - r},{cy} A{r},{r} {sweep} {cx + r},{cy} Z");
        return Poly(path, color);
    }

    // Five-pointed star centred on (cx, cy), outer radius r.
    private static GeometryDrawing Star(double cx, double cy, double r, string color)
    {
        var points = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            var radius = i % 2 == 0 ? r : r * 0.4;
            var angle = (-90 + (i * 36)) * Math.PI / 180;
            points.Add(FormattableString.Invariant($"{cx + (radius * Math.Cos(angle)):0.##},{cy + (radius * Math.Sin(angle)):0.##}"));
        }
        return Poly("M" + string.Join(" L", points) + " Z", color);
    }

    private static DrawingGroup Field(string color) => With(new DrawingGroup(), Box(0, 0, S, S, color));

    private static DrawingGroup With(DrawingGroup group, params Drawing[] extra)
    {
        foreach (var d in extra)
        {
            group.Children.Add(d);
        }
        return group;
    }

    private static DrawingGroup H(params string[] colors) => H(colors.Select(c => (c, 1)).ToArray());

    private static DrawingGroup H(params (string Color, int Weight)[] bands)
    {
        var group = new DrawingGroup();
        var total = bands.Sum(b => b.Weight);
        double y = 0;
        foreach (var (color, weight) in bands)
        {
            var h = S * weight / total;
            // A hair of overlap so no seam shows between bands.
            group.Children.Add(Box(0, y, S, h + 0.3, color));
            y += h;
        }
        return group;
    }

    private static DrawingGroup V(params string[] colors) => V(colors.Select(c => (c, 1)).ToArray());

    private static DrawingGroup V(params (string Color, int Weight)[] bands)
    {
        var group = new DrawingGroup();
        var total = bands.Sum(b => b.Weight);
        double x = 0;
        foreach (var (color, weight) in bands)
        {
            var w = S * weight / total;
            group.Children.Add(Box(x, 0, w + 0.3, S, color));
            x += w;
        }
        return group;
    }

    // Cross shifted towards the hoist, as on the real flags.
    private static DrawingGroup Nordic(string field, string cross, string? inner = null)
    {
        var group = Field(field);
        group.Children.Add(Box(6.5, 0, 5, S, cross));
        group.Children.Add(Box(0, 9.5, S, 5, cross));
        if (inner != null)
        {
            group.Children.Add(Box(7.75, 0, 2.5, S, inner));
            group.Children.Add(Box(0, 10.75, S, 2.5, inner));
        }
        return group;
    }

    private static DrawingGroup Union()
    {
        var group = Field("#012169");
        group.Children.Add(Line(0, 0, S, S, "#FFFFFF", 4.6));
        group.Children.Add(Line(S, 0, 0, S, "#FFFFFF", 4.6));
        group.Children.Add(Line(0, 0, S, S, "#C8102E", 1.6));
        group.Children.Add(Line(S, 0, 0, S, "#C8102E", 1.6));
        group.Children.Add(Box(9, 0, 6, S, "#FFFFFF"));
        group.Children.Add(Box(0, 9, S, 6, "#FFFFFF"));
        group.Children.Add(Box(10.2, 0, 3.6, S, "#C8102E"));
        group.Children.Add(Box(0, 10.2, S, 3.6, "#C8102E"));
        return group;
    }

    // The Union Jack in the top-left quarter (Australia, New Zealand).
    private static DrawingGroup MiniUnion()
    {
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, 12, 12)) };
        group.Children.Add(Line(0, 0, 12, 12, "#FFFFFF", 2.4));
        group.Children.Add(Line(12, 0, 0, 12, "#FFFFFF", 2.4));
        group.Children.Add(Line(0, 0, 12, 12, "#C8102E", 0.9));
        group.Children.Add(Line(12, 0, 0, 12, "#C8102E", 0.9));
        group.Children.Add(Box(4.6, 0, 2.8, 12, "#FFFFFF"));
        group.Children.Add(Box(0, 4.6, 12, 2.8, "#FFFFFF"));
        group.Children.Add(Box(5.2, 0, 1.6, 12, "#C8102E"));
        group.Children.Add(Box(0, 5.2, 12, 1.6, "#C8102E"));
        return group;
    }
}
