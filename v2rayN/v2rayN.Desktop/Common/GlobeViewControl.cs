using System.Diagnostics;
using System.Globalization;
using Avalonia.Animation.Easings;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Vector3 = System.Numerics.Vector3;

namespace v2rayN.Desktop.Common;

/// <summary>
/// The globe behind the connection: a lit dark sphere, the world as a field of dots, a GLIDE watermark
/// on open sea, and the route from this computer to the running server as a glowing line with pulses.
/// The camera turns a 3D land mesh beneath a fixed rim. Land, routes and pins share one projection,
/// and transient motion stops once the connection settles. Pure drawing: the status card says which country the
/// server is in and what state the connection is in.
/// </summary>
public class GlobeView : Control
{
    // Country centre (latitude, longitude) for the flags the server list can show; others stay unmarked.
    private static readonly Dictionary<string, (double Lat, double Lon)> Centers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["US"] = (39, -98), ["CA"] = (56, -100), ["MX"] = (23, -102), ["BR"] = (-10, -52), ["AR"] = (-34, -64),
        ["CL"] = (-33, -71), ["CO"] = (4, -73), ["PE"] = (-10, -75), ["GB"] = (54, -2), ["UK"] = (54, -2),
        ["IE"] = (53, -8), ["FR"] = (46, 2), ["DE"] = (51, 10), ["NL"] = (52, 5), ["BE"] = (50.6, 4.6),
        ["LU"] = (49.8, 6.1), ["CH"] = (47, 8), ["AT"] = (47.5, 14), ["IT"] = (42.5, 12.5), ["ES"] = (40, -4),
        ["PT"] = (39.5, -8), ["SE"] = (62, 15), ["NO"] = (62, 9), ["FI"] = (64, 26), ["DK"] = (56, 10),
        ["IS"] = (65, -18), ["PL"] = (52, 19), ["CZ"] = (49.8, 15.5), ["SK"] = (48.7, 19.5), ["HU"] = (47, 19.5),
        ["RO"] = (46, 25), ["BG"] = (42.7, 25.5), ["GR"] = (39, 22), ["RS"] = (44, 21), ["HR"] = (45, 15.5),
        ["SI"] = (46, 14.8), ["UA"] = (49, 32), ["MD"] = (47, 29), ["LT"] = (55.3, 23.9), ["LV"] = (57, 25),
        ["EE"] = (58.7, 25.5), ["RU"] = (60, 90), ["BY"] = (53.7, 28), ["TR"] = (39, 35), ["CY"] = (35, 33),
        ["IL"] = (31, 35), ["AE"] = (24, 54), ["SA"] = (24, 45), ["QA"] = (25.3, 51.2), ["IR"] = (32, 53),
        ["IQ"] = (33, 44), ["EG"] = (27, 30), ["ZA"] = (-29, 24), ["NG"] = (9, 8), ["KE"] = (0, 38),
        ["MA"] = (32, -6), ["IN"] = (22, 79), ["PK"] = (30, 70), ["BD"] = (24, 90), ["KZ"] = (48, 68),
        ["CN"] = (35, 103), ["HK"] = (22.3, 114.2), ["TW"] = (23.7, 121), ["JP"] = (36, 138), ["KR"] = (36.5, 128),
        ["SG"] = (1.35, 103.8), ["MY"] = (4, 102), ["TH"] = (15, 101), ["VN"] = (16, 107), ["ID"] = (-2, 118),
        ["PH"] = (12, 122), ["AU"] = (-25, 134), ["NZ"] = (-41, 174), ["AM"] = (40.2, 45), ["GE"] = (42, 43.5),
        ["AZ"] = (40.3, 47.7),
    };

    // Open-sea patches (latitude, longitude, how wide a word fits), the same ones as on the website.
    private static readonly (double Lat, double Lon, double Width)[] OceanPatches =
    [
        (18, -39, .57), (48, -30, .42), (-36, -15, .59), (-52, 12, .55), (-51, -35, .45), (3, -20, .25),
        (2, -98, .41), (0, -135, .49), (31, -145, .45), (-31, -123, .43), (-57, -135, .51),
        (24, 161, .62), (18, 156, .62), (7, 145, .30), (40, 163, .35), (83, -174, .30),
        (10, 63, .31), (-4, 87, .35), (2, 64, .43), (-28, 81, .33), (-40, 95, .53), (-49, 113, .44),
    ];

    public static readonly StyledProperty<string?> CountryProperty = AvaloniaProperty.Register<GlobeView, string?>(nameof(Country));
    public static readonly StyledProperty<bool> ConnectedProperty = AvaloniaProperty.Register<GlobeView, bool>(nameof(Connected));
    public static readonly StyledProperty<bool> CheckingProperty = AvaloniaProperty.Register<GlobeView, bool>(nameof(Checking));
    public static readonly StyledProperty<bool> LostProperty = AvaloniaProperty.Register<GlobeView, bool>(nameof(Lost));
    public static readonly StyledProperty<IImage?> ServerFlagProperty = AvaloniaProperty.Register<GlobeView, IImage?>(nameof(ServerFlag));
    public static readonly StyledProperty<string?> YouCaptionProperty = AvaloniaProperty.Register<GlobeView, string?>(nameof(YouCaption));
    public static readonly StyledProperty<string?> ServerCaptionProperty = AvaloniaProperty.Register<GlobeView, string?>(nameof(ServerCaption));

    /// <summary>Two-letter code of the other end of the route, if known.</summary>
    public string? Country { get => GetValue(CountryProperty); set => SetValue(CountryProperty, value); }

    /// <summary>True while the connection is verified; the route then glows and carries pulses.</summary>
    public bool Connected { get => GetValue(ConnectedProperty); set => SetValue(ConnectedProperty, value); }

    /// <summary>True while the connection is being checked: a ring searches out from your position.</summary>
    public bool Checking { get => GetValue(CheckingProperty); set => SetValue(CheckingProperty, value); }

    /// <summary>True when the connection dropped: the route shows a break.</summary>
    public bool Lost { get => GetValue(LostProperty); set => SetValue(LostProperty, value); }

    /// <summary>Round flag shown in the server's label.</summary>
    public IImage? ServerFlag { get => GetValue(ServerFlagProperty); set => SetValue(ServerFlagProperty, value); }

    /// <summary>Small caption above the start label ("You").</summary>
    public string? YouCaption { get => GetValue(YouCaptionProperty); set => SetValue(YouCaptionProperty, value); }

    /// <summary>Small caption above the server label ("Server").</summary>
    public string? ServerCaption { get => GetValue(ServerCaptionProperty); set => SetValue(ServerCaptionProperty, value); }

    // Countries of the saved servers: shown as small dim points, so the globe is alive before you connect.
    private static HashSet<string> _servers = [];

    public static void SetServers(IEnumerable<string?> codes)
    {
        var set = codes.Where(c => !string.IsNullOrEmpty(c)).Select(c => c!.ToUpperInvariant()).ToHashSet();
        if (!set.SetEquals(_servers))
        {
            _servers = set;
            ServersChanged?.Invoke();
        }
    }

    private static event Action? ServersChanged;

    // ===== The world as dots (a land mask: one bit per cell, rows by latitude, fewer cells towards the poles) =====
    private static Vector3[]? _land;

    private static void EnsureLand()
    {
        if (_land != null)
        {
            return;
        }
        var land = new List<Vector3>();
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Glide/Assets/globe-land.txt"));
            using var reader = new StreamReader(stream);
            var bits = Convert.FromBase64String(reader.ReadToEnd().Trim());
            // Read every row first, so a damaged row can be checked against its neighbours.
            var rows = new bool[180][];
            var cell = 0;
            for (var row = 0; row < 180; row++)
            {
                var latRad = (row - 89.5) * Math.PI / 180;
                var cells = Math.Max(1, (int)Math.Floor((360 * Math.Cos(latRad)) + 0.5));
                rows[row] = new bool[cells];
                for (var i = 0; i < cells; i++, cell++)
                {
                    rows[row][i] = (bits[cell >> 3] & (1 << (cell % 8))) != 0;
                }
            }
            for (var row = 0; row < 180; row++)
            {
                var lat = row - 89.5;
                var cells = rows[row].Length;
                // A row far fuller than the rows around it (row 73, 16 degrees south, is mostly "land") is a
                // fault in the data that draws a line of dots across the sea; keep only cells whose neighbour rows agree.
                var dense = row is > 0 and < 179 && lat is > -75 and < 75
                    && rows[row].Count(b => b) > cells * 0.6
                    && rows[row - 1].Count(b => b) < rows[row - 1].Length * 0.5;
                for (var i = 0; i < cells; i++)
                {
                    // The ice cap below 58 degrees south is left out: it only draws a ring of dots.
                    if (lat < -58 || !rows[row][i])
                    {
                        continue;
                    }
                    if (dense)
                    {
                        var frac = (i + 0.5) / cells;
                        var above = rows[row - 1][Math.Min(rows[row - 1].Length - 1, (int)(frac * rows[row - 1].Length))];
                        var below = rows[row + 1][Math.Min(rows[row + 1].Length - 1, (int)(frac * rows[row + 1].Length))];
                        if (!above && !below)
                        {
                            continue;
                        }
                    }
                    var lonDeg = ((i + 0.25 + (0.5 * (row % 2))) * 360 / cells) - 180;
                    land.Add(ToXyz(lat, lonDeg));
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("GlobeLand", ex);
        }
        _land = land.ToArray();
    }

    private const double CameraMs = 840;
    private const double DepartureMs = 240;
    private const double TraceMs = 680;
    private const double SettleMs = 1050;
    private static readonly CubicEaseInOut CameraEasing = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Action _onExternalChanged;
    private Window? _window;
    private bool _attached;
    private ConnState _state;
    private long _stateStarted;
    private long _cameraStarted;
    private double _cameraArc;
    private bool _hasPresented;
    private readonly record struct Camera(double Lat, double Lon, double Zoom);
    private Camera _camera = new(20, 10, 1);
    private Camera _cameraFrom = new(20, 10, 1);
    private Camera _cameraTarget = new(20, 10, 1);
    private string? _departingCountry;
    private IImage? _departingFlag;
    private bool _departingLost;
    private (double Lat, double Lon)? _checkingOrigin;

    static GlobeView()
    {
        AffectsRender<GlobeView>(CountryProperty, ConnectedProperty, CheckingProperty, LostProperty, ServerFlagProperty, YouCaptionProperty, ServerCaptionProperty);
    }

    public GlobeView()
    {
        IsHitTestVisible = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        _timer.Tick += (_, _) =>
        {
            RefreshClock();
            InvalidateVisual();
        };
        _onExternalChanged = () => Dispatcher.UIThread.Post(RefreshScene);
    }

    internal void SetConnection(ConnState state, string? country, IImage? flag)
    {
        if (_state == state && Country == country && ReferenceEquals(ServerFlag, flag))
        {
            return;
        }

        if (_state != state || (state == ConnState.Checking && Country != country))
        {
            _stateStarted = 0;
            _departingCountry = country == null && state is not (ConnState.Checking or ConnState.Connected or ConnState.NoInternet)
                ? Country : null;
            _departingFlag = _departingCountry != null ? ServerFlag : null;
            _departingLost = Lost;
        }
        _state = state;
        Country = country;
        ServerFlag = flag;
        Connected = state == ConnState.Connected;
        Checking = state == ConnState.Checking;
        Lost = state == ConnState.NoInternet;
        RefreshScene();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window != null)
        {
            _window.PropertyChanged += OnWindowPropertyChanged;
        }
        EnsureLand();
        GlobeOrigin.Changed += _onExternalChanged;
        ServersChanged += _onExternalChanged;
        RefreshScene();
        // Find the origin on startup, even before connecting.
        _ = GlobeOrigin.RefreshAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _timer.Stop();
        if (_window != null)
        {
            _window.PropertyChanged -= OnWindowPropertyChanged;
            _window = null;
        }
        GlobeOrigin.Changed -= _onExternalChanged;
        ServersChanged -= _onExternalChanged;
        _static?.Dispose();
        _static = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            RefreshClock();
            InvalidateVisual();
        }
    }

    private void RefreshScene()
    {
        var origin = Origin();
        if (Checking && origin != _checkingOrigin)
        {
            _stateStarted = 0;
        }
        _checkingOrigin = origin;
        UpdateCamera();
        var target = FrameRoute(origin);
        if (!_hasPresented && _attached && AppMotion.Enabled)
        {
            // Reveal the sphere with a small turn even when there is no route yet.
            _hasPresented = true;
            _camera = target with { Lon = target.Lon - 24, Lat = target.Lat - 5 };
            _cameraFrom = _camera;
            _cameraTarget = target;
            _cameraStarted = 0;
            _cameraArc = 0;
        }
        else if (_cameraTarget != target)
        {
            _cameraFrom = _camera;
            _cameraTarget = target;
            _cameraStarted = 0;
            _cameraArc = Checking || Connected ? 8 : 0;
        }
        RefreshClock();
        InvalidateVisual();
    }

    private void UpdateCamera()
    {
        if (!AppMotion.Enabled || !_attached || _window?.WindowState == WindowState.Minimized || !IsEffectivelyVisible)
        {
            _camera = _cameraTarget;
            return;
        }
        if (_cameraStarted == 0 || _camera == _cameraTarget)
        {
            return;
        }

        var progress = Math.Clamp(Stopwatch.GetElapsedTime(_cameraStarted).TotalMilliseconds / CameraMs, 0, 1);
        if (progress >= 1)
        {
            _camera = _cameraTarget;
            return;
        }
        var turn = CameraEasing.Ease(progress);
        var push = CameraEasing.Ease(Math.Clamp((progress - 0.10) / 0.90, 0, 1));
        var longitudeTravel = Math.IEEERemainder(_cameraTarget.Lon - _cameraFrom.Lon, 360);
        // The small arc keeps short routes from looking like a flat scale animation.
        var arc = _cameraArc * Math.Sin(Math.PI * turn);
        _camera = new Camera(
            _cameraFrom.Lat + ((_cameraTarget.Lat - _cameraFrom.Lat) * turn) + arc,
            _cameraFrom.Lon + (longitudeTravel * turn),
            _cameraFrom.Zoom + ((_cameraTarget.Zoom - _cameraFrom.Zoom) * push));
    }

    private void RefreshClock()
    {
        UpdateCamera();
        var animating = Checking || (Connected && StateAgeMs < SettleMs)
            || (_departingCountry != null && StateAgeMs < DepartureMs) || _camera != _cameraTarget;
        if (_attached && AppMotion.Enabled && animating && IsEffectivelyVisible && _window?.WindowState != WindowState.Minimized)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private double StateAgeMs => _stateStarted == 0 ? 0 : Stopwatch.GetElapsedTime(_stateStarted).TotalMilliseconds;

    private static (double Lat, double Lon)? Origin()
        => GlobeOrigin.Position ?? (GlobeOrigin.Code is { } c && Centers.TryGetValue(c, out var p) ? p : null);

    private bool TryTarget(out double lat, out double lon)
    {
        lat = 20;
        lon = 10;
        if (Country is { Length: > 0 } c && Centers.TryGetValue(c, out var p))
        {
            lat = p.Lat;
            lon = p.Lon;
            return true;
        }
        return false;
    }

    private static bool Same((double Lat, double Lon) a, double lat, double lon)
        => Math.Abs(a.Lat - lat) < 0.5 && Math.Abs(a.Lon - lon) < 0.5;

    private Camera FrameRoute((double Lat, double Lon)? origin)
    {
        var overview = origin is { } start ? new Camera(start.Lat * 0.72, start.Lon, 1) : new Camera(20, 10, 1);
        if (!(Connected || Checking || Lost))
        {
            return overview;
        }
        if (!TryTarget(out var lat, out var lon))
        {
            // Show the camera move during verification, even before we have a location for the server.
            return overview with { Lat = overview.Lat + 6, Lon = overview.Lon + 21, Zoom = 1.23 };
        }
        if (origin is not { } from || Same(from, lat, lon))
        {
            return new Camera(Math.Clamp(lat - 8, -75, 75), lon + 13, 1.34);
        }

        var a = ToXyz(from.Lat, from.Lon);
        var b = ToXyz(lat, lon);
        var angle = Math.Acos(Math.Clamp(Vector3.Dot(a, b), -1, 1));
        var midpoint = a + b;
        if (midpoint.LengthSquared() < 0.01f)
        {
            return new Camera(lat * 0.85, lon, 1);
        }
        midpoint = Vector3.Normalize(midpoint);
        var midLat = Math.Asin(Math.Clamp(midpoint.Z, -1, 1)) * 180 / Math.PI;
        var midLon = Math.Atan2(midpoint.Y, midpoint.X) * 180 / Math.PI;
        var preferredZoom = Math.Clamp(1.9 - (angle / Math.PI), 1.12, 1.85);
        // Keep both pins inside the rim on long routes.
        var fit = Math.Max(1, 0.92 / Math.Sin(angle / 2));
        return new Camera(Math.Clamp(midLat, -78, 78), midLon, Math.Min(fit, preferredZoom));
    }

    private static Vector3 ToXyz(double latDeg, double lonDeg)
    {
        var (sinLat, cosLat) = Math.SinCos(latDeg * Math.PI / 180);
        var (sinLon, cosLon) = Math.SinCos(lonDeg * Math.PI / 180);
        return new Vector3((float)(cosLat * cosLon), (float)(cosLat * sinLon), (float)sinLat);
    }

    private IBrush Brush(string key, IBrush fallback)
        => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;

    private static Color ColorOf(IBrush brush, Color fallback)
        => (brush as ISolidColorBrush)?.Color ?? fallback;

    private FormattedText? _mark;

    private RenderTargetBitmap? _static;
    private (Size Size, double Scale, bool Light, bool Connected, Color Accent) _staticKey;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!_timer.IsEnabled)
        {
            RefreshClock();
        }
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size < 40)
        {
            return;
        }
        var variant = ActualThemeVariant;
        var light = variant == ThemeVariant.Light || variant?.InheritVariant == ThemeVariant.Light;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var r = (size / 2) - 16;

        var accent = ColorOf(Brush("AppAccentBrush", Brushes.SlateBlue), Colors.SlateBlue);
        var good = ColorOf(Brush("AppGoodDotBrush", Brushes.Teal), Colors.Teal);
        var bad = ColorOf(Brush("AppBadDotBrush", Brushes.IndianRed), Colors.IndianRed);
        var routeColor = Lost ? bad : Connected ? good : accent;
        var ink = light ? Color.FromRgb(79, 70, 229) : Color.FromRgb(176, 148, 255);

        var lat0 = _camera.Lat * Math.PI / 180;
        var lon0 = _camera.Lon * Math.PI / 180;
        var projection = GlobeProjection.FromAngles(lat0, lon0);
        var origin = Origin();
        var departure = AppMotion.Enabled && _departingCountry != null
            ? 1 - Math.Clamp(StateAgeMs / DepartureMs, 0, 1) : 0;
        var routeCountry = departure > 0 ? _departingCountry : Country;
        var target = routeCountry != null && Centers.TryGetValue(routeCountry, out var endpoint)
            ? ((double Lat, double Lon)?)endpoint : null;
        var rz = r * _camera.Zoom;
        var disk = new EllipseGeometry(new Rect(c.X - r, c.Y - r, r * 2, r * 2));
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var bounds = new Rect(Bounds.Size);
        var shellKey = (bounds.Size, scale, light, Connected, accent);
        if (_static == null || _staticKey != shellKey)
        {
            _static?.Dispose();
            _static = DrawLayer(bounds.Size, scale, ctx => DrawStill(ctx, c, r, size, light, accent));
            _staticKey = shellKey;
        }
        if (_static != null)
        {
            context.DrawImage(_static, bounds);
        }
        else
        {
            DrawStill(context, c, r, size, light, accent);
        }

        // Start the clocks once the first scene is ready.
        if (_cameraStarted == 0 && _camera != _cameraTarget)
        {
            _cameraStarted = Stopwatch.GetTimestamp();
        }
        if (_stateStarted == 0 && (Checking || Connected || departure > 0))
        {
            _stateStarted = Stopwatch.GetTimestamp();
        }
        using (context.PushGeometryClip(disk))
        {
            var watermark = Math.Clamp(1 - ((_camera.Zoom - 1) / 0.18), 0, 1);
            if (watermark > 0)
            {
                using (context.PushOpacity(watermark))
                {
                    DrawWatermark(context, c, rz, lat0, lon0, light, origin, target);
                }
            }
            if (_land is { Length: > 0 } land)
            {
                var glow = Connected ? good : accent;
                context.Custom(new GlobeSurfaceDraw(bounds, c, rz, r, projection, land, ink, accent, glow, light,
                    origin is { } o ? ToXyz(o.Lat, o.Lon) : null,
                    (Connected || Checking) && target is { } t ? ToXyz(t.Lat, t.Lon) : null));
            }
        }

        var age = StateAgeMs;
        if (AppMotion.Enabled && Checking)
        {
            DrawRimShine(context, c, r, accent, light, age / 600);
        }

        UpdateRoute(c, rz, projection, origin, target);
        _placedLabels.Clear();
        Point? youAt = null;
        Point? serverAt = null;
        // Everything on the surface stays inside the sphere, also when it is magnified.
        using (context.PushGeometryClip(disk))
        {
            DrawServerNodes(context, c, rz, projection, ink, origin, target);
            if (departure > 0 && _route != null)
            {
                StrokeRoute(context, _route, _departingLost ? bad : good, departure * 0.8, 2);
            }
            else
            {
                DrawRoute(context, routeColor, accent, bad, age);
            }
            if (origin is { } og)
            {
                youAt = DrawNode(context, c, rz, projection, og, accent, ink);
                if (AppMotion.Enabled && Checking && youAt is { } ya0)
                {
                    // A ring searches out from you while the connection is being checked.
                    var t = (age % 1300) / 1300;
                    var ring = 8 + (t * 29);
                    context.DrawEllipse(null, new Pen(new SolidColorBrush(accent, (1 - t) * 0.6), 1.6), ya0, ring, ring);
                }
            }
            if (target is { } server && (Connected || Checking || Lost || departure > 0))
            {
                var nodeColor = departure > 0 ? (_departingLost ? bad : good) : routeColor;
                using (context.PushOpacity(departure > 0 ? departure : 1))
                {
                    serverAt = DrawNode(context, c, rz, projection, server, nodeColor, nodeColor);
                }
                if (AppMotion.Enabled && Connected && serverAt is { } at && age is >= TraceMs and < SettleMs)
                {
                    var t = (age - TraceMs) / (SettleMs - TraceMs);
                    var ring = 7 + (t * 21);
                    context.DrawEllipse(null, new Pen(new SolidColorBrush(good, (1 - t) * 0.65), 1.5), at, ring, ring);
                }
            }
        }
        if (youAt is { } yt && GlobeOrigin.Code is { } yc)
        {
            DrawLabel(context, yt, YouCaption, CountryNames.Get(yc), FlagImages.Get(yc), light, ref _originLabel);
        }
        else if (origin == null && GlobeOrigin.IsLocating)
        {
            DrawLocatingPill(context, c, r, light);
        }
        if (serverAt is { } sa && routeCountry is { Length: > 0 } sc)
        {
            using (context.PushOpacity(departure > 0 ? departure : 1))
            {
                DrawLabel(context, sa, ServerCaption, CountryNames.Get(sc), departure > 0 ? _departingFlag : ServerFlag, light, ref _serverLabel);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------
    private static RenderTargetBitmap? DrawLayer(Size size, double scale, Action<DrawingContext> render)
    {
        RenderTargetBitmap? bitmap = null;
        try
        {
            bitmap = new RenderTargetBitmap(
                new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * scale)), Math.Max(1, (int)Math.Ceiling(size.Height * scale))),
                new Vector(96 * scale, 96 * scale));
            using var ctx = bitmap.CreateDrawingContext();
            render(ctx);
            return bitmap;
        }
        catch (Exception ex)
        {
            bitmap?.Dispose();
            Logging.SaveLog("GlobeLayer", ex);
            return null;
        }
    }

    private void DrawStill(DrawingContext ctx, Point c, double r, double size, bool light, Color accent)
    {
        // Halo outside the sphere, stronger when connected.
        var halo = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 0.70),
                new GradientStop(Color.FromArgb(light ? (Connected ? (byte)30 : (byte)20)
                    : (Connected ? (byte)70 : (byte)44), accent.R, accent.G, accent.B), 0.86),
                new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1),
            },
        };
        ctx.DrawEllipse(halo, null, c, size / 2, size / 2);

        // The body: glass lit from the upper left.
        var body = new RadialGradientBrush
        {
            Center = new RelativePoint(0.38, 0.32, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.38, 0.32, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.80, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.80, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(light ? Color.FromRgb(255, 255, 255) : Color.FromRgb(38, 38, 88), 0),
                new GradientStop(light ? Color.FromRgb(214, 217, 242) : Color.FromRgb(5, 6, 14), 1),
            },
        };
        ctx.DrawEllipse(body, null, c, r, r);

        // Depth: the far side falls into shade, and a soft highlight sits where the light hits.
        var shade = new RadialGradientBrush
        {
            Center = new RelativePoint(0.38, 0.32, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.38, 0.32, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.95, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.95, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.50),
                new GradientStop(light ? Color.FromArgb(70, 60, 55, 140) : Color.FromArgb(150, 0, 0, 8), 1),
            },
        };
        ctx.DrawEllipse(shade, null, c, r, r);
        var spec = new RadialGradientBrush
        {
            Center = new RelativePoint(0.30, 0.24, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.30, 0.24, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.38, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.38, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(light ? (byte)120 : (byte)38, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
            },
        };
        ctx.DrawEllipse(spec, null, c, r, r);

        // The rim: a thin bright edge with a soft bloom around it.
        ctx.DrawEllipse(null, new Pen(new SolidColorBrush(accent, light ? 0.035 : 0.07), 12), c, r, r);
        ctx.DrawEllipse(null, new Pen(new SolidColorBrush(accent, light ? 0.08 : 0.16), 5), c, r, r);
        ctx.DrawEllipse(null, new Pen(new SolidColorBrush(accent, light ? 0.40 : 0.85), 1.4), c, r, r);
    }

    // GLIDE lies flat on the sphere over the open sea that faces us most, like the website: it picks the
    // widest patch of ocean near the middle of the view, follows the curve of the surface there, and
    // darkens the globe under the letters.
    private void DrawWatermark(DrawingContext ctx, Point c, double r, double lat0, double lon0, bool light, (double Lat, double Lon)? youAt = null, (double Lat, double Lon)? serverAt = null)
    {
        var sl0 = Math.Sin(lat0);
        var cl0 = Math.Cos(lat0);
        var bestScore = double.NegativeInfinity;
        double bx = 0, by = 0, bz = 0, width = 0;
        // The "You" and "Server" labels sit at the two ends; sea patches close to either are avoided so the
        // letters never run under a label.
        var ends = new List<Vector3>(2);
        if (youAt is { } ya)
        {
            ends.Add(ToXyz(ya.Lat, ya.Lon));
        }
        if (serverAt is { } sa)
        {
            ends.Add(ToXyz(sa.Lat, sa.Lon));
        }
        foreach (var (lat, lon, w) in OceanPatches)
        {
            var la = lat * Math.PI / 180;
            var dLon = (lon * Math.PI / 180) - lon0;
            var x = Math.Cos(la) * Math.Sin(dLon);
            var y = (cl0 * Math.Sin(la)) - (sl0 * Math.Cos(la) * Math.Cos(dLon));
            var z = (sl0 * Math.Sin(la)) + (cl0 * Math.Cos(la) * Math.Cos(dLon));
            var score = z >= .6 ? z * (1 + (1.9 * w)) : z;
            var patch = ToXyz(lat, lon);
            if (ends.Any(e => Vector3.Dot(e, patch) > 0.94))
            {
                score -= 3;
            }
            if (score <= bestScore)
            {
                continue;
            }
            bestScore = score;
            bx = x;
            by = y;
            bz = z;
            width = w;
        }
        if (width <= 0 || bz < 0.25)
        {
            return;
        }
        if (_mark == null)
        {
            _mark = new FormattedText(
                "GLIDE",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("avares://Glide/Assets/Fonts#Manrope"), FontStyle.Normal, FontWeight.ExtraBold),
                100,
                Brushes.White);
            _mark.SetFontWeight(FontWeight.ExtraBold);
        }
        // The word's own axes on the surface: east runs sideways on screen, north is perpendicular to it.
        var len = Math.Sqrt((bz * bz) + (bx * bx));
        var ea = bz / len;
        var eb = -bx / len;
        var northX = by * eb;
        var northY = (bz * ea) - (bx * eb);
        var scale = width * r / _mark.Width;
        var matrix = Matrix.CreateTranslation(-_mark.Width / 2, -_mark.Height / 2)
            * Matrix.CreateScale(scale, scale)
            * new Matrix(ea, 0, -northX, northY, c.X + (bx * r), c.Y - (by * r));
        var fade = Math.Min(1, (bz - 0.25) / 0.35);
        var tint = light ? Color.FromArgb((byte)(54 * fade), 55, 48, 163) : Color.FromArgb((byte)(130 * fade), 2, 3, 12);
        _mark.SetForegroundBrush(new SolidColorBrush(tint));
        using (ctx.PushTransform(matrix))
        {
            ctx.DrawText(_mark, default);
        }
    }

    // A bright stretch of the rim that drifts slowly round it (the globe itself stays put).
    private static void DrawRimShine(DrawingContext context, Point c, double r, Color accent, bool light, double start)
    {
        var span = 1.0;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(c.X + (r * Math.Cos(start)), c.Y + (r * Math.Sin(start))), false);
            g.ArcTo(new Point(c.X + (r * Math.Cos(start + span)), c.Y + (r * Math.Sin(start + span))), new Size(r, r), 0, false, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        var white = Color.FromRgb(255, 255, 255);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(accent, light ? 0.20 : 0.28), 5, lineCap: PenLineCap.Round), geo);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(white, light ? 0.55 : 0.50), 1.6, lineCap: PenLineCap.Round), geo);
    }

    // The saved servers' countries as small dim points.
    private void DrawServerNodes(DrawingContext context, Point c, double r, GlobeProjection projection,
        Color ink, (double Lat, double Lon)? origin, (double Lat, double Lon)? target)
    {
        foreach (var code in _servers)
        {
            if (!Centers.TryGetValue(code, out var p) || (origin is { } o && Same(o, p.Lat, p.Lon)) || (target is { } t && Same(t, p.Lat, p.Lon)))
            {
                continue;
            }
            var world = ToXyz(p.Lat, p.Lon);
            if (Vector3.Dot(world, projection.Forward) > 0.12f)
            {
                var at = projection.Project(world, c, r);
                context.DrawEllipse(new SolidColorBrush(ink, 0.18), null, at, 5, 5);
                context.DrawEllipse(new SolidColorBrush(ink, 0.55), null, at, 1.9, 1.9);
            }
        }
    }

    private static Point? DrawNode(DrawingContext context, Point c, double r, GlobeProjection projection,
        (double Lat, double Lon) at, Color core, Color glow)
    {
        var world = ToXyz(at.Lat, at.Lon);
        if (Vector3.Dot(world, projection.Forward) < 0.12f)
        {
            return null;
        }
        var p = projection.Project(world, c, r);
        context.DrawEllipse(new SolidColorBrush(glow, 0.25), null, p, 9, 9);
        context.DrawEllipse(new SolidColorBrush(core), new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1), p, 4, 4);
        return p;
    }

    private static readonly FontFamily GlobeFontLatin = new("avares://Glide/Assets/Fonts#Manrope, avares://Glide/Assets/Fonts#Vazirmatn");
    private static readonly FontFamily GlobeFontPersian = new("avares://Glide/Assets/Fonts#Vazirmatn, avares://Glide/Assets/Fonts#Manrope");

    private static FormattedText Text(string text, double size, FontWeight weight, Color color)
        => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(CountryNames.PersianUi ? GlobeFontPersian : GlobeFontLatin, FontStyle.Normal, weight), size, new SolidColorBrush(color));

    private static void DrawLocatingPill(DrawingContext context, Point c, double r, bool light)
    {
        var muted = light ? Color.FromRgb(75, 85, 99) : Color.FromRgb(163, 169, 184);
        var label = Text(ResUI.TbLocating, 11.5, FontWeight.SemiBold, muted);
        const double padX = 12;
        const double padY = 6;
        var w = label.Width + (padX * 2);
        var h = label.Height + (padY * 2);
        var rect = new Rect(c.X - (w / 2), c.Y + r - h - 8, w, h);
        context.DrawRectangle(
            new SolidColorBrush(light ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(214, 14, 15, 26)),
            new Pen(new SolidColorBrush(light ? Color.FromRgb(228, 228, 238) : Color.FromArgb(32, 255, 255, 255)), 1),
            rect,
            10,
            10);
        context.DrawText(label, new Point(rect.X + padX, rect.Y + padY));
    }

    // Where the route line was drawn this frame, and the labels already placed, so labels keep clear of both.
    private Point?[]? _routePoints;
    private readonly List<Rect> _placedLabels = [];
    private LabelLayout? _originLabel, _serverLabel;

    private sealed class LabelLayout(string? caption, string name, bool light)
    {
        public (string? Caption, string Name, bool Light) TextKey { get; } = (caption, name, light);
        public FormattedText? Caption { get; } = string.IsNullOrEmpty(caption) ? null
            : Text(caption, 10.5, FontWeight.Medium, light ? Color.FromRgb(75, 85, 99) : Color.FromRgb(163, 169, 184));
        public FormattedText Title { get; } = Text(name, 12.5, FontWeight.Bold,
            light ? Color.FromRgb(17, 24, 39) : Color.FromRgb(243, 244, 248));
        public (Point Anchor, Size Bounds, int Route, bool Flag, Rect Prior) PlacementKey { get; set; }
        public Rect Rect { get; set; }
        public int Candidate { get; set; }
    }

    private bool HitsRoute(Rect rect)
    {
        if (_routePoints == null)
        {
            return false;
        }
        var box = rect.Inflate(9);
        foreach (var p in _routePoints)
        {
            if (p is { } pt && box.Contains(pt))
            {
                return true;
            }
        }
        for (var i = 1; i < _routePoints.Length; i++)
        {
            if (_routePoints[i - 1] is { } a && _routePoints[i] is { } b && box.Contains(new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2)))
            {
                return true;
            }
        }
        return false;
    }

    // A small label beside a point: caption above the country name, a round flag first when there is one.
    private void DrawLabel(DrawingContext context, Point at, string? caption, string name, IImage? flag, bool light, ref LabelLayout? cache)
    {
        if (cache == null || cache.TextKey != (caption, name, light))
        {
            cache = new LabelLayout(caption, name, light);
        }
        var cap = cache.Caption;
        var title = cache.Title;
        const double padX = 10;
        const double padY = 6;
        var flagSize = flag == null ? 0 : 20.0;
        var textW = Math.Max(cap?.Width ?? 0, title.Width);
        var w = padX + (flag == null ? 0 : flagSize + 8) + textW + padX;
        var h = padY + (cap?.Height ?? 0) + title.Height + padY;
        var placement = (at, Bounds.Size, _routeVersion, flag != null, _placedLabels.Count > 0 ? _placedLabels[0] : default);
        if (cache.PlacementKey != placement)
        {
            cache.PlacementKey = placement;
            cache.Rect = PlaceLabel(at, w, h, cache);
        }
        var rect = cache.Rect;
        _placedLabels.Add(rect);
        var x = rect.X;
        var y = rect.Y;
        var line = new Pen(new SolidColorBrush(light ? Color.FromArgb(70, 79, 70, 229) : Color.FromArgb(70, 176, 148, 255)), 1);
        var corner = new Point(at.X < rect.Center.X ? rect.X : rect.Right, at.Y < rect.Center.Y ? rect.Y : rect.Bottom);
        context.DrawLine(line, at, corner);
        context.DrawRectangle(
            new SolidColorBrush(light ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(214, 14, 15, 26)),
            new Pen(new SolidColorBrush(light ? Color.FromRgb(228, 228, 238) : Color.FromArgb(32, 255, 255, 255)), 1),
            rect,
            10,
            10);
        var rtl = CountryNames.PersianUi;
        var tx = x + padX;
        if (flag != null)
        {
            var fx = rtl ? x + w - padX - flagSize : tx;
            var fy = y + ((h - flagSize) / 2);
            var localRect = new Rect(0, 0, flagSize, flagSize);
            using (context.PushTransform(Matrix.CreateTranslation(fx, fy)))
            using (context.PushGeometryClip(new EllipseGeometry(localRect)))
            {
                context.DrawImage(flag, localRect);
            }
            if (!rtl)
            {
                tx += flagSize + 8;
            }
        }
        var ty = y + padY;
        if (cap != null)
        {
            context.DrawText(cap, new Point(rtl ? tx + textW - cap.Width : tx, ty));
            ty += cap.Height;
        }
        context.DrawText(title, new Point(rtl ? tx + textW - title.Width : tx, ty));
    }

    private Rect PlaceLabel(Point at, double width, double height, LabelLayout cache)
    {
        // Preserve the current side while zooming, as long as it still fits.
        const double gap = 14;
        Span<Point> candidates =
        [
            new(at.X + gap, at.Y - gap - height), new(at.X - gap - width, at.Y - gap - height),
            new(at.X + gap, at.Y + gap), new(at.X - gap - width, at.Y + gap),
            new(at.X + gap, at.Y - (height / 2)), new(at.X - gap - width, at.Y - (height / 2)),
            new(at.X - (width / 2), at.Y - gap - height), new(at.X - (width / 2), at.Y + gap),
        ];
        for (var i = 0; i < candidates.Length; i++)
        {
            var index = (cache.Candidate + i) % candidates.Length;
            var rect = new Rect(candidates[index], new Size(width, height));
            if (rect.X >= 2 && rect.Y >= 2 && rect.Right <= Bounds.Width - 2 && rect.Bottom <= Bounds.Height - 2
                && !_placedLabels.Any(p => p.Inflate(6).Intersects(rect)) && !HitsRoute(rect))
            {
                cache.Candidate = index;
                return rect;
            }
        }
        return new Rect(Math.Clamp(at.X + gap, 2, Math.Max(2, Bounds.Width - width - 2)),
            Math.Clamp(at.Y - gap - height, 2, Math.Max(2, Bounds.Height - height - 2)), width, height);
    }

    private ((double Lat, double Lon)? Origin, (double Lat, double Lon)? Target) _routeKey;
    private Vector3[]? _worldRoute;
    private GlobeProjection _routeProjection;
    private StreamGeometry? _route, _brokenRoute;
    private Point _routeCenter;
    private double _routeRadius;
    private int _routeVersion;

    private void UpdateRoute(Point center, double radius, GlobeProjection projection,
        (double Lat, double Lon)? origin, (double Lat, double Lon)? target)
    {
        var key = (origin, target);
        if (_routeKey != key)
        {
            _routeKey = key;
            _routeRadius = 0;
            _worldRoute = origin is { } a && target is { } b && !Same(a, b.Lat, b.Lon)
                ? BuildRoute(a, b)
                : null;
            _routePoints = _worldRoute == null ? null : new Point?[_worldRoute.Length];
        }
        if (_routeRadius == radius && _routeCenter == center && _routeProjection == projection)
        {
            return;
        }
        _routeCenter = center;
        _routeRadius = radius;
        _routeProjection = projection;
        _routeVersion++;
        if (_worldRoute == null || _routePoints == null)
        {
            _route = _brokenRoute = null;
            return;
        }
        for (var i = 0; i < _worldRoute.Length; i++)
        {
            var point = _worldRoute[i];
            _routePoints[i] = Vector3.Dot(point, projection.Forward) > 0
                ? projection.Project(point, center, radius) : null;
        }
        _route = MakeRoute(1);
        _brokenRoute = null;
    }

    private static Vector3[]? BuildRoute((double Lat, double Lon) from, (double Lat, double Lon) to)
    {
        var a = ToXyz(from.Lat, from.Lon);
        var b = ToXyz(to.Lat, to.Lon);
        var omega = Math.Acos(Math.Clamp(Vector3.Dot(a, b), -1, 1));
        var sin = Math.Sin(omega);
        if (sin < 0.00001)
        {
            return null;
        }
        const int steps = 56;
        var route = new Vector3[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            var point = (a * (float)(Math.Sin((1 - t) * omega) / sin)) + (b * (float)(Math.Sin(t * omega) / sin));
            var lift = 1 + (0.16 * Math.Sin(Math.PI * t));
            route[i] = point * (float)lift;
        }
        return route;
    }

    private StreamGeometry MakeRoute(double progress, bool broken = false)
    {
        var points = _routePoints!;
        var steps = points.Length - 1;
        var end = (int)(progress * steps);
        var route = new StreamGeometry();
        using var geometry = route.Open();
        var open = false;
        for (var i = 0; i <= end; i++)
        {
            if (points[i] is { } point && !(broken && i >= (int)(steps * 0.42) && i <= (int)(steps * 0.58)))
            {
                if (open)
                {
                    geometry.LineTo(point);
                }
                else
                {
                    geometry.BeginFigure(point, false);
                    open = true;
                }
            }
            else if (open)
            {
                geometry.EndFigure(false);
                open = false;
            }
        }
        if (open)
        {
            if (progress < 1 && PointOnRoute(progress) is { } tip)
            {
                geometry.LineTo(tip);
            }
            geometry.EndFigure(false);
        }
        return route;
    }

    private void DrawRoute(DrawingContext context, Color line, Color accent, Color bad, double age)
    {
        if (_route == null)
        {
            return;
        }
        if (Lost)
        {
            _brokenRoute ??= MakeRoute(1, broken: true);
            context.DrawGeometry(null, new Pen(new SolidColorBrush(bad, 0.8), 1.8, new DashStyle([3, 4], 0), PenLineCap.Round), _brokenRoute);
            return;
        }
        if (Connected)
        {
            StrokeRoute(context, _route, line, 0.15, 8);
            StrokeRoute(context, _route, line, 0.95, 2);
            if (AppMotion.Enabled && age < TraceMs)
            {
                DrawPulse(context, age / TraceMs, Colors.White, 0.9);
            }
            return;
        }

        var progress = AppMotion.Enabled && Checking ? Math.Min(1, age / TraceMs) : 0;
        var waiting = new Pen(new SolidColorBrush(accent, 0.35), 1.2, new DashStyle([2, 5], 0), PenLineCap.Round);
        context.DrawGeometry(null, waiting, _route);
        if (progress > 0)
        {
            var trace = progress < 1 ? MakeRoute(progress) : _route;
            StrokeRoute(context, trace, accent, 0.22, 6);
            StrokeRoute(context, trace, accent, 0.85, 2);
            var phase = ((age - TraceMs) % 1450) / 1450;
            var strength = progress < 1 ? 1 : 0.45 * Math.Sin(Math.PI * phase);
            DrawPulse(context, progress < 1 ? progress : phase, Colors.White, strength);
        }
    }

    private static void StrokeRoute(DrawingContext context, Geometry route, Color color, double opacity, double width)
        => context.DrawGeometry(null, new Pen(new SolidColorBrush(color, opacity), width, lineCap: PenLineCap.Round), route);

    private Point? PointOnRoute(double progress)
    {
        var points = _routePoints!;
        var index = progress * (points.Length - 1);
        var left = (int)index;
        var fraction = index - left;
        if (fraction == 0)
        {
            return points[left];
        }
        return points[left] is { } a && points[left + 1] is { } b
            ? new Point(a.X + ((b.X - a.X) * fraction), a.Y + ((b.Y - a.Y) * fraction))
            : null;
    }

    private void DrawPulse(DrawingContext context, double progress, Color color, double strength)
    {
        if (strength <= 0 || PointOnRoute(progress) is not { } head)
        {
            return;
        }
        Point? end = head;
        for (var i = 1; i <= 10 && end != null; i++)
        {
            var next = PointOnRoute(Math.Max(0, progress - (0.017 * i)));
            if (next is { } p)
            {
                var fade = strength * (1 - (i / 11.0));
                context.DrawLine(new Pen(new SolidColorBrush(color, fade), 2.3, lineCap: PenLineCap.Round), end.Value, p);
            }
            end = next;
        }
        context.DrawEllipse(new SolidColorBrush(color, 0.2 * strength), null, head, 8, 8);
        context.DrawEllipse(new SolidColorBrush(color, strength), null, head, 3, 3);
    }

}
