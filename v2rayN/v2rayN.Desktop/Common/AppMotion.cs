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
