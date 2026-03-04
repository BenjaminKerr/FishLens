using FishLens_App.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FishLens_App
{
    public partial class History
    {
        #region Report Display

        // **************************************************
        // Function: GenerateReport
        // Description: Loads CSV data, applies filters, and triggers report display
        public void GenerateReport()
        {
            try
            {
                string csvPath = _pathResolver.ResolveCsvScriptPath();

                if (!File.Exists(csvPath))
                {
                    ShowNoDataMessage();
                    return;
                }

                string[] allLines = File.ReadAllLines(csvPath);

                if (allLines.Length <= 1)
                {
                    ShowNoDataMessage();
                    return;
                }

                var dataLines = allLines.Skip(1).ToArray();
                var filteredLines = ApplyFilters(dataLines);

                if (filteredLines.Length == 0)
                {
                    ShowNoMatchingDataMessage();
                    return;
                }

                DisplayReport(filteredLines);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating report");
                ShowErrorMessage("Unable to generate report. Please check the logs for details.", "Error Generating Report");
            }
        }

        // **************************************************
        // Function: DisplayReport
        // Description: Renders the visual report from filtered CSV lines
        private void DisplayReport(string[] csvLines)
        {
            try
            {
                ConfigureReportView();
                ClearPreviousReport();

                var stats = CalculateStatistics(csvLines);
                _currentReportText = GenerateReportText(stats);

                BuildVisualReport(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error displaying report");
                ShowErrorMessage("Unable to display report. Please check the logs for details.", "Error Displaying Report");
            }
        }

        #endregion

        #region Visual Report Builder

        // **************************************************
        // Function: BuildVisualReport
        // Description: Constructs all visual components of the report
        private void BuildVisualReport(ReportStatistics stats)
        {
            AddReportHeader(stats.TotalDetections);
            AddDashboardOverview(stats);
            AddSpacer(20);

            AddMainCountSection(stats);
            AddSpacer(20);

            AddSectionTitle("Detection Distribution");
            AddDetectionCharts(stats);
            AddSpacer(20);

            AddGroupBySection(stats);
            AddSpacer(20);

            AddSectionTitle("Movement Patterns & Estimates");
            AddMovementCharts(stats);
            AddUpstreamEstimation(stats);
            AddSpacer(20);

            AddSectionTitle("AI Performance Metrics");
            AddAIPerformanceSection(stats);
            AddSpacer(20);

            AddSectionTitle("Location Analysis");
            AddLocationBreakdown(stats);
            AddSpacer(20);

            AddSectionTitle("Video Analysis");
            AddVideoBreakdown(stats);

            if (stats.DetectionsByHour.Count > 0)
            {
                AddSpacer(20);
                AddSectionTitle("Activity by Time of Day");
                AddTimeDistribution(stats);
            }

            if (stats.DetectionsByDate.Count > 0)
            {
                AddSpacer(20);
                AddSectionTitle("Daily Detection Trends");
                AddDailyTrendGraph(stats);
            }
        }

        // **************************************************
        // Function: AddReportHeader
        // Description: Creates and adds the report header section
        private void AddReportHeader(int totalDetections)
        {
            var headerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            headerPanel.Children.Add(CreateTitleTextBlock());
            headerPanel.Children.Add(CreateSubtitleTextBlock());
            reportPanel.Children.Add(headerPanel);
        }

        // **************************************************
        // Function: AddDashboardOverview
        // Description: Creates the overview dashboard with key metrics
        private void AddDashboardOverview(ReportStatistics stats)
        {
            var grid = CreateDashboardGrid();

            var totalCard = CreateStatCard("Total Detections", stats.TotalDetections.ToString(), "#0D3640", "");
            Grid.SetColumn(totalCard, 0); Grid.SetRow(totalCard, 0); Grid.SetColumnSpan(totalCard, 2);

            var confCard = CreateStatCard("Avg Confidence", $"{100 * stats.AverageConfidence:F1}%", "#1E88E5", "");
            Grid.SetColumn(confCard, 0); Grid.SetRow(confCard, 1);

            var highConfCard = CreateStatCard("High Confidence", stats.HighConfidenceCount.ToString(), "#43A047", "");
            Grid.SetColumn(highConfCard, 1); Grid.SetRow(highConfCard, 1);

            grid.Children.Add(totalCard);
            grid.Children.Add(confCard);
            grid.Children.Add(highConfCard);
            reportPanel.Children.Add(grid);
        }

        // **************************************************
        // Function: AddMainCountSection
        // Description: Displays total fish detected and fish-per-day metrics
        private void AddMainCountSection(ReportStatistics stats)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 25) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var mainCountCard = CreateStatCard("Total Fish Detected", stats.FishCount.ToString(), "#1E88E5", "");
            Grid.SetColumn(mainCountCard, 0);
            grid.Children.Add(mainCountCard);

            var fishPerDayCard = CreateStatCard("Fish Per Day", $"{stats.FishPerDay:F1}", "#43A047", "");
            Grid.SetColumn(fishPerDayCard, 1);
            grid.Children.Add(fishPerDayCard);

            reportPanel.Children.Add(grid);
        }

        // **************************************************
        // Function: AddGroupBySection
        // Description: Displays data grouped by current selection (species/datetime/location)
        private void AddGroupBySection(ReportStatistics stats)
        {
            AddSectionTitle($"🔍 Grouped by {_currentGroupBy.ToUpper()}");

            switch (_currentGroupBy.ToLower())
            {
                case "species": AddSpeciesGrouping(stats); break;
                case "datetime": AddDateTimeGrouping(stats); break;
                case "location": AddLocationGrouping(stats); break;
            }
        }

        // **************************************************
        // Function: AddSpeciesGrouping
        // Description: Displays detections grouped by species
        private void AddSpeciesGrouping(ReportStatistics stats)
        {
            foreach (var kvp in stats.SpeciesBreakdown.OrderByDescending(x => x.Value))
                AddBarChart(kvp.Key, kvp.Value, stats.TotalDetections, "#7E57C2");
        }

        // **************************************************
        // Function: AddDateTimeGrouping
        // Description: Displays detections grouped by date
        private void AddDateTimeGrouping(ReportStatistics stats)
        {
            int maxCount = stats.DetectionsByDate.Values.Max();
            foreach (var kvp in stats.DetectionsByDate.OrderBy(x => x.Key))
                AddBarChart(kvp.Key.ToString("MMM dd, yyyy"), kvp.Value, maxCount, "#FF6F00", true);
        }

        // **************************************************
        // Function: AddLocationGrouping
        // Description: Displays detections grouped by location
        private void AddLocationGrouping(ReportStatistics stats)
        {
            int maxCount = stats.DetectionsByLocation.Count > 0 ? stats.DetectionsByLocation.Values.Max() : 1;
            foreach (var kvp in stats.DetectionsByLocation.OrderByDescending(x => x.Value))
                AddBarChart(kvp.Key, kvp.Value, maxCount, "#00ACC1", true);
        }

        // **************************************************
        // Function: AddDetectionCharts
        // Description: Adds bar charts showing fish and bird detection counts
        private void AddDetectionCharts(ReportStatistics stats)
        {
            AddBarChart("Fish", stats.FishCount, stats.TotalDetections, "#1E88E5");
            AddBarChart("Bird", stats.BirdCount, stats.TotalDetections, "#43A047");
        }

        // **************************************************
        // Function: AddMovementCharts
        // Description: Adds bar charts showing upstream and downstream movement
        private void AddMovementCharts(ReportStatistics stats)
        {
            AddBarChart("Upstream", stats.UpstreamCount, stats.TotalDetections, "#0D3640");
            AddBarChart("Downstream", stats.DownstreamCount, stats.TotalDetections, "#00ACC1");
        }

        // **************************************************
        // Function: AddUpstreamEstimation
        // Description: Displays estimated net upstream count with explanation
        private void AddUpstreamEstimation(ReportStatistics stats)
        {
            var estimationPanel = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF9E6")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE082")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 15, 0, 0)
            };

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = "Total upstream count for the report period:",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F57C00")),
                Margin = new Thickness(0, 0, 0, 10)
            });

            var valueGrid = new Grid();
            valueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            valueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            valueGrid.Children.Add(new TextBlock
            {
                Text = $"≈ {stats.EstimatedUpstreamCount:F0} fish",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E65100"))
            });

            panel.Children.Add(valueGrid);
            panel.Children.Add(new TextBlock
            {
                Text = $"Detected: {stats.UpstreamCount} upstream - {stats.DownstreamCount} downstream",
                FontSize = 13,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B6914")),
                Margin = new Thickness(0, 8, 0, 0)
            });

            estimationPanel.Child = panel;
            reportPanel.Children.Add(estimationPanel);
        }

        // **************************************************
        // Function: AddAIPerformanceSection
        // Description: Displays AI performance metrics (confidence and correctness)
        private void AddAIPerformanceSection(ReportStatistics stats)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 25) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var confCard = CreateStatCard("Avg Confidence", $"{100 * stats.AverageConfidence:F1}%", "#1E88E5", "");
            Grid.SetColumn(confCard, 0);
            grid.Children.Add(confCard);

            var correctCard = CreateStatCard("Avg Correctness",
                stats.AverageCorrectness > 0 ? $"{stats.AverageCorrectness:F1}%" : "N/A",
                "#43A047", "");
            Grid.SetColumn(correctCard, 1);
            grid.Children.Add(correctCard);

            reportPanel.Children.Add(grid);

            AddBarChart("High Confidence (≥80%)", stats.HighConfidenceCount, stats.TotalDetections, "#43A047");
            AddBarChart("Lower Confidence (<80%)", stats.TotalDetections - stats.HighConfidenceCount, stats.TotalDetections, "#FB8C00");
        }

        // **************************************************
        // Function: AddLocationBreakdown
        // Description: Displays detailed breakdown by location with species sub-breakdown
        private void AddLocationBreakdown(ReportStatistics stats)
        {
            if (stats.DetectionsByLocation.Count == 0) return;

            int maxDetections = stats.DetectionsByLocation.Values.Max();
            foreach (var kvp in stats.DetectionsByLocation.OrderByDescending(x => x.Value))
            {
                AddBarChart(kvp.Key, kvp.Value, maxDetections, "#00ACC1", true);

                if (stats.GroupedByLocation.ContainsKey(kvp.Key))
                    AddLocationSpeciesBreakdown(kvp.Key, stats.GroupedByLocation[kvp.Key]);
            }
        }

        // **************************************************
        // Function: AddLocationSpeciesBreakdown
        // Description: Shows species breakdown for a specific location
        private void AddLocationSpeciesBreakdown(string location, Dictionary<string, int> speciesData)
        {
            var breakdownPanel = new StackPanel
            {
                Margin = new Thickness(30, 5, 0, 15),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F8FA"))
            };

            foreach (var species in speciesData.OrderByDescending(x => x.Value))
            {
                breakdownPanel.Children.Add(new TextBlock
                {
                    Text = $"  └─ {species.Key}: {species.Value}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#657786")),
                    Margin = new Thickness(0, 2, 0, 2)
                });
            }

            reportPanel.Children.Add(breakdownPanel);
        }

        // **************************************************
        // Function: AddVideoBreakdown
        // Description: Adds bar charts for top 10 videos by detection count
        private void AddVideoBreakdown(ReportStatistics stats)
        {
            if (stats.VideoDetections.Count == 0) return;

            int maxDetections = stats.VideoDetections.Values.Max();
            foreach (var kvp in stats.VideoDetections.OrderByDescending(x => x.Value).Take(10))
                AddBarChart(kvp.Key, kvp.Value, maxDetections, "#7E57C2", true);
        }

        // **************************************************
        // Function: AddTimeDistribution
        // Description: Adds a 24-hour activity distribution chart
        private void AddTimeDistribution(ReportStatistics stats)
        {
            var points = new List<(string label, int value)>();
            for (int hour = 0; hour < 24; hour++)
            {
                int count = stats.DetectionsByHour.ContainsKey(hour) ? stats.DetectionsByHour[hour] : 0;
                points.Add(($"{hour:D2}:00", count));
            }

            reportPanel.Children.Add(CreateLineChart(points, "#FF6F00", 140, reportPanel?.ActualWidth ?? 0));
        }

        // **************************************************
        // Function: AddDailyTrendGraph
        // Description: Creates a visual trend graph showing detections over time
        private void AddDailyTrendGraph(ReportStatistics stats)
        {
            if (stats.DetectionsByDate.Count == 0) return;

            var points = stats.DetectionsByDate
                .OrderBy(x => x.Key)
                .Select(kvp => (kvp.Key.ToString("MMM dd"), kvp.Value))
                .ToList();

            reportPanel.Children.Add(CreateLineChart(points, "#7E57C2", 160, reportPanel?.ActualWidth ?? 0));
        }

        // **************************************************
        // Function: AddSectionTitle
        // Description: Adds a formatted section title to the report
        private void AddSectionTitle(string title)
        {
            reportPanel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#14171A")),
                Margin = new Thickness(0, 10, 0, 12)
            });
        }

        // **************************************************
        // Function: AddBarChart
        // Description: Creates and adds a horizontal bar chart with label and value
        private void AddBarChart(string label, int value, int maxValue, string color, bool isCount = false)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            container.Children.Add(CreateBarChartLabelGrid(label, value, maxValue, color, isCount));
            container.Children.Add(CreateBarChartBars(value, maxValue, color));
            reportPanel.Children.Add(container);
        }

        // **************************************************
        // Function: AddSpacer
        // Description: Adds vertical spacing between report sections
        private void AddSpacer(int height)
        {
            reportPanel.Children.Add(new Border { Height = height });
        }

        #endregion

        #region UI Element Factories

        // **************************************************
        // Function: CreateStatCard
        // Description: Creates a styled statistics card with value and label
        private Border CreateStatCard(string label, string value, string color, string icon = "")
        {
            var card = CreateCardBorder();
            var panel = new StackPanel();

            if (!string.IsNullOrEmpty(icon))
                panel.Children.Add(CreateIconTextBlock(icon));

            panel.Children.Add(CreateValueTextBlock(value, color));
            panel.Children.Add(CreateLabelTextBlock(label));

            card.Child = panel;
            return card;
        }

        // **************************************************
        // Function: CreateTitleTextBlock
        // Description: Creates the main title text block for the report header
        private TextBlock CreateTitleTextBlock()
        {
            return new TextBlock
            {
                Text = "Analysis Dashboard",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0D3640")),
                Margin = new Thickness(0, 0, 0, 5)
            };
        }

        // **************************************************
        // Function: CreateSubtitleTextBlock
        // Description: Creates the subtitle text block showing generation time
        private TextBlock CreateSubtitleTextBlock()
        {
            return new TextBlock
            {
                Text = $"Generated on {DateTime.Now:MMMM dd, yyyy} at {DateTime.Now:h:mm tt}",
                FontSize = 12,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#657786"))
            };
        }

        // **************************************************
        // Function: CreateDashboardGrid
        // Description: Creates the 2-column grid layout for the dashboard overview
        private Grid CreateDashboardGrid()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 25) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            return grid;
        }

        // **************************************************
        // Function: CreateCardBorder
        // Description: Creates a styled border container for stat cards
        private Border CreateCardBorder()
        {
            return new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F8FA")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E1E8ED")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 5, 10)
            };
        }

        // **************************************************
        // Function: CreateIconTextBlock
        private TextBlock CreateIconTextBlock(string icon) =>
            new TextBlock { Text = icon, FontSize = 20, Margin = new Thickness(0, 0, 0, 5) };

        // **************************************************
        // Function: CreateValueTextBlock
        private TextBlock CreateValueTextBlock(string value, string color) =>
            new TextBlock
            {
                Text = value,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
            };

        // **************************************************
        // Function: CreateLabelTextBlock
        private TextBlock CreateLabelTextBlock(string label) =>
            new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#657786"))
            };

        // **************************************************
        // Function: CreateBarChartLabelGrid
        // Description: Creates the label/value row above a bar chart
        private Grid CreateBarChartLabelGrid(string label, int value, int maxValue, string color, bool isCount)
        {
            var labelGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            labelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            labelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            double percentage = maxValue > 0 ? (value * 100.0 / maxValue) : 0;
            string valueDisplay = isCount ? value.ToString() : $"{value} ({percentage:F1}%)";

            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#14171A")),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(labelText, 0);

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
            return labelGrid;
        }

        // **************************************************
        // Function: CreateBarChartBars
        // Description: Creates the visual bar fill element for a bar chart
        private Grid CreateBarChartBars(int value, int maxValue, string color)
        {
            double percentage = maxValue > 0 ? (value * 100.0 / maxValue) : 0;

            var grid = new Grid();
            grid.Children.Add(new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E1E8ED")),
                Height = 24,
                CornerRadius = new CornerRadius(4)
            });
            grid.Children.Add(new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                Height = 24,
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(1, percentage * 3.8)
            });

            return grid;
        }

        // **************************************************
        // Function: CreateEmptyStatePanel
        // Description: Creates the panel displayed when no history data exists
        private StackPanel CreateEmptyStatePanel()
        {
            var panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 80, 0, 0)
            };

            panel.Children.Add(new TextBlock
            {
                Text = string.Empty,
                FontSize = 64,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "No Analysis History",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#14171A")),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Run an analysis to see your detection records here",
                FontSize = 14,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#657786")),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            return panel;
        }

        // **************************************************
        // Function: CreateLineChart
        // Description: Creates a responsive line chart from ordered (label, value) points
        private FrameworkElement CreateLineChart(List<(string label, int value)> points, string color, double height = 180, double availableWidth = 0)
        {
            int maxValue = points.Count > 0 ? Math.Max(1, points.Max(p => p.value)) : 1;
            double totalWidth = (availableWidth > 200) ? availableWidth : (reportPanel?.ActualWidth > 200 ? reportPanel.ActualWidth : 720);

            const double leftMargin = 56;
            const double rightMargin = 12;
            const double topMargin = 8;
            const double bottomMargin = 36;

            double chartWidth = totalWidth - leftMargin - rightMargin;
            double chartHeight = height - topMargin - bottomMargin;

            var container = new Grid { Margin = new Thickness(0, 6, 0, 12), Width = double.NaN, HorizontalAlignment = HorizontalAlignment.Stretch };
            var canvas = new Canvas { Width = totalWidth, Height = height, Background = Brushes.Transparent };

            // Y-axis grid lines and labels
            for (int t = 0; t <= 4; t++)
            {
                double frac = (double)t / 4;
                double y = topMargin + frac * chartHeight;

                canvas.Children.Add(new Line
                {
                    X1 = leftMargin,
                    X2 = leftMargin + chartWidth,
                    Y1 = y,
                    Y2 = y,
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6EDF2")),
                    StrokeThickness = 1
                });

                var lbl = new TextBlock
                {
                    Text = ((int)Math.Round((1 - frac) * maxValue)).ToString(),
                    FontSize = 11,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#657786")),
                    Width = leftMargin - 8,
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(lbl, 0);
                Canvas.SetTop(lbl, y - 8);
                canvas.Children.Add(lbl);
            }

            // X-axis baseline
            double baseY = topMargin + chartHeight;
            canvas.Children.Add(new Line
            {
                X1 = leftMargin,
                X2 = leftMargin + chartWidth,
                Y1 = baseY,
                Y2 = baseY,
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCDDE6")),
                StrokeThickness = 1.5
            });

            var poly = new Polyline
            {
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                StrokeThickness = 2,
                Fill = Brushes.Transparent
            };

            int labelEvery = Math.Max(1, points.Count / 8);

            for (int i = 0; i < points.Count; i++)
            {
                double x = leftMargin + (points.Count == 1 ? chartWidth / 2 : (i * (chartWidth / Math.Max(1, points.Count - 1))));
                double y = topMargin + (1.0 - (double)points[i].value / maxValue) * chartHeight;
                poly.Points.Add(new System.Windows.Point(x, y));

                var marker = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                    Stroke = Brushes.White,
                    StrokeThickness = 1
                };
                Canvas.SetLeft(marker, x - 3);
                Canvas.SetTop(marker, y - 3);
                canvas.Children.Add(marker);

                canvas.Children.Add(new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = baseY,
                    Y2 = baseY + 6,
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCDDE6")),
                    StrokeThickness = 1
                });

                if (i % labelEvery == 0 || i == points.Count - 1)
                {
                    var xl = new TextBlock
                    {
                        Text = points[i].label,
                        FontSize = 10,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#657786"))
                    };
                    Canvas.SetLeft(xl, x - 20);
                    Canvas.SetTop(xl, baseY + 8);
                    canvas.Children.Add(xl);
                }
            }

            canvas.Children.Add(poly);
            container.Children.Add(canvas);

            // Redraw on panel resize
            if (reportPanel != null)
            {
                void ResizeHandler(object s, SizeChangedEventArgs e)
                {
                    if (e.NewSize.Width - leftMargin - rightMargin > 100)
                    {
                        reportPanel.SizeChanged -= ResizeHandler;
                        var parent = container.Parent as Panel;
                        if (parent != null)
                        {
                            int idx = parent.Children.IndexOf(container);
                            if (idx >= 0)
                            {
                                parent.Children.RemoveAt(idx);
                                parent.Children.Insert(idx, CreateLineChart(points, color, height, e.NewSize.Width));
                            }
                        }
                    }
                }
                reportPanel.SizeChanged += ResizeHandler;
            }

            return container;
        }

        #endregion

        #region Report Text Generation

        // **************************************************
        // Function: GenerateReportText
        // Description: Creates a formatted text report for export
        private string GenerateReportText(ReportStatistics stats)
        {
            var sb = new StringBuilder();
            AppendReportHeader(sb, stats);
            AppendSummaryStatistics(sb, stats);
            AppendEnhancedMetrics(sb, stats);
            AppendMovementStatistics(sb, stats);
            AppendAIPerformanceMetrics(sb, stats);
            AppendLocationStatistics(sb, stats);
            AppendTopVideos(sb, stats);
            AppendReportFooter(sb);
            return sb.ToString();
        }

        private void AppendReportHeader(StringBuilder sb, ReportStatistics stats)
        {
            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine("              FISHLENS ANALYSIS REPORT");
            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            if (stats.MinDetectionTimestamp.HasValue && stats.MaxDetectionTimestamp.HasValue)
                sb.AppendLine($"Data Range: {stats.MinDetectionTimestamp.Value:yyyy-MM-dd HH:mm:ss} to {stats.MaxDetectionTimestamp.Value:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
        }

        private void AppendSummaryStatistics(StringBuilder sb, ReportStatistics stats)
        {
            sb.AppendLine("SUMMARY STATISTICS");
            sb.AppendLine($"Total Detections: {stats.TotalDetections}");
            sb.AppendLine($"Fish: {stats.FishCount} ({CalculatePercentage(stats.FishCount, stats.TotalDetections):F1}%)");
            sb.AppendLine($"Bird: {stats.BirdCount} ({CalculatePercentage(stats.BirdCount, stats.TotalDetections):F1}%)");
            sb.AppendLine($"Average Confidence: {stats.AverageConfidence:F2}");
            sb.AppendLine($"High Confidence (≥80%): {stats.HighConfidenceCount}");
            sb.AppendLine();
        }

        private void AppendEnhancedMetrics(StringBuilder sb, ReportStatistics stats)
        {
            sb.AppendLine("ENHANCED METRICS");
            sb.AppendLine($"Fish Per Day: {stats.FishPerDay:F2}");
            sb.AppendLine($"Estimated Upstream Total: ~{stats.EstimatedUpstreamCount:F0} fish");
            sb.AppendLine($"Average Length: {(stats.AverageLengthCm > 0 ? $"{stats.AverageLengthCm:F1} cm" : "N/A")}");
            sb.AppendLine();
        }

        private void AppendMovementStatistics(StringBuilder sb, ReportStatistics stats)
        {
            sb.AppendLine("MOVEMENT DIRECTION");
            sb.AppendLine($"Upstream: {stats.UpstreamCount} ({CalculatePercentage(stats.UpstreamCount, stats.TotalDetections):F1}%)");
            sb.AppendLine($"Downstream: {stats.DownstreamCount} ({CalculatePercentage(stats.DownstreamCount, stats.TotalDetections):F1}%)");
            sb.AppendLine();
        }

        private void AppendAIPerformanceMetrics(StringBuilder sb, ReportStatistics stats)
        {
            sb.AppendLine("AI PERFORMANCE");
            sb.AppendLine($"Average Confidence: {stats.AverageConfidence:F2}%");
            sb.AppendLine($"High Confidence Detections: {stats.HighConfidenceCount} ({CalculatePercentage(stats.HighConfidenceCount, stats.TotalDetections):F1}%)");
            if (stats.AverageCorrectness > 0)
                sb.AppendLine($"Average Correctness: {stats.AverageCorrectness:F2}%");
            sb.AppendLine();
        }

        private void AppendLocationStatistics(StringBuilder sb, ReportStatistics stats)
        {
            sb.AppendLine("LOCATION BREAKDOWN");
            foreach (var location in stats.DetectionsByLocation.OrderByDescending(x => x.Value))
                sb.AppendLine($"{location.Key}: {location.Value} detections ({CalculatePercentage(location.Value, stats.TotalDetections):F1}%)");
            sb.AppendLine();
        }

        private void AppendTopVideos(StringBuilder sb, ReportStatistics stats)
        {
            sb.AppendLine("TOP VIDEOS");
            foreach (var video in stats.VideoDetections.OrderByDescending(x => x.Value).Take(5))
                sb.AppendLine($"  {video.Key}: {video.Value} detections");
        }

        private void AppendReportFooter(StringBuilder sb)
        {
            sb.AppendLine("═══════════════════════════════════════════════════════");
        }

        #endregion
    }
}