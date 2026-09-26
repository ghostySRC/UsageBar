using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace UsageBar.Windows.Services;

public static class ThemeManager
{
    public static void Apply()
    {
        var isLight = !IsDarkMode();

        var resources = Application.Current.Resources;
        resources["SurfaceBrush"] = Brush(isLight ? "#F7F7F8" : "#242629");
        resources["TextBrush"] = Brush(isLight ? "#202124" : "#F3F3F4");
        resources["MutedBrush"] = Brush(isLight ? "#686B70" : "#AFB1B5");
        resources["TrackBrush"] = Brush(isLight ? "#D9DCE1" : "#494B50");
        resources["AccentBrush"] = Brush(isLight ? "#2563EB" : "#76A7FF");
    }

    public static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) == 0;
        }
        catch { return false; }
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color)!);
}
