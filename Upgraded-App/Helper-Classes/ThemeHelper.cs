using System;
using System.Windows;

public static class ThemeHelper
{
    public static void ThemeSwap(bool enabled)
    {
        string themeFileName = enabled ? "HighContrastTheme.xaml" : "NormalTheme.xaml";
        try
        {
            ThemeSwapHelper(themeFileName);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply theme '{themeFileName}': {ex.Message}");
            MessageBox.Show("Failed to load theme. Please restart the application.",
                            "Theme Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void ThemeSwapHelper(string themeFileName)
    {
        if (Application.Current == null) return;

        var mergedDictionaries = Application.Current.Resources.MergedDictionaries;

        for (int i = mergedDictionaries.Count - 1; i >= 0; i--)
        {
            var src = mergedDictionaries[i].Source?.OriginalString ?? string.Empty;
            if (src.EndsWith("NormalTheme.xaml", StringComparison.OrdinalIgnoreCase)
                || src.EndsWith("HighContrastTheme.xaml", StringComparison.OrdinalIgnoreCase))
            {
                mergedDictionaries.RemoveAt(i);
            }
        }

        var newTheme = new ResourceDictionary
        {
            Source = new Uri($"Themes/{themeFileName}", UriKind.Relative)
        };
        mergedDictionaries.Add(newTheme); // Add last so it takes precedence
    }
}