// **************************************************
// File: VideoListUiBuilder.cs
// Description: Builds the sidebar video-list UI elements (headers, buttons,
//              grids).  Extracted from MainWindow to keep display-construction
//              logic self-contained and testable.
// Author: Benjamin Kerr
// 2025 – 2026
// **************************************************

using FishLens_App.Models;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FishLens_App
{
    /// <summary>
    /// Populates the sidebar <see cref="StackPanel"/> (videoList) with folder
    /// headers and per-video button rows.
    /// </summary>
    internal class VideoListUiBuilder
    {
        // UI constants (kept in sync with MainWindow constants)
        private const int BUTTON_HEIGHT = 45;
        private const int BUTTON_FONT_SIZE = 13;
        private const int BUTTON_MARGIN = 5;
        private const int BUTTON_PADDING_HORIZONTAL = 12;
        private const int BUTTON_PADDING_VERTICAL = 8;

        private readonly StackPanel _videoList;
        private readonly VideoButtonStyleHelper _styleHelper;
        private readonly Func<string, string, SolidColorBrush> _resBrush;
        private readonly double _confidenceThreshold;

        /// <param name="videoList">The sidebar StackPanel that holds all rows.</param>
        /// <param name="styleHelper">Pre-constructed style factory.</param>
        /// <param name="resBrush">Resource-brush resolver from the host window.</param>
        /// <param name="confidenceThreshold">
        ///   Videos whose AvgConfidence is below this value get the low-confidence style.
        /// </param>
        public VideoListUiBuilder(
            StackPanel videoList,
            VideoButtonStyleHelper styleHelper,
            Func<string, string, SolidColorBrush> resBrush,
            double confidenceThreshold)
        {
            _videoList = videoList ?? throw new ArgumentNullException(nameof(videoList));
            _styleHelper = styleHelper ?? throw new ArgumentNullException(nameof(styleHelper));
            _resBrush = resBrush ?? throw new ArgumentNullException(nameof(resBrush));
            _confidenceThreshold = confidenceThreshold;
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>
        /// Adds a folder header row and the full list of video button rows to
        /// the sidebar.
        /// </summary>
        public void CreateVideoButtonsList(
            System.Collections.Generic.List<(FileInfo videoFile, Video videoData)> videoDataList,
            string folderName,
            RoutedEventHandler videoButtonClick)
        {
            CreateFolderHeader(folderName);
            CreateVideoButtons(videoDataList, folderName, videoButtonClick);
        }

        /// <summary>
        /// Builds a single video row grid (button + checkbox) for a restored file.
        /// Used by the undo-delete path in MainWindow.
        /// </summary>
        public Grid CreateGridForRestoredFile(
            string filePath,
            Video videoData,
            string folder,
            RoutedEventHandler videoButtonClick)
        {
            var grid = new Grid { Tag = folder, HorizontalAlignment = HorizontalAlignment.Stretch };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FileInfo fi = new FileInfo(filePath);
            Button button = CreateSingleVideoButton(fi, videoData);
            button.Click += videoButtonClick;
            Grid.SetColumn(button, 0);

            var checkBox = new CheckBox
            {
                Padding = new Thickness(5),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = _resBrush("OnAccentForeground", "#F5F8FA")
            };
            Grid.SetColumn(checkBox, 1);

            grid.Children.Add(button);
            grid.Children.Add(checkBox);
            return grid;
        }

        /// <summary>
        /// Ensures a folder header for <paramref name="folder"/> exists in the
        /// sidebar, adding one if not already present.
        /// </summary>
        public void EnsureFolderHeaderExists(string folder)
        {
            foreach (var child in _videoList.Children)
            {
                if (child is Grid g && g.Tag is string t && t == $"header:{folder}")
                    return;
            }

            CreateFolderHeader(folder);
        }

        // ------------------------------------------------------------------
        // Folder header
        // ------------------------------------------------------------------

        /// <summary>
        /// Adds a labelled folder header row (folder name + select-all checkbox)
        /// followed by a separator to the sidebar.
        /// </summary>
        public void CreateFolderHeader(string folderName)
        {
            var folderNameGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            folderNameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            folderNameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textBox = new TextBox
            {
                Text = folderName,
                Foreground = _resBrush("OnAccentForeground", "#F5F8FA"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };

            var folderCheckBox = new CheckBox
            {
                Padding = new Thickness(5),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = _resBrush("OnAccentForeground", "#F5F8FA")
            };

            folderNameGrid.Tag = $"header:{folderName}";

            folderCheckBox.Checked += (s, e) => ToggleFolderCheckboxes(folderName, true);
            folderCheckBox.Unchecked += (s, e) => ToggleFolderCheckboxes(folderName, false);

            Grid.SetColumn(folderCheckBox, 1);
            folderNameGrid.Children.Add(textBox);
            folderNameGrid.Children.Add(folderCheckBox);
            _videoList.Children.Add(folderNameGrid);

            var separator = new Separator
            {
                Margin = new Thickness(0, 5, 0, 5),
                Background = _resBrush("HoverBackgroundBrush", "#2D7A8F"),
                Opacity = 0.5
            };
            _videoList.Children.Add(separator);
        }

        // ------------------------------------------------------------------
        // Per-video buttons
        // ------------------------------------------------------------------

        private void CreateVideoButtons(
            System.Collections.Generic.List<(FileInfo videoFile, Video videoData)> videoDataList,
            string folderName,
            RoutedEventHandler videoButtonClick)
        {
            foreach (var (videoFile, videoData) in videoDataList)
            {
                var grid = new Grid
                {
                    Tag = folderName,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                Button button = CreateSingleVideoButton(videoFile, videoData);
                button.Click += videoButtonClick;
                Grid.SetColumn(button, 0);

                var checkBox = new CheckBox
                {
                    Padding = new Thickness(5),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = _resBrush("OnAccentForeground", "#F5F8FA")
                };
                Grid.SetColumn(checkBox, 1);

                grid.Children.Add(button);
                grid.Children.Add(checkBox);
                _videoList.Children.Add(grid);
            }
        }

        /// <summary>
        /// Creates the styled <see cref="Button"/> for a single video entry.
        /// </summary>
        public Button CreateSingleVideoButton(FileInfo videoFile, Video videoData)
        {
            bool isLowConfidence = videoData.AvgConfidence < _confidenceThreshold;

            var button = new Button
            {
                Content = videoFile.Name,
                Margin = new Thickness(BUTTON_MARGIN),
                Padding = new Thickness(BUTTON_PADDING_HORIZONTAL, BUTTON_PADDING_VERTICAL,
                                                         BUTTON_PADDING_HORIZONTAL, BUTTON_PADDING_VERTICAL),
                Height = BUTTON_HEIGHT,
                Tag = videoFile.FullName,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontSize = BUTTON_FONT_SIZE,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };

            // Prefer XAML-defined resource styles; fall back to programmatic style.
            button.Style = TryResolveStyle(isLowConfidence)
                        ?? _styleHelper.CreateButtonStyle(isLowConfidence);

            return button;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private void ToggleFolderCheckboxes(string folderName, bool isChecked)
        {
            foreach (var child in _videoList.Children)
            {
                if (child is Grid g && g.Tag is string t && t == folderName)
                {
                    foreach (var elem in g.Children)
                    {
                        if (elem is CheckBox cb)
                            cb.IsChecked = isChecked;
                    }
                }
            }
        }

        /// <summary>
        /// Attempts to load a named XAML style from the application resource dictionary.
        /// Returns <c>null</c> if the resource is absent or is the wrong type.
        /// </summary>
        private static Style TryResolveStyle(bool isLowConfidence)
        {
            try
            {
                string key = isLowConfidence ? "VideoButtonLowConfidenceStyle" : "VideoButtonNormalStyle";
                return Application.Current.TryFindResource(key) as Style;
            }
            catch
            {
                return null;
            }
        }
    }
}