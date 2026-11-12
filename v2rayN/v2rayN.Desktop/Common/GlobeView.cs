using System.Globalization;
using System.Net.NetworkInformation;

namespace v2rayN.Desktop.Common;

/// <summary>
/// Country names for the globe's labels. Persian names are listed here because the system's region
/// data would give them in another language.
/// </summary>
public static class CountryNames
{
    private static readonly Dictionary<string, string> Persian = new(StringComparer.OrdinalIgnoreCase)
    {
        ["US"] = "آمریکا", ["CA"] = "کانادا", ["MX"] = "مکزیک", ["BR"] = "برزیل", ["AR"] = "آرژانتین",
        ["CL"] = "شیلی", ["CO"] = "کلمبیا", ["PE"] = "پرو", ["GB"] = "بریتانیا", ["UK"] = "بریتانیا",
        ["IE"] = "ایرلند", ["FR"] = "فرانسه", ["DE"] = "آلمان", ["NL"] = "هلند", ["BE"] = "بلژیک",
        ["LU"] = "لوکزامبورگ", ["CH"] = "سوئیس", ["AT"] = "اتریش", ["IT"] = "ایتالیا", ["ES"] = "اسپانیا",
        ["PT"] = "پرتغال", ["SE"] = "سوئد", ["NO"] = "نروژ", ["FI"] = "فنلاند", ["DK"] = "دانمارک",
        ["IS"] = "ایسلند", ["PL"] = "لهستان", ["CZ"] = "چک", ["SK"] = "اسلواکی", ["HU"] = "مجارستان",
        ["RO"] = "رومانی", ["BG"] = "بلغارستان", ["GR"] = "یونان", ["RS"] = "صربستان", ["HR"] = "کرواسی",
        ["SI"] = "اسلوونی", ["UA"] = "اوکراین", ["MD"] = "مولداوی", ["LT"] = "لیتوانی", ["LV"] = "لتونی",
        ["EE"] = "استونی", ["RU"] = "روسیه", ["BY"] = "بلاروس", ["TR"] = "ترکیه", ["CY"] = "قبرس",
        ["IL"] = "اسرائیل", ["AE"] = "امارات", ["SA"] = "عربستان", ["QA"] = "قطر", ["IR"] = "ایران",
        ["IQ"] = "عراق", ["EG"] = "مصر", ["ZA"] = "آفریقای جنوبی", ["NG"] = "نیجریه", ["KE"] = "کنیا",
        ["MA"] = "مراکش", ["IN"] = "هند", ["PK"] = "پاکستان", ["BD"] = "بنگلادش", ["KZ"] = "قزاقستان",
        ["CN"] = "چین", ["HK"] = "هنگ‌کنگ", ["TW"] = "تایوان", ["JP"] = "ژاپن", ["KR"] = "کره جنوبی",
        ["SG"] = "سنگاپور", ["MY"] = "مالزی", ["TH"] = "تایلند", ["VN"] = "ویتنام", ["ID"] = "اندونزی",
        ["PH"] = "فیلیپین", ["AU"] = "استرالیا", ["NZ"] = "نیوزیلند", ["AM"] = "ارمنستان", ["GE"] = "گرجستان",
        ["AZ"] = "آذربایجان",
    };

    public static bool PersianUi
    {
        get
        {
            try
            {
                return AppManager.Instance.Config?.UiItem?.CurrentLanguage?.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ?? false;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>The country's name in the interface language (Persian, else English).</summary>
    public static string Get(string? code)
    {
        if (code.IsNullOrEmpty())
        {
            return string.Empty;
        }
        if (PersianUi && Persian.TryGetValue(code, out var fa))
        {
            return fa;
        }
        try
        {
            return new RegionInfo(code.Equals("UK", StringComparison.OrdinalIgnoreCase) ? "GB" : code).EnglishName;
        }
        catch
        {
            return code;
        }
    }
}

/// <summary>
/// Where this computer is right now, for the start of the globe route, detected automatically from the network.
/// Lookups run on every app start and whenever the network changes, never through the system proxy. With
/// Glide's TUN on they go around the tunnel through the physical Wi-Fi/Ethernet adapter (skipped when there
/// is none); answers that match the proxy exit are rejected, and results are cached for minutes in
/// "glide-origin.json". When detection fails or hasn't finished, no country is guessed (never falls back to
/// time zone or Windows region).
/// To switch the lookup off completely, create an empty file named "glide-no-locate" in the config folder.
/// </summary>
public static class GlobeOrigin
{
    private static readonly object Gate = new();
    private static bool _uiProxyActive;
    private static string? _exitCountry;
    private static string? _exitIp;
    private static CancellationTokenSource? _networkDebounceCts;

    private static readonly OriginLocationService Service = new(
        isProxyActive: () =>
        {
            lock (Gate)
            {
                if (_uiProxyActive)
                {
                    return true;
                }
            }
            return OriginLocationService.DefaultIsGlideProxyActive();
        },
        getExitInfo: () =>
        {
            lock (Gate)
            {
                return (_exitCountry, _exitIp);
            }
        });

    public static event Action? Changed;

    static GlobeOrigin()
    {
        Service.Changed += () => Changed?.Invoke();
        try
        {
            NetworkChange.NetworkAddressChanged += OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        }
        catch
        {
            // Ignore if network change notifications are unavailable on the host OS.
        }
    }

    private static void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable)
        {
            ScheduleNetworkRefresh(2000);
        }
    }

    private static void OnNetworkChanged(object? sender, EventArgs e)
    {
        ScheduleNetworkRefresh(2000);
    }

    private static void ScheduleNetworkRefresh(int delayMs)
    {
        CancellationToken token;
        lock (Gate)
        {
            _networkDebounceCts?.Cancel();
            _networkDebounceCts?.Dispose();
            _networkDebounceCts = new CancellationTokenSource();
            token = _networkDebounceCts.Token;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, token).ConfigureAwait(false);
                if (!token.IsCancellationRequested)
                {
                    await RefreshAsync(force: true).ConfigureAwait(false);
                }
            }
            catch
            {
                // Debounced or cancelled.
            }
        });
    }

    /// <summary>Updates the active proxy/TUN state and exit IP info so origin lookups never use or accept proxy exits.</summary>
    public static void SetProxyState(bool active, string? exitCountry = null, string? exitIp = null)
    {
        bool becameInactive;
        lock (Gate)
        {
            becameInactive = _uiProxyActive && !active;
            _uiProxyActive = active;
            _exitCountry = exitCountry;
            _exitIp = exitIp;
        }
        if (!string.IsNullOrEmpty(exitCountry) || !string.IsNullOrEmpty(exitIp))
        {
            Service.NotifyExitInfo(exitCountry, exitIp);
        }
        if (becameInactive)
        {
            ScheduleNetworkRefresh(1800);
        }
    }

    /// <summary>Asks for a fresh network location while Glide's proxy is off.</summary>
    public static Task RefreshAsync(bool force = false) => Service.RefreshAsync(force);

    /// <summary>The exact position when the lookup gave one.</summary>
    public static (double Lat, double Lon)? Position => Service.Position;

    /// <summary>The country code the route starts from, or null when nothing is known.</summary>
    public static string? Code => Service.Code;

    /// <summary>True while a network location lookup is in flight.</summary>
    public static bool IsLocating => Service.IsLocating;

    /// <summary>Human-readable source description for tooltips (e.g. "from your network, 10:41").</summary>
    public static string? SourceTooltip => Service.FormatSourceTooltip(CountryNames.PersianUi);
}
