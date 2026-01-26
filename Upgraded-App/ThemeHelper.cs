// **************************************************
// ***********************************
// File: ThemeHelper.cs
// Description: Manages application theme and high contrast mode
// Author: Benjamin Kerr
// 2025 - 2026
// ***********************************
// **************************************************

using System;
using System.Windows;

namespace FishLens_App
{
    public static class ThemeHelper
    {
        // **************************************************
        // Function: ApplyHighContrastMode
        // Description: Swap application theme to high-contrast or normal.
        // **************************************************
        public static void ApplyHighContrastMode(bool enabled)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        SwapThemeDictionary(enabled ? "HighContrastTheme.xaml" : "NormalTheme.xaml");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Theme swap failed: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to apply high contrast mode: {ex.Message}");
            }
        }

        // **************************************************
        // Function: SwapThemeDictionary
        // Description: Replace existing theme dictionaries and load the requested one.
        // **************************************************
        private static void SwapThemeDictionary(string themeFileName)
        {
            if (Application.Current == null) return;

            var appResources = Application.Current.Resources;
            if (appResources == null) return;

            var merged = appResources.MergedDictionaries;

            // Remove any previously loaded theme dictionaries located in Themes/ folder
            for (int i = merged.Count - 1; i >= 0; i--)
            {
                try
                {
                    var src = merged[i].Source?.OriginalString ?? string.Empty;
                    if (src.StartsWith("Themes/", StringComparison.OrdinalIgnoreCase) || src.Contains("/Themes/"))
                    {
                        merged.RemoveAt(i);
                    }
                }
                catch { /* ignore malformed entries */ }
            }

            // Load and add the requested theme dictionary
            var rd = new ResourceDictionary { Source = new Uri($"Themes/{themeFileName}", UriKind.Relative) };
            merged.Add(rd);
        }
    }
}