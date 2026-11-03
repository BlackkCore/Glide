using Avalonia.Data.Converters;

namespace v2rayN.Desktop.Converters;

/// <summary>
/// Name of a row in the update window ("Glide", "sing-box", "Rule files") or, with parameter
/// "caption", what it is ("The app itself", "Proxy engine", …). Display only.
/// </summary>
public class UpdateItemNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CheckUpdateModel item)
        {
            return string.Empty;
        }
        var caption = parameter as string == "caption";
        if (item.IsGeoFile)
        {
            return caption ? ResUI.TipUpdateKindRules : ResUI.TbUpdateKindRules;
        }
        if (item.CoreType == ECoreType.v2rayN)
        {
            return caption ? ResUI.TbUpdateKindApp : Global.AppDisplayName;
        }
        return caption ? ResUI.TbUpdateKindEngine : item.CoreType?.ToString().Replace('_', '-') ?? string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
