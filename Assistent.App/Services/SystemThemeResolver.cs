using Assistent.App.Settings;
using Microsoft.Win32;

namespace Assistent.App.Services;

public static class SystemThemeResolver
{
    public static AppTheme Resolve()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false);
            var light = key?.GetValue("AppsUseLightTheme");
            if (light is int v && v == 0)
                return AppTheme.Dark;
        }
        catch
        {
            /* ignore */
        }

        return AppTheme.Light;
    }
}
