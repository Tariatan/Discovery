using System.Configuration;
using System.Windows;

namespace Discovery.Properties;

internal sealed class Settings : ApplicationSettingsBase
{
    private static readonly Settings s_DefaultInstance = (Settings)Synchronized(new Settings());

    public static Settings Default => s_DefaultInstance;

    [UserScopedSetting]
    [DefaultSettingValue("0,0")]
    [SettingsSerializeAs(SettingsSerializeAs.String)]
    public Point FormLocation
    {
        get => this[nameof(FormLocation)] is Point point ? point : new Point(0, 0);
        set => this[nameof(FormLocation)] = value;
    }
}
