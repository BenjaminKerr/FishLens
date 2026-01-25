// **************************************************
// ***********************************
// File: ThemeHelper.cs
// Description: Manages application theme and high contrast mode
// Author: Benjamin Kerr
// 2025 - 2026
// ***********************************
// **************************************************

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FishLens_App
{
    /// <summary>
    /// Provides theme management functionality including high contrast mode
    /// </summary>
    public static class ThemeHelper
    {
        // Store original property values so we can restore them exactly
        private static readonly Dictionary<DependencyObject, Dictionary<DependencyProperty, object>> _originalValues = new();

        private static void SaveAndSet(DependencyObject obj, DependencyProperty prop, object value)
        {
            if (obj == null || prop == null) return;

            if (!_originalValues.TryGetValue(obj, out var dict))
            {
                dict = new Dictionary<DependencyProperty, object>();
                _originalValues[obj] = dict;
            }

            if (!dict.ContainsKey(prop))
            {
                dict[prop] = obj.GetValue(prop);
            }

            obj.SetValue(prop, value);
        }

        private static void RestoreAll()
        {
            foreach (var kv in _originalValues)
            {
                var obj = kv.Key;
                foreach (var dpKv in kv.Value)
                {
                    try
                    {
                        obj.SetValue(dpKv.Key, dpKv.Value);
                    }
                    catch { /* swallow to avoid restore-time crashes */ }
                }
            }

            _originalValues.Clear();
        }

        #region Public Methods

        // **************************************************
        // Function: ApplyHighContrastMode
        // Description: Applies comprehensive high contrast styling to the application
        // **************************************************
        public static void ApplyHighContrastMode(bool enabled)
        {
            try
            {
                var main = Application.Current.MainWindow as MainWindow;
                if (main == null) return;

                main.Dispatcher.Invoke(() =>
                {
                    if (enabled)
                    {
                        ApplyHighContrastColors(main);
                        ApplyHighContrastToPages();
                    }
                    else
                    {
                        ApplyNormalColors(main);
                        ApplyNormalToPages();
                    }
                });
            }
            catch (Exception ex)
            {
                // Log error if logger is available
                System.Diagnostics.Debug.WriteLine($"Failed to apply high contrast mode: {ex.Message}");
            }
        }

        #endregion

        #region High Contrast Mode

        // **************************************************
        // Function: ApplyHighContrastColors
        // Description: Applies high contrast color scheme
        // **************************************************
        private static void ApplyHighContrastColors(MainWindow main)
        {
            // Main window background - pure black (save original then set)
            SaveAndSet(main, Window.BackgroundProperty, new SolidColorBrush(Colors.Black));

            // Sidebar - try both casing variants used in XAML
            var sidebar = main.FindName("Sidebar") as Border ?? main.FindName("SideBar") as Border;
            if (sidebar != null)
            {
                SaveAndSet(sidebar, Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(10, 10, 10)));
                SaveAndSet(sidebar, Border.BorderBrushProperty, new SolidColorBrush(Colors.White));
                SaveAndSet(sidebar, Border.BorderThicknessProperty, new Thickness(0, 0, 3, 0));
            }

            // Title - bright white
            var titleTb = main.FindName("Title") as TextBlock;
            if (titleTb != null)
            {
                SaveAndSet(titleTb, TextBlock.ForegroundProperty, new SolidColorBrush(Colors.White));
            }

            // Video name and metadata - bright cyan for visibility
            var videoNameTb = main.FindName("videoName") as TextBlock;
            if (videoNameTb != null)
            {
                SaveAndSet(videoNameTb, TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0, 255, 255))); // Cyan
            }

            var videoDateTime = main.FindName("videoDateTime") as TextBlock;
            if (videoDateTime != null)
            {
                SaveAndSet(videoDateTime, TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0, 255, 255))); // Cyan
            }

            // Confidence text - bright white
            var fishPresentConf = main.FindName("fishPresentConfidence") as TextBlock;
            if (fishPresentConf != null)
            {
                SaveAndSet(fishPresentConf, TextBlock.ForegroundProperty, new SolidColorBrush(Colors.White));
            }

            var fishSpeciesConf = main.FindName("fishSpeciesConfidence") as TextBlock;
            if (fishSpeciesConf != null)
            {
                SaveAndSet(fishSpeciesConf, TextBlock.ForegroundProperty, new SolidColorBrush(Colors.White));
            }

            // Apply conservative changes to a few named controls (buttons/inputs) only
            ApplyHighContrastToButtons(main);
            ApplyHighContrastToNavigation(main);
            ApplyHighContrastToInputs(main);
        }

        // **************************************************
        // Function: ApplyHighContrastToContentPanels
        // Description: Styles content panels for high contrast
        // **************************************************
        private static void ApplyHighContrastToContentPanels(MainWindow main)
        {
            // Find all borders (content panels) in main grid
            var mainGrid = main.Content as Grid;
            if (mainGrid == null) return;

            // Intentionally conservative: do not mutate every Border in the visual tree.
            // Only set text color for known data areas to avoid breaking control templates.
            var dataPanel = main.FindName("dataPanel") as StackPanel;
            if (dataPanel != null)
            {
                ApplyHighContrastToElement(dataPanel);
            }
        }

        // **************************************************
        // Function: ApplyHighContrastToElement
        // Description: Recursively applies high contrast to an element and its children
        // **************************************************
        private static void ApplyHighContrastToElement(DependencyObject element)
        {
            if (element == null) return;

            int childCount = VisualTreeHelper.GetChildrenCount(element);

            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);

                if (child is TextBlock textBlock)
                {
                    SaveAndSet(textBlock, TextBlock.ForegroundProperty, new SolidColorBrush(Colors.White));
                }
                else if (child is Label label)
                {
                    SaveAndSet(label, Label.ForegroundProperty, new SolidColorBrush(Colors.White));
                }

                ApplyHighContrastToElement(child);
            }
        }

        // **************************************************
        // Function: ApplyHighContrastToButtons
        // Description: Applies high contrast button styling
        // **************************************************
        private static void ApplyHighContrastToButtons(MainWindow main)
        {
            // Buttons - set high-contrast styling only on known named buttons, saving originals
            var openFolderBtn = main.FindName("openFolder") as Button;
            if (openFolderBtn != null)
            {
                SaveAndSet(openFolderBtn, Button.BackgroundProperty, new SolidColorBrush(Colors.Black));
                SaveAndSet(openFolderBtn, Button.ForegroundProperty, new SolidColorBrush(Colors.Yellow));
                SaveAndSet(openFolderBtn, Button.BorderBrushProperty, new SolidColorBrush(Colors.Yellow));
                SaveAndSet(openFolderBtn, Button.BorderThicknessProperty, new Thickness(2));
            }

            var exportBtn = main.FindName("exportData") as Button;
            if (exportBtn != null)
            {
                SaveAndSet(exportBtn, Button.BackgroundProperty, new SolidColorBrush(Colors.Black));
                SaveAndSet(exportBtn, Button.ForegroundProperty, new SolidColorBrush(Colors.Yellow));
                SaveAndSet(exportBtn, Button.BorderBrushProperty, new SolidColorBrush(Colors.Yellow));
                SaveAndSet(exportBtn, Button.BorderThicknessProperty, new Thickness(2));
            }

            var saveBtn = main.FindName("saveButton") as Button;
            if (saveBtn != null)
            {
                SaveAndSet(saveBtn, Button.BackgroundProperty, new SolidColorBrush(Colors.Black));
                SaveAndSet(saveBtn, Button.ForegroundProperty, new SolidColorBrush(Colors.Yellow));
                SaveAndSet(saveBtn, Button.BorderBrushProperty, new SolidColorBrush(Colors.Yellow));
                SaveAndSet(saveBtn, Button.BorderThicknessProperty, new Thickness(2));
            }
        }

        // **************************************************
        // Function: ApplyHighContrastToNavigation
        // Description: Styles navigation icons for high contrast
        // **************************************************
        private static void ApplyHighContrastToNavigation(MainWindow main)
        {
            var homeBtn = main.FindName("Home") as Button;
            var historyBtn = main.FindName("History") as Button;
            var settingsBtn = main.FindName("Settings") as Button;

            if (homeBtn != null)
            {
                SaveAndSet(homeBtn, Button.ForegroundProperty, new SolidColorBrush(Colors.Yellow));
                SaveAndSet(homeBtn, Button.BorderBrushProperty, new SolidColorBrush(Colors.Yellow));
                SaveAndSet(homeBtn, Button.BorderThicknessProperty, new Thickness(2));
            }

            if (historyBtn != null)
            {
                SaveAndSet(historyBtn, Button.ForegroundProperty, new SolidColorBrush(Colors.Yellow));
                SaveAndSet(historyBtn, Button.BorderBrushProperty, new SolidColorBrush(Colors.Yellow));
                SaveAndSet(historyBtn, Button.BorderThicknessProperty, new Thickness(2));
            }

            if (settingsBtn != null)
            {
                SaveAndSet(settingsBtn, Button.ForegroundProperty, new SolidColorBrush(Colors.Yellow));
                SaveAndSet(settingsBtn, Button.BorderBrushProperty, new SolidColorBrush(Colors.Yellow));
                SaveAndSet(settingsBtn, Button.BorderThicknessProperty, new Thickness(2));
            }
        }

        // **************************************************
        // Function: ApplyHighContrastToInputs
        // Description: Styles input controls for high contrast
        // **************************************************
        private static void ApplyHighContrastToInputs(MainWindow main)
        {
            // ComboBoxes
            var fishPresentStatus = main.FindName("fishPresentStatus") as ComboBox;
            if (fishPresentStatus != null)
            {
                SaveAndSet(fishPresentStatus, ComboBox.BackgroundProperty, new SolidColorBrush(Color.FromRgb(30, 30, 30)));
                SaveAndSet(fishPresentStatus, ComboBox.ForegroundProperty, new SolidColorBrush(Colors.White));
                SaveAndSet(fishPresentStatus, ComboBox.BorderBrushProperty, new SolidColorBrush(Colors.White));
                SaveAndSet(fishPresentStatus, ComboBox.BorderThicknessProperty, new Thickness(2));
            }

            var travelDirection = main.FindName("travelDirection") as ComboBox;
            if (travelDirection != null)
            {
                SaveAndSet(travelDirection, ComboBox.BackgroundProperty, new SolidColorBrush(Color.FromRgb(30, 30, 30)));
                SaveAndSet(travelDirection, ComboBox.ForegroundProperty, new SolidColorBrush(Colors.White));
                SaveAndSet(travelDirection, ComboBox.BorderBrushProperty, new SolidColorBrush(Colors.White));
                SaveAndSet(travelDirection, ComboBox.BorderThicknessProperty, new Thickness(2));
            }

            // TextBox
            var fishSpecies = main.FindName("fishSpecies") as TextBox;
            if (fishSpecies != null)
            {
                SaveAndSet(fishSpecies, TextBox.BackgroundProperty, new SolidColorBrush(Color.FromRgb(30, 30, 30)));
                SaveAndSet(fishSpecies, TextBox.ForegroundProperty, new SolidColorBrush(Colors.White));
                SaveAndSet(fishSpecies, TextBox.BorderBrushProperty, new SolidColorBrush(Colors.White));
                SaveAndSet(fishSpecies, TextBox.BorderThicknessProperty, new Thickness(2));
            }
        }

        // **************************************************
        // Function: ApplyHighContrastToPages
        // Description: Applies high contrast to Settings and History pages
        // **************************************************
        private static void ApplyHighContrastToPages()
        {
            var main = Application.Current.MainWindow as MainWindow;
            if (main == null) return;

            var frame = main.FindName("MainFrame") as Frame;
            if (frame?.Content != null)
            {
                // Apply to current page content
                if (frame.Content is Page page)
                {
                    SaveAndSet(page, Page.BackgroundProperty, new SolidColorBrush(Colors.Black));
                    ApplyHighContrastToPageElements(page);
                }
            }
        }

        // **************************************************
        // Function: ApplyHighContrastToPageElements
        // Description: Recursively applies high contrast to page elements
        // **************************************************
        private static void ApplyHighContrastToPageElements(DependencyObject parent)
        {
            if (parent == null) return;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // Apply to different control types
                if (child is TextBlock textBlock)
                {
                    SaveAndSet(textBlock, TextBlock.ForegroundProperty, new SolidColorBrush(Colors.White));
                }
                else if (child is Label label)
                {
                    SaveAndSet(label, Label.ForegroundProperty, new SolidColorBrush(Colors.White));
                }
                else if (child is Button button)
                {
                    SaveAndSet(button, Button.BackgroundProperty, new SolidColorBrush(Colors.Black));
                    SaveAndSet(button, Button.ForegroundProperty, new SolidColorBrush(Colors.Yellow));
                    SaveAndSet(button, Button.BorderBrushProperty, new SolidColorBrush(Colors.Yellow));
                    SaveAndSet(button, Button.BorderThicknessProperty, new Thickness(2));
                }
                else if (child is Border border)
                {
                    SaveAndSet(border, Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(45, 45, 45)));
                    SaveAndSet(border, Border.BorderBrushProperty, new SolidColorBrush(Colors.White));
                    SaveAndSet(border, Border.BorderThicknessProperty, new Thickness(2));
                }
                else if (child is ComboBox comboBox)
                {
                    SaveAndSet(comboBox, ComboBox.BackgroundProperty, new SolidColorBrush(Color.FromRgb(30, 30, 30)));
                    SaveAndSet(comboBox, ComboBox.ForegroundProperty, new SolidColorBrush(Colors.White));
                    SaveAndSet(comboBox, ComboBox.BorderBrushProperty, new SolidColorBrush(Colors.White));
                    SaveAndSet(comboBox, ComboBox.BorderThicknessProperty, new Thickness(2));
                }
                else if (child is TextBox textBox)
                {
                    SaveAndSet(textBox, TextBox.BackgroundProperty, new SolidColorBrush(Color.FromRgb(30, 30, 30)));
                    SaveAndSet(textBox, TextBox.ForegroundProperty, new SolidColorBrush(Colors.White));
                    SaveAndSet(textBox, TextBox.BorderBrushProperty, new SolidColorBrush(Colors.White));
                    SaveAndSet(textBox, TextBox.BorderThicknessProperty, new Thickness(2));
                }
                else if (child is CheckBox checkBox)
                {
                    SaveAndSet(checkBox, CheckBox.ForegroundProperty, new SolidColorBrush(Colors.White));
                }
                else if (child is Slider slider)
                {
                    SaveAndSet(slider, Slider.ForegroundProperty, new SolidColorBrush(Colors.Yellow));
                }

                // Recursively apply to children
                ApplyHighContrastToPageElements(child);
            }
        }

        #endregion

        #region Normal Mode

        // **************************************************
        // Function: ApplyNormalColors
        // Description: Restores normal color scheme
        // **************************************************
        private static void ApplyNormalColors(MainWindow main)
        {
            // Restore any values previously saved by SaveAndSet
            RestoreAll();

            // Main window background
            main.Background = (SolidColorBrush)(new BrushConverter().ConvertFrom("#E8F4F8"));

            // Sidebar
            var sidebar = main.FindName("Sidebar") as Border;
            if (sidebar != null)
            {
                sidebar.Background = (SolidColorBrush)(new BrushConverter().ConvertFrom("#1B4F5C"));
                sidebar.BorderBrush = (SolidColorBrush)(new BrushConverter().ConvertFrom("#0D3640"));
                sidebar.BorderThickness = new Thickness(0, 0, 1, 0);
            }

            // Title
            var titleTb = main.FindName("Title") as TextBlock;
            if (titleTb != null)
            {
                titleTb.Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#0D3640"));
            }

            // Video metadata
            var videoNameTb = main.FindName("videoName") as TextBlock;
            if (videoNameTb != null)
            {
                videoNameTb.Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#8BA5AE"));
            }

            var videoDateTime = main.FindName("videoDateTime") as TextBlock;
            if (videoDateTime != null)
            {
                videoDateTime.Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#8BA5AE"));
            }

            // Confidence text
            var fishPresentConf = main.FindName("fishPresentConfidence") as TextBlock;
            if (fishPresentConf != null)
            {
                fishPresentConf.Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#0D3640"));
            }

            var fishSpeciesConf = main.FindName("fishSpeciesConfidence") as TextBlock;
            if (fishSpeciesConf != null)
            {
                fishSpeciesConf.Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#0D3640"));
            }

            // Restore all other original colors
            RestoreOriginalButtonColors(main);
            RestoreOriginalInputColors(main);
            RestoreOriginalPanelColors(main);
        }

        // **************************************************
        // Function: RestoreOriginalButtonColors
        // Description: Restores original button styling
        // **************************************************
        private static void RestoreOriginalButtonColors(MainWindow main)
        {
            var openFolderBtn = main.FindName("openFolder") as Button;
            if (openFolderBtn != null)
            {
                openFolderBtn.Background = (SolidColorBrush)(new BrushConverter().ConvertFrom("#1B4F5C"));
                openFolderBtn.Foreground = new SolidColorBrush(Colors.White);
                openFolderBtn.BorderThickness = new Thickness(0);
            }

            var exportBtn = main.FindName("exportData") as Button;
            if (exportBtn != null)
            {
                exportBtn.Background = (SolidColorBrush)(new BrushConverter().ConvertFrom("#1B4F5C"));
                exportBtn.Foreground = new SolidColorBrush(Colors.White);
                exportBtn.BorderThickness = new Thickness(0);
            }

            var saveBtn = main.FindName("saveButton") as Button;
            if (saveBtn != null)
            {
                saveBtn.Background = (SolidColorBrush)(new BrushConverter().ConvertFrom("#0D3640"));
                saveBtn.Foreground = new SolidColorBrush(Colors.White);
                saveBtn.BorderThickness = new Thickness(0);
            }

            // Navigation buttons
            var homeBtn = main.FindName("Home") as Button;
            var historyBtn = main.FindName("History") as Button;
            var settingsBtn = main.FindName("Settings") as Button;

            if (homeBtn != null)
            {
                homeBtn.Foreground = new SolidColorBrush(Colors.White);
                homeBtn.BorderBrush = new SolidColorBrush(Colors.Transparent);
                homeBtn.BorderThickness = new Thickness(0);
            }

            if (historyBtn != null)
            {
                historyBtn.Foreground = new SolidColorBrush(Colors.White);
                historyBtn.BorderBrush = new SolidColorBrush(Colors.Transparent);
                historyBtn.BorderThickness = new Thickness(0);
            }

            if (settingsBtn != null)
            {
                settingsBtn.Foreground = new SolidColorBrush(Colors.White);
                settingsBtn.BorderBrush = new SolidColorBrush(Colors.Transparent);
                settingsBtn.BorderThickness = new Thickness(0);
            }
        }

        // **************************************************
        // Function: RestoreOriginalInputColors
        // Description: Restores original input control styling
        // **************************************************
        private static void RestoreOriginalInputColors(MainWindow main)
        {
            var fishPresentStatus = main.FindName("fishPresentStatus") as ComboBox;
            if (fishPresentStatus != null)
            {
                fishPresentStatus.Background = new SolidColorBrush(Colors.White);
                fishPresentStatus.Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#0D3640"));
                fishPresentStatus.BorderBrush = (SolidColorBrush)(new BrushConverter().ConvertFrom("#8BA5AE"));
                fishPresentStatus.BorderThickness = new Thickness(1);
            }

            var travelDirection = main.FindName("travelDirection") as ComboBox;
            if (travelDirection != null)
            {
                travelDirection.Background = new SolidColorBrush(Colors.White);
                travelDirection.Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#0D3640"));
                travelDirection.BorderBrush = (SolidColorBrush)(new BrushConverter().ConvertFrom("#8BA5AE"));
                travelDirection.BorderThickness = new Thickness(1);
            }

            var fishSpecies = main.FindName("fishSpecies") as TextBox;
            if (fishSpecies != null)
            {
                fishSpecies.Background = new SolidColorBrush(Colors.White);
                fishSpecies.Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#0D3640"));
                fishSpecies.BorderBrush = (SolidColorBrush)(new BrushConverter().ConvertFrom("#8BA5AE"));
                fishSpecies.BorderThickness = new Thickness(1);
            }
        }

        // **************************************************
        // Function: RestoreOriginalPanelColors
        // Description: Restores original panel styling
        // **************************************************
        private static void RestoreOriginalPanelColors(MainWindow main)
        {
            var mainGrid = main.Content as Grid;
            if (mainGrid == null) return;

            foreach (var child in mainGrid.Children)
            {
                if (child is Border border && border.Name != "Sidebar")
                {
                    border.Background = new SolidColorBrush(Colors.White);
                    border.BorderBrush = (SolidColorBrush)(new BrushConverter().ConvertFrom("#C7D8DD"));
                    border.BorderThickness = new Thickness(1);
                }
            }

            var dataPanel = main.FindName("dataPanel") as StackPanel;
            if (dataPanel != null)
            {
                ApplyNormalToElement(dataPanel);
            }
        }

        // **************************************************
        // Function: ApplyNormalToElement
        // Description: Recursively applies normal colors to an element and its children
        // **************************************************
        private static void ApplyNormalToElement(DependencyObject element)
        {
            if (element == null) return;

            int childCount = VisualTreeHelper.GetChildrenCount(element);

            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);

                if (child is TextBlock textBlock)
                {
                    textBlock.Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#0D3640"));
                }
                else if (child is Label label)
                {
                    label.Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#0D3640"));
                }

                ApplyNormalToElement(child);
            }
        }

        // **************************************************
        // Function: ApplyNormalToPages
        // Description: Restores normal styling to Settings and History pages
        // **************************************************
        private static void ApplyNormalToPages()
        {
            var main = Application.Current.MainWindow as MainWindow;
            if (main == null) return;

            var frame = main.FindName("MainFrame") as Frame;
            if (frame?.Content != null)
            {
                if (frame.Content is Page page)
                {
                    page.Background = new SolidColorBrush(Colors.White);
                    ApplyNormalToPageElements(page);
                }
            }
        }

        // **************************************************
        // Function: ApplyNormalToPageElements
        // Description: Recursively restores normal styling to page elements
        // **************************************************
        private static void ApplyNormalToPageElements(DependencyObject parent)
        {
            if (parent == null) return;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // Restore normal styling - you may need to adjust these colors based on your actual design
                if (child is TextBlock textBlock)
                {
                    textBlock.Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#0D3640"));
                }
                else if (child is Label label)
                {
                    label.Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#0D3640"));
                }
                else if (child is Button button)
                {
                    button.ClearValue(Button.BackgroundProperty);
                    button.ClearValue(Button.ForegroundProperty);
                    button.ClearValue(Button.BorderBrushProperty);
                    button.ClearValue(Button.BorderThicknessProperty);
                }
                else if (child is Border border)
                {
                    border.ClearValue(Border.BackgroundProperty);
                    border.ClearValue(Border.BorderBrushProperty);
                    border.ClearValue(Border.BorderThicknessProperty);
                }
                else if (child is ComboBox comboBox)
                {
                    comboBox.ClearValue(ComboBox.BackgroundProperty);
                    comboBox.ClearValue(ComboBox.ForegroundProperty);
                    comboBox.ClearValue(ComboBox.BorderBrushProperty);
                    comboBox.ClearValue(ComboBox.BorderThicknessProperty);
                }
                else if (child is TextBox textBox)
                {
                    textBox.ClearValue(TextBox.BackgroundProperty);
                    textBox.ClearValue(TextBox.ForegroundProperty);
                    textBox.ClearValue(TextBox.BorderBrushProperty);
                    textBox.ClearValue(TextBox.BorderThicknessProperty);
                }

                ApplyNormalToPageElements(child);
            }
        }

        #endregion
    }
}