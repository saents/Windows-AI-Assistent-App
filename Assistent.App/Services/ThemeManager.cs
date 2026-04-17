using System.Windows;
using Assistent.App.Settings;

namespace Assistent.App.Services;

public static class ThemeManager
{
    public static void Apply(Application app, AppTheme requested)
    {
        var effective = requested == AppTheme.System ? SystemThemeResolver.Resolve() : requested;
        var pack = effective == AppTheme.Dark
            ? "pack://application:,,,/Themes/Dark.xaml"
            : "pack://application:,,,/Themes/Light.xaml";

        var merged = app.Resources.MergedDictionaries;
        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var src = merged[i].Source?.OriginalString;
            if (src != null && src.Contains("Themes/", StringComparison.OrdinalIgnoreCase))
                merged.RemoveAt(i);
        }

        merged.Insert(0, new ResourceDictionary { Source = new Uri(pack, UriKind.Absolute) });
    }
}
