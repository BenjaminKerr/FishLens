using FishLens_App.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace FishLens_App
{
    /// <summary>
    /// Interaction logic for History.xaml
    /// </summary>
    public partial class History : Page
    {
        private readonly IProjectPathResolver _pathResolver;
        private readonly IFileSystemManager _fileSystemManager;
        private readonly ILogger<MainWindow> _logger;
        private string _currentReportText;

        public History(IProjectPathResolver pathResolver, IFileSystemManager fileSystemManager, ILogger<MainWindow> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _fileSystemManager = fileSystemManager ?? throw new ArgumentNullException(nameof(fileSystemManager));

            InitializeComponent();
            CreateHistoryList();
        }

        public void SaveButtonClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Changes saved successfully!",
                           "Success",
                           MessageBoxButton.OK,
                           MessageBoxImage.Information);
        }

        // **************************************************
        // Function: Generate Report Click Handler
        public void GenerateReportClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string csvPath = _pathResolver.ResolveCsvScriptDirectory();

                if (!File.Exists(csvPath))
                {
                    MessageBox.Show("No data available to generate a report.",
                                    "No Data",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information);
                    return;
                }

                string[] allLines = File.ReadAllLines(csvPath);

                if (allLines.Length == 0)
                {
                    MessageBox.Show("No data available to generate a report.",
                                    "No Data",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information);
                    return;
                }

                // Generate and display report in the preview pane
                DisplayReport(allLines);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating report");
                MessageBox.Show("Unable to generate report. Please check the logs for details.",
                                "Error Generating Report",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
        }

        // **************************************************
        // Function: Export Report Click Handler
        public void ExportReportClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentReportText))
                {
                    MessageBox.Show("No report to export.",
                                    "No Report",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information);
                    return;
                }

                string csvPath = _pathResolver.ResolveCsvScriptDirectory();
                string reportsDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(csvPath), "Reports");
                Directory.CreateDirectory(reportsDir);

                string reportFileName = $"FishLens_Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                string reportPath = System.IO.Path.Combine(reportsDir, reportFileName);

                File.WriteAllText(reportPath, _currentReportText);

                var result = MessageBox.Show($"Report exported successfully!\n\nLocation: {reportPath}\n\nWould you like to open it now?",
                               "Report Exported",
                               MessageBoxButton.YesNo,
                               MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = reportPath,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting report");
                MessageBox.Show("Unable to export report. Please check the logs for details.",
                                "Error Exporting Report",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
        }

        // **************************************************
        // Function: Display Report with Graphical Elements
        private void DisplayReport(string[] csvLines)
        {
            try
            {
                // Hide video player, show report
                placeholderPanel.Visibility = Visibility.Collapsed;
                videoPlayer.Visibility = Visibility.Collapsed;
                videoControls.Visibility = Visibility.Collapsed;
                reportScrollViewer.Visibility = Visibility.Visible;
                reportControls.Visibility = Visibility.Visible;
                viewTitle.Text = "Analysis Report";

                // Clear previous report
                reportPanel.Children.Clear();

                // Calculate statistics
                int totalDetections = csvLines.Length;
                int fishCount = 0;
                int birdCount = 0;
                int upstreamCount = 0;
                int downstreamCount = 0;
                Dictionary<string, int> videoDetections = new Dictionary<string, int>();

                foreach (string line in csvLines)
                {
                    string[] columns = line.Split(',');

                    if (columns.Length >= 8)
                    {
                        // Count by class
                        if (columns[2].ToLower() == "fish")
                            fishCount++;
                        else if (columns[2].ToLower() == "bird")
                            birdCount++;

                        // Count by direction
                        if (columns[7].ToLower().Contains("upstream"))
                            upstreamCount++;
                        else if (columns[7].ToLower().Contains("downstream"))
                            downstreamCount++;

                        // Count by video
                        string videoName = columns[0];
                        if (videoDetections.ContainsKey(videoName))
                            videoDetections[videoName]++;
                        else
                            videoDetections[videoName] = 1;
                    }
                }

                // Store report text for export
                StringBuilder reportText = new StringBuilder();
                reportText.AppendLine("═══════════════════════════════════════════════════════");
                reportText.AppendLine("              FISHLENS ANALYSIS REPORT");
                reportText.AppendLine("═══════════════════════════════════════════════════════");
                reportText.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                reportText.AppendLine($"Total Detections: {totalDetections}");
                reportText.AppendLine($"Fish: {fishCount}, Bird: {birdCount}");
                reportText.AppendLine($"Upstream: {upstreamCount}, Downstream: {downstreamCount}");
                _currentReportText = reportText.ToString();

                // Header Section
                AddReportHeader(totalDetections);

                // Summary Statistics Cards
                AddSummaryCards(totalDetections, fishCount, birdCount, upstreamCount, downstreamCount);

                // Detection by Class Chart
                AddSectionTitle("Detection by Class");
                AddBarChart("Fish", fishCount, totalDetections, "#1E88E5");
                AddBarChart("Bird", birdCount, totalDetections, "#43A047");

                AddSpacer(20);

                // Movement Direction Chart
                AddSectionTitle("Movement Direction");
                AddBarChart("Upstream", upstreamCount, totalDetections, "#0D3640");
                AddBarChart("Downstream", downstreamCount, totalDetections, "#00ACC1");

                AddSpacer(20);

                // Detections per Video
                if (videoDetections.Count > 0)
                {
                    AddSectionTitle("Detections per Video");
                    int maxDetections = videoDetections.Values.Max();
                    foreach (var kvp in videoDetections.OrderByDescending(x => x.Value))
                    {
                        AddBarChart(kvp.Key, kvp.Value, maxDetections, "#7E57C2", true);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error displaying report");
                MessageBox.Show("Unable to display report. Please check the logs for details.",
                                "Error Displaying Report",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
        }

        // **************************************************
        // UI Helper Functions for Report Generation

        private void AddReportHeader(int totalDetections)
        {
            var headerPanel = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 20)
            };

            var title = new TextBlock
            {
                Text = "📊 Analysis Summary",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0D3640")),
                Margin = new Thickness(0, 0, 0, 5)
            };

            var subtitle = new TextBlock
            {
                Text = $"Generated on {DateTime.Now:MMMM dd, yyyy} at {DateTime.Now:h:mm tt}",
                FontSize = 12,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#657786"))
            };

            headerPanel.Children.Add(title);
            headerPanel.Children.Add(subtitle);
            reportPanel.Children.Add(headerPanel);
        }

        private void AddSummaryCards(int total, int fish, int bird, int upstream, int downstream)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 25)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Total Detections Card
            var totalCard = CreateStatCard("Total Detections", total.ToString(), "#0D3640");
            Grid.SetColumn(totalCard, 0);
            Grid.SetRow(totalCard, 0);
            Grid.SetColumnSpan(totalCard, 2);
            grid.Children.Add(totalCard);

            reportPanel.Children.Add(grid);
        }

        private Border CreateStatCard(string label, string value, string color)
        {
            var card = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F8FA")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E1E8ED")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var panel = new StackPanel();

            var valueText = new TextBlock
            {
                Text = value,
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
            };

            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 13,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#657786"))
            };

            panel.Children.Add(valueText);
            panel.Children.Add(labelText);
            card.Child = panel;

            return card;
        }

        private void AddSectionTitle(string title)
        {
            var textBlock = new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#14171A")),
                Margin = new Thickness(0, 10, 0, 12)
            };

            reportPanel.Children.Add(textBlock);
        }

        private void AddBarChart(string label, int value, int maxValue, string color, bool isCount = false)
        {
            var container = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 12)
            };

            // Label and value row
            var labelGrid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 6)
            };

            labelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            labelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#14171A")),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(labelText, 0);

            double percentage = maxValue > 0 ? (value * 100.0 / maxValue) : 0;
            string valueDisplay = isCount ? value.ToString() : $"{value} ({percentage:F1}%)";

            var valueText = new TextBlock
            {
                Text = valueDisplay,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
            };
            Grid.SetColumn(valueText, 1);

            labelGrid.Children.Add(labelText);
            labelGrid.Children.Add(valueText);

            // Progress bar
            var barBackground = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E1E8ED")),
                Height = 24,
                CornerRadius = new CornerRadius(4)
            };

            var barFill = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                Height = 24,
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(1, percentage * 3.8) // Scale to fit container (380px max for 100%)
            };

            var grid = new Grid();
            grid.Children.Add(barBackground);
            grid.Children.Add(barFill);

            container.Children.Add(labelGrid);
            container.Children.Add(grid);

            reportPanel.Children.Add(container);
        }

        private void AddSpacer(int height)
        {
            var spacer = new Border
            {
                Height = height
            };
            reportPanel.Children.Add(spacer);
        }

        // **************************************************
        // Function: Creates a List of History Elements
        private void CreateHistoryList()
        {
            try
            {
                string csvPath = _pathResolver.ResolveCsvScriptDirectory();

                if (!File.Exists(csvPath))
                {
                    ShowEmptyState();
                    return;
                }

                string[] allLines = File.ReadAllLines(csvPath);

                if (allLines.Length == 0)
                {
                    ShowEmptyState();
                    return;
                }

                foreach (string line in allLines)
                {
                    string[] columns = line.Split(',');

                    if (columns != null && columns.Length >= 8 && historyList != null)
                    {
                        Border element = CreateHistoryElement(columns);
                        historyList.Children.Add(element);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating history list");
                MessageBox.Show("Unable to load history data. Please check the logs for details.",
                                "Error Loading History",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
            }
        }

        // **************************************************
        // Function: Shows empty state when no data is available
        private void ShowEmptyState()
        {
            var emptyPanel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 80, 0, 0)
            };

            var icon = new TextBlock
            {
                Text = "📊",
                FontSize = 64,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            };

            var title = new TextBlock
            {
                Text = "No Analysis History",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#14171A")),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var subtitle = new TextBlock
            {
                Text = "Run an analysis to see your detection records here",
                FontSize = 14,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#657786")),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            emptyPanel.Children.Add(icon);
            emptyPanel.Children.Add(title);
            emptyPanel.Children.Add(subtitle);

            historyList.Children.Add(emptyPanel);
        }

        // **************************************************
        // Function: Creates a Single History Element
        private Border CreateHistoryElement(string[] columns)
        {
            Grid grid = new Grid
            {
                Background = new SolidColorBrush(Colors.White),
                Margin = new Thickness(0, 0, 0, 8)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });

            Border nameBorder = GetName(columns[0]);
            Grid.SetColumn(nameBorder, 0);
            grid.Children.Add(nameBorder);

            ComboBox classBox = GetClass(columns.Length > 2 ? columns[2] : "");
            Grid.SetColumn(classBox, 1);
            grid.Children.Add(classBox);

            ComboBox directionBox = GetDirection(columns.Length > 7 ? columns[7] : "");
            Grid.SetColumn(directionBox, 2);
            grid.Children.Add(directionBox);

            Button playBtn = GetPlayButton(columns[0]);
            Grid.SetColumn(playBtn, 3);
            grid.Children.Add(playBtn);

            Border containerBorder = new Border
            {
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E1E8ED")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = grid,
                Padding = new Thickness(15, 12, 15, 12),
                Margin = new Thickness(0, 0, 0, 10)
            };

            containerBorder.MouseEnter += (s, e) =>
            {
                containerBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F8FA"));
                containerBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0D3640"));
            };

            containerBorder.MouseLeave += (s, e) =>
            {
                containerBorder.Background = new SolidColorBrush(Colors.White);
                containerBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E1E8ED"));
            };

            return containerBorder;
        }

        private Border GetName(string name)
        {
            TextBlock textBlock = new TextBlock
            {
                Text = name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#14171A")),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            return new Border
            {
                Child = textBlock,
                Padding = new Thickness(0, 0, 10, 0)
            };
        }

        private ComboBox GetClass(string className)
        {
            var comboBox = new ComboBox
            {
                SelectedIndex = className.ToLower() == "fish" ? 0 : 1,
                Margin = new Thickness(5, 0, 5, 0),
                Padding = new Thickness(12, 8, 12, 8),
                FontSize = 13,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F8FA")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C7D8DD")),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 110
            };

            comboBox.Items.Add(new ComboBoxItem { Content = "Fish", FontSize = 13 });
            comboBox.Items.Add(new ComboBoxItem { Content = "Bird", FontSize = 13 });

            return comboBox;
        }

        private ComboBox GetDirection(string direction)
        {
            var comboBox = new ComboBox
            {
                SelectedIndex = direction.ToLower().Contains("upstream") ? 0 : 1,
                Margin = new Thickness(5, 0, 5, 0),
                Padding = new Thickness(12, 8, 12, 8),
                FontSize = 13,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F8FA")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C7D8DD")),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 120
            };

            comboBox.Items.Add(new ComboBoxItem { Content = "Upstream", FontSize = 13 });
            comboBox.Items.Add(new ComboBoxItem { Content = "Downstream", FontSize = 13 });

            return comboBox;
        }

        private Button GetPlayButton(string videoFileName)
        {
            var button = new Button
            {
                Content = "▶",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0D3640")),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Padding = new Thickness(12),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0),
                ToolTip = "Play video"
            };

            string videoPath = _pathResolver.ResolvePath(videoFileName);
            button.Tag = videoPath;

            button.Click += VideoButtonClick;

            var style = new Style(typeof(Button));
            var template = new ControlTemplate(typeof(Button));

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

            border.AppendChild(content);
            template.VisualTree = border;

            style.Setters.Add(new Setter(Button.TemplateProperty, template));

            var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Button.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#165666"))));
            style.Triggers.Add(hoverTrigger);

            button.Style = style;

            return button;
        }

        // **************************************************
        // Function: Video Button Click Handler
        private void VideoButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Button clickedButton = (Button)sender;
                string videoPath = clickedButton.Tag.ToString();

                if (!File.Exists(videoPath))
                {
                    MessageBox.Show($"Video file not found:\n{videoPath}",
                                    "File Not Found",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                    return;
                }

                // Hide report, show video
                placeholderPanel.Visibility = Visibility.Collapsed;
                reportScrollViewer.Visibility = Visibility.Collapsed;
                reportControls.Visibility = Visibility.Collapsed;
                videoPlayer.Visibility = Visibility.Visible;
                videoControls.Visibility = Visibility.Visible;

                videoPlayer.Source = new Uri(videoPath);
                videoPlayer.Play();

                string videoFileName = System.IO.Path.GetFileName(videoPath);
                viewTitle.Text = videoFileName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error playing video");
                MessageBox.Show("Unable to play video. Please check the logs for details.",
                                "Error Playing Video",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
        }

        // **************************************************
        // Video Control Handlers
        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (videoPlayer.Source != null)
            {
                videoPlayer.Play();
            }
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (videoPlayer.Source != null)
            {
                videoPlayer.Pause();
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (videoPlayer.Source != null)
            {
                videoPlayer.Stop();
                videoPlayer.Position = TimeSpan.Zero;
            }
        }
    }
}