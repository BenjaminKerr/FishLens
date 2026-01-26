// **************************************************
// ***********************************
// File: ThemeHelper.cs
// Description: Manages application theme and high contrast mode
// Author: Benjamin Kerr
// 2025 - 2026
// ***********************************
// **************************************************

using DocumentFormat.OpenXml.Drawing;
using System;
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
            // Main window background - pure black
            main.Background = new SolidColorBrush(Colors.Black);

            // Sidebar - very dark with bright border
            var sidebar = main.FindName("Sidebar") as Border;
            if (sidebar != null)
            {
                sidebar.Background = new SolidColorBrush(Color.FromRgb(10, 10, 10));
                sidebar.BorderBrush = new SolidColorBrush(Colors.White);
                sidebar.BorderThickness = new Thickness(0, 0, 3, 0);
            }

            // Title - bright white
            var titleTb = main.FindName("Title") as TextBlock;
            if (titleTb != null)
            {
                titleTb.Foreground = new SolidColorBrush(Colors.White);
            }

            // Video name and metadata - bright cyan for visibility
            var videoNameTb = main.FindName("videoName") as TextBlock;
            if (videoNameTb != null)
            {
                videoNameTb.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 255)); // Cyan
            }

            var videoDateTime = main.FindName("videoDateTime") as TextBlock;
            if (videoDateTime != null)
            {
                videoDateTime.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 255)); // Cyan
            }

            // Confidence text - bright white
            var fishPresentConf = main.FindName("fishPresentConfidence") as TextBlock;
            if (fishPresentConf != null)
            {
                fishPresentConf.Foreground = new SolidColorBrush(Colors.White);
            }

            var fishSpeciesConf = main.FindName("fishSpeciesConfidence") as TextBlock;
            if (fishSpeciesConf != null)
            {
                fishSpeciesConf.Foreground = new SolidColorBrush(Colors.White);
            }

            // Content panels - dark gray background
            ApplyHighContrastToContentPanels(main);

            // Buttons - high contrast styling
            ApplyHighContrastToButtons(main);

            // Navigation icons
            ApplyHighContrastToNavigation(main);

            // Input controls
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

            foreach (var child in mainGrid.Children)
            {
                if (child is Border border && border.Name != "Sidebar")
                {
                    border.Background = new SolidColorBrush(Color.FromRgb(45, 45, 45));
                    border.BorderBrush = new SolidColorBrush(Colors.White);
                    border.BorderThickness = new Thickness(2);
                }
            }

            // Data panel - apply to all text blocks recursively
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
                    textBlock.Foreground = new SolidColorBrush(Colors.White);
                }
                else if (child is Label label)
                {
                    label.Foreground = new SolidColorBrush(Colors.White);
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
            // Open folder button
            var openFolderBtn = main.FindName("openFolder") as Button;
            if (openFolderBtn != null)
            {
                openFolderBtn.Background = new SolidColorBrush(Colors.Black);
                openFolderBtn.Foreground = new SolidColorBrush(Colors.Yellow);
                openFolderBtn.BorderBrush = new SolidColorBrush(Colors.Yellow);
                openFolderBtn.BorderThickness = new Thickness(2);
            }

            // Export data button
            var exportBtn = main.FindName("exportData") as Button;
            if (exportBtn != null)
            {
                exportBtn.Background = new SolidColorBrush(Colors.Black);
                exportBtn.Foreground = new SolidColorBrush(Colors.Yellow);
                exportBtn.BorderBrush = new SolidColorBrush(Colors.Yellow);
                exportBtn.BorderThickness = new Thickness(2);
            }

            // Save button
            var saveBtn = main.FindName("saveButton") as Button;
            if (saveBtn != null)
            {
                saveBtn.Background = new SolidColorBrush(Colors.Black);
                saveBtn.Foreground = new SolidColorBrush(Colors.Yellow);
                saveBtn.BorderBrush = new SolidColorBrush(Colors.Yellow);
                saveBtn.BorderThickness = new Thickness(2);
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
                homeBtn.Foreground = new SolidColorBrush(Colors.Yellow);
                homeBtn.BorderBrush = new SolidColorBrush(Colors.Yellow);
                homeBtn.BorderThickness = new Thickness(2);
            }

            if (historyBtn != null)
            {
                historyBtn.Foreground = new SolidColorBrush(Colors.Yellow);
                historyBtn.BorderBrush = new SolidColorBrush(Colors.Yellow);
                historyBtn.BorderThickness = new Thickness(2);
            }

            if (settingsBtn != null)
            {
                settingsBtn.Foreground = new SolidColorBrush(Colors.Yellow);
                settingsBtn.BorderBrush = new SolidColorBrush(Colors.Yellow);
                settingsBtn.BorderThickness = new Thickness(2);
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
                fishPresentStatus.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
                fishPresentStatus.Foreground = new SolidColorBrush(Colors.White);
                fishPresentStatus.BorderBrush = new SolidColorBrush(Colors.White);
                fishPresentStatus.BorderThickness = new Thickness(2);
            }

            var travelDirection = main.FindName("travelDirection") as ComboBox;
            if (travelDirection != null)
            {
                travelDirection.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
                travelDirection.Foreground = new SolidColorBrush(Colors.White);
                travelDirection.BorderBrush = new SolidColorBrush(Colors.White);
                travelDirection.BorderThickness = new Thickness(2);
            }

            // TextBox
            var fishSpecies = main.FindName("fishSpecies") as TextBox;
            if (fishSpecies != null)
            {
                fishSpecies.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
                fishSpecies.Foreground = new SolidColorBrush(Colors.White);
                fishSpecies.BorderBrush = new SolidColorBrush(Colors.White);
                fishSpecies.BorderThickness = new Thickness(2);
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
                    page.Background = new SolidColorBrush(Colors.Black);
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
                    textBlock.Foreground = new SolidColorBrush(Colors.White);
                }
                else if (child is Label label)
                {
                    label.Foreground = new SolidColorBrush(Colors.White);
                }
                else if (child is Button button)
                {
                    button.Background = new SolidColorBrush(Colors.Black);
                    button.Foreground = new SolidColorBrush(Colors.Yellow);
                    button.BorderBrush = new SolidColorBrush(Colors.Yellow);
                    button.BorderThickness = new Thickness(2);
                }
                else if (child is Border border)
                {
                    border.Background = new SolidColorBrush(Color.FromRgb(45, 45, 45));
                    border.BorderBrush = new SolidColorBrush(Colors.White);
                    border.BorderThickness = new Thickness(2);
                }
                else if (child is ComboBox comboBox)
                {
                    comboBox.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
                    comboBox.Foreground = new SolidColorBrush(Colors.White);
                    comboBox.BorderBrush = new SolidColorBrush(Colors.White);
                    comboBox.BorderThickness = new Thickness(2);
                }
                else if (child is TextBox textBox)
                {
                    textBox.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
                    textBox.Foreground = new SolidColorBrush(Colors.White);
                    textBox.BorderBrush = new SolidColorBrush(Colors.White);
                    textBox.BorderThickness = new Thickness(2);
                }
                else if (child is CheckBox checkBox)
                {
                    checkBox.Foreground = new SolidColorBrush(Colors.White);
                }
                else if (child is Slider slider)
                {
                    slider.Foreground = new SolidColorBrush(Colors.Yellow);
                }

                // Recursively apply to children
                ApplyHighContrastToPageElements(child);
            }
        }

        #endregion

        #region Normal Mode

        private static void ApplyNormalColors(MainWindow main)
        {
            // 1. Window Background
            main.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#E8F4F8");

            // 2. Sidebar (Matching your XAML: #1B4F5C background, #0D3640 border)
            var sidebar = main.FindName("SideBar") as Border;
            if (sidebar != null)
            {
                sidebar.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#1B4F5C");
                sidebar.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#0D3640");
                sidebar.BorderThickness = new Thickness(0, 0, 1, 0);
            }

            // 3. Text Elements
            RestoreTextColors(main);

            // 4. Interactive Elements (Buttons, Inputs)
            RestoreButtonColors(main);
            RestoreInputColors(main);

            // 5. Content Panels (The White Cards)
            RestorePanelColors(main);
        }

        private static void RestoreTextColors(MainWindow main)
        {
            // Deep Teal for Titles
            var title = main.FindName("Title") as TextBlock;
            if (title != null) title.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#0D3640");

            // Gray-Blue for Metadata
            string[] grayText = { "videoName", "videoDateTime" };
            foreach (var name in grayText)
            {
                if (main.FindName(name) is TextBlock tb)
                    tb.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#8BA5AE");
            }

            // Recursive cleanup for any loose text inside dataPanel
            if (main.FindName("dataPanel") is StackPanel panel)
            {
                ApplyNormalTextToChildren(panel);
            }
        }

        private static void RestoreButtonColors(MainWindow main)
        {
            // Sidebar Nav Buttons (Transparent backgrounds, White text)
            string[] navButtons = { "Home", "History", "Settings" };
            foreach (var name in navButtons)
            {
                if (main.FindName(name) is Button btn)
                {
                    btn.Background = Brushes.Transparent;
                    btn.Foreground = Brushes.White;
                }
            }

            // Action Buttons (Teal background, White text)
            string[] tealButtons = { "openFolder", "exportData", "saveButton" };
            foreach (var name in tealButtons)
            {
                if (main.FindName(name) is Button btn)
                {
                    // Note: your saveButton uses #0D3640, others use #1B4F5C
                    string color = (name == "saveButton") ? "#0D3640" : "#1B4F5C";
                    btn.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(color);
                    btn.Foreground = Brushes.White;
                }
            }
        }

        private static void RestoreInputColors(MainWindow main)
        {
            string[] inputs = { "fishPresentStatus", "travelDirection", "fishSpecies" };
            foreach (var name in inputs)
            {
                if (main.FindName(name) is Control ctrl)
                {
                    ctrl.Background = Brushes.White;
                    ctrl.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#0D3640");
                    ctrl.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#8BA5AE");
                }
            }
        }

        private static void RestorePanelColors(MainWindow main)
        {
            // This targets the white "card" borders in your Grid rows 1 and 2
            var mainGrid = main.Content as Grid;
            if (mainGrid == null) return;

            foreach (var child in mainGrid.Children)
            {
                // Find Borders that aren't the Sidebar
                if (child is Border b && b.Name != "SideBar")
                {
                    b.Background = Brushes.White;
                    b.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#C7D8DD");
                    b.BorderThickness = new Thickness(1);
                }
            }
        }

        private static void ApplyNormalTextToChildren(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBlock tb)
                {
                    // Defaulting all panel text back to the Dark Teal
                    tb.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#0D3640");
                }
                ApplyNormalTextToChildren(child);
            }
        }

        // **************************************************
        // Function: ApplyNormalToPages
        // Description: Reverts Settings and History pages to the original light theme
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
                    // Revert Page Background (Matching your XAML: #F5F8FA)
                    page.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#F5F8FA");
                    ApplyNormalToPageElements(page);
                }
            }
        }

        // **************************************************
        // Function: ApplyNormalToPageElements
        // Description: Recursively reverts page-specific controls to normal theme
        // **************************************************
        private static void ApplyNormalToPageElements(DependencyObject parent)
        {
            if (parent == null) return;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            var brushConverter = new BrushConverter();

            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // 1. Restore Text (Deep Teal for headers, Slate for descriptions)
                if (child is TextBlock textBlock)
                {
                    // We check if it's likely a description by its current High Contrast color
                    // or just default everything back to the primary Dark Teal (#0D3640)
                    textBlock.Foreground = (SolidColorBrush)brushConverter.ConvertFrom("#0D3640");
                }

                // 2. Restore Borders and Cards
                else if (child is Border border)
                {
                    // If it's the very top border (Header), keep it Dark Teal
                    if (Grid.GetRow(border) == 0 && border.Parent is Grid)
                    {
                        border.Background = (SolidColorBrush)brushConverter.ConvertFrom("#0D3640");
                    }
                    else
                    {
                        // Otherwise, it's a content "card" (White bg, light gray border)
                        border.Background = Brushes.White;
                        border.BorderBrush = (SolidColorBrush)brushConverter.ConvertFrom("#E1E8ED");
                        border.BorderThickness = new Thickness(1);
                    }
                }

                // 3. Restore Interactive Controls
                else if (child is Button button)
                {
                    button.Background = (SolidColorBrush)brushConverter.ConvertFrom("#0D3640");
                    button.Foreground = Brushes.White;
                    button.BorderThickness = new Thickness(0);
                }
                else if (child is ComboBox comboBox)
                {
                    comboBox.Background = Brushes.White;
                    comboBox.Foreground = (SolidColorBrush)brushConverter.ConvertFrom("#0D3640");
                    comboBox.BorderBrush = (SolidColorBrush)brushConverter.ConvertFrom("#8BA5AE");
                }
                else if (child is TextBox textBox)
                {
                    textBox.Background = Brushes.White;
                    textBox.Foreground = (SolidColorBrush)brushConverter.ConvertFrom("#0D3640");
                    textBox.BorderBrush = (SolidColorBrush)brushConverter.ConvertFrom("#8BA5AE");
                }
                else if (child is CheckBox checkBox)
                {
                    checkBox.Foreground = (SolidColorBrush)brushConverter.ConvertFrom("#0D3640");
                }

                // Continue recursion
                ApplyNormalToPageElements(child);
            }
        }

        #endregion
    }
}