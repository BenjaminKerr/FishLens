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
    public partial class History : Page
    {
        private readonly IProjectPathResolver _pathResolver;
        private readonly IFileSystemManager _fileSystemManager;
        private readonly ILogger<MainWindow> _logger;
        private string _currentReportText;

        // Filter state
        private DateTime? _filterStartDate;
        private DateTime? _filterEndDate;
        private string _filterSpecies = "All";
        private string _filterDirection = "All";
        private double _filterMinConfidence = 0.0;

        public History(IProjectPathResolver pathResolver, IFileSystemManager fileSystemManager, ILogger<MainWindow> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _fileSystemManager = fileSystemManager ?? throw new ArgumentNullException(nameof(fileSystemManager));

            InitializeComponent();
        }

        public void SaveButtonClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Changes saved successfully!",
                           "Success",
                           MessageBoxButton.OK,
                           MessageBoxImage.Information);
        }

        // **************************************************
        // Function: Generate Report Click Handler with Filtering
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

                // Apply filters
                var filteredLines = ApplyFilters(allLines);

                if (filteredLines.Length == 0)
                {
                    MessageBox.Show("No data matches the current filters.",
                                    "No Matching Data",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information);
                    return;
                }

                // Generate and display report
                DisplayReport(filteredLines);
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
        // Function: Apply Filters to CSV Data
        private string[] ApplyFilters(string[] csvLines)
        {
            var filtered = new List<string>();

            foreach (string line in csvLines)
            {
                string[] columns = line.Split(',');
                if (columns.Length < 8) continue;

                // Filter by species
                if (_filterSpecies != "All" && !columns[2].Equals(_filterSpecies, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Filter by direction
                if (_filterDirection != "All" && !columns[7].Contains(_filterDirection, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Filter by confidence (assuming column 3 contains confidence)
                if (columns.Length > 3 && double.TryParse(columns[3], out double confidence))
                {
                    if (confidence < _filterMinConfidence)
                        continue;
                }

                // Add more date filtering logic here if you have timestamp data

                filtered.Add(line);
            }

            return filtered.ToArray();
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
        // Function: Display Enhanced Report with Analytics
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

                // Parse and calculate comprehensive statistics
                var stats = CalculateStatistics(csvLines);

                // Store report text for export
                _currentReportText = GenerateReportText(stats);

                // Build visual report
                AddReportHeader(stats.TotalDetections);
                AddDashboardOverview(stats);
                AddSpacer(20);
                AddSectionTitle("📊 Detection Distribution");
                AddDetectionCharts(stats);
                AddSpacer(20);
                AddSectionTitle("🎯 Movement Patterns");
                AddMovementCharts(stats);
                AddSpacer(20);
                AddSectionTitle("📹 Video Analysis");
                AddVideoBreakdown(stats);

                // Add time-based analysis if timestamp data available
                if (stats.DetectionsByHour.Count > 0)
                {
                    AddSpacer(20);
                    AddSectionTitle("⏰ Activity by Time of Day");
                    AddTimeDistribution(stats);
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
        // Function: Calculate Comprehensive Statistics
        private ReportStatistics CalculateStatistics(string[] csvLines)
        {
            var stats = new ReportStatistics
            {
                VideoDetections = new Dictionary<string, int>(),
                SpeciesBreakdown = new Dictionary<string, int>(),
                DetectionsByHour = new Dictionary<int, int>()
            };

            stats.TotalDetections = csvLines.Length;
            double totalConfidence = 0;

            foreach (string line in csvLines)
            {
                string[] columns = line.Split(',');
                if (columns.Length < 8) continue;

                // Count by species
                string species = columns[2];
                if (species.Equals("fish", StringComparison.OrdinalIgnoreCase))
                    stats.FishCount++;
                else if (species.Equals("bird", StringComparison.OrdinalIgnoreCase))
                    stats.BirdCount++;

                // Species breakdown
                if (stats.SpeciesBreakdown.ContainsKey(species))
                    stats.SpeciesBreakdown[species]++;
                else
                    stats.SpeciesBreakdown[species] = 1;

                // Count by direction
                string direction = columns[7];
                if (direction.Contains("upstream", StringComparison.OrdinalIgnoreCase))
                    stats.UpstreamCount++;
                else if (direction.Contains("downstream", StringComparison.OrdinalIgnoreCase))
                    stats.DownstreamCount++;

                // Count by video
                string videoName = columns[0];
                if (stats.VideoDetections.ContainsKey(videoName))
                    stats.VideoDetections[videoName]++;
                else
                    stats.VideoDetections[videoName] = 1;

                // Confidence metrics
                if (columns.Length > 3 && double.TryParse(columns[3], out double confidence))
                {
                    totalConfidence += confidence;
                    if (confidence >= 0.8)
                        stats.HighConfidenceCount++;
                }

                // Time-based analysis (if you have timestamp in column 1 or similar)
                // This is placeholder - adjust based on your actual data format
                // if (DateTime.TryParse(columns[1], out DateTime timestamp))
                // {
                //     int hour = timestamp.Hour;
                //     if (stats.DetectionsByHour.ContainsKey(hour))
                //         stats.DetectionsByHour[hour]++;
                //     else
                //         stats.DetectionsByHour[hour] = 1;
                // }
            }

            stats.AverageConfidence = stats.TotalDetections > 0 ? totalConfidence / stats.TotalDetections : 0;

            return stats;
        }

        // **************************************************
        // Function: Generate Text Report for Export
        private string GenerateReportText(ReportStatistics stats)
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine("              FISHLENS ANALYSIS REPORT");
            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("SUMMARY STATISTICS");
            sb.AppendLine($"Total Detections: {stats.TotalDetections}");
            sb.AppendLine($"Fish: {stats.FishCount} ({(stats.FishCount * 100.0 / stats.TotalDetections):F1}%)");
            sb.AppendLine($"Bird: {stats.BirdCount} ({(stats.BirdCount * 100.0 / stats.TotalDetections):F1}%)");
            sb.AppendLine($"Average Confidence: {stats.AverageConfidence:F2}");
            sb.AppendLine($"High Confidence (≥80%): {stats.HighConfidenceCount}");
            sb.AppendLine();
            sb.AppendLine("MOVEMENT DIRECTION");
            sb.AppendLine($"Upstream: {stats.UpstreamCount} ({(stats.UpstreamCount * 100.0 / stats.TotalDetections):F1}%)");
            sb.AppendLine($"Downstream: {stats.DownstreamCount} ({(stats.DownstreamCount * 100.0 / stats.TotalDetections):F1}%)");
            sb.AppendLine();
            sb.AppendLine("TOP VIDEOS");
            foreach (var video in stats.VideoDetections.OrderByDescending(x => x.Value).Take(5))
            {
                sb.AppendLine($"  {video.Key}: {video.Value} detections");
            }
            sb.AppendLine("═══════════════════════════════════════════════════════");

            return sb.ToString();
        }

        // **************************************************
        // UI Component Functions
        private void AddReportHeader(int totalDetections)
        {
            var headerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };

            var title = new TextBlock
            {
                Text = "📊 Analysis Dashboard",
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

        private void AddDashboardOverview(ReportStatistics stats)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 25) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Total Detections
            var totalCard = CreateStatCard("Total Detections", stats.TotalDetections.ToString(), "#0D3640", "🎯");
            Grid.SetColumn(totalCard, 0);
            Grid.SetRow(totalCard, 0);
            Grid.SetColumnSpan(totalCard, 2);
            grid.Children.Add(totalCard);

            // Average Confidence
            var confCard = CreateStatCard("Avg Confidence", $"{stats.AverageConfidence:F1}%", "#1E88E5", "⭐");
            Grid.SetColumn(confCard, 0);
            Grid.SetRow(confCard, 1);
            grid.Children.Add(confCard);

            // High Confidence Count
            var highConfCard = CreateStatCard("High Confidence", stats.HighConfidenceCount.ToString(), "#43A047", "✓");
            Grid.SetColumn(highConfCard, 1);
            Grid.SetRow(highConfCard, 1);
            grid.Children.Add(highConfCard);

            reportPanel.Children.Add(grid);
        }

        private Border CreateStatCard(string label, string value, string color, string icon = "")
        {
            var card = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F8FA")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E1E8ED")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 5, 10)
            };

            var panel = new StackPanel();

            if (!string.IsNullOrEmpty(icon))
            {
                var iconText = new TextBlock
                {
                    Text = icon,
                    FontSize = 20,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                panel.Children.Add(iconText);
            }

            var valueText = new TextBlock
            {
                Text = value,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
            };

            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#657786"))
            };

            panel.Children.Add(valueText);
            panel.Children.Add(labelText);
            card.Child = panel;

            return card;
        }

        private void AddDetectionCharts(ReportStatistics stats)
        {
            AddBarChart("Fish", stats.FishCount, stats.TotalDetections, "#1E88E5");
            AddBarChart("Bird", stats.BirdCount, stats.TotalDetections, "#43A047");
        }

        private void AddMovementCharts(ReportStatistics stats)
        {
            AddBarChart("Upstream", stats.UpstreamCount, stats.TotalDetections, "#0D3640");
            AddBarChart("Downstream", stats.DownstreamCount, stats.TotalDetections, "#00ACC1");
        }

        private void AddVideoBreakdown(ReportStatistics stats)
        {
            if (stats.VideoDetections.Count == 0) return;

            int maxDetections = stats.VideoDetections.Values.Max();
            foreach (var kvp in stats.VideoDetections.OrderByDescending(x => x.Value).Take(10))
            {
                AddBarChart(kvp.Key, kvp.Value, maxDetections, "#7E57C2", true);
            }
        }

        private void AddTimeDistribution(ReportStatistics stats)
        {
            // Create a 24-hour activity chart
            int maxHourly = stats.DetectionsByHour.Values.Max();
            for (int hour = 0; hour < 24; hour++)
            {
                int count = stats.DetectionsByHour.ContainsKey(hour) ? stats.DetectionsByHour[hour] : 0;
                string timeLabel = $"{hour:D2}:00";
                AddBarChart(timeLabel, count, maxHourly, "#FF6F00", true);
            }
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
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            var labelGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
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
                Width = Math.Max(1, percentage * 3.8)
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
            reportPanel.Children.Add(new Border { Height = height });
        }

        private void ShowEmptyState()
        {
            var emptyPanel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 80, 0, 0)
            };

            emptyPanel.Children.Add(new TextBlock
            {
                Text = "📊",
                FontSize = 64,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            });

            emptyPanel.Children.Add(new TextBlock
            {
                Text = "No Analysis History",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#14171A")),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            });

            emptyPanel.Children.Add(new TextBlock
            {
                Text = "Run an analysis to see your detection records here",
                FontSize = 14,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#657786")),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            historyList.Children.Add(emptyPanel);
        }

        public void ConfidenceSlider_ValueChanged(object sender, RoutedPropertyChangedEventHandler<double> e)
        {

        }

        public void ApplyFiltersClick(object sender, RoutedEventArgs e)
        {

        }

        public void ClearFiltersClick(object sender, RoutedEventArgs e)
        {

        }

        public void RefreshReportClick(object sender, RoutedEventArgs e)
        {

        }

        public void ReportType_SelectionChanged(object sender, RoutedEventArgs e)
        {

        }

        public void PrintReportClick(object sender, RoutedEventArgs e)
        {

        }

        public void AllDatesClick(object sender, RoutedEventArgs e)
        {

        }
    }
}