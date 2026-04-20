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
        // **************************************************
        // Helper: Resolves a brush from the app ResourceDictionary by key,
        // falling back to a hardcoded fallback color if the key is missing.
        private static SolidColorBrush Res(string key, string fallbackHex)
        {
            if (Application.Current?.Resources[key] is SolidColorBrush brush)
                return brush;
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallbackHex));
        }

        // Semantic aliases — maps intent to resource key + fallback
        private SolidColorBrush BrushWindowBg() => Res("WindowBackground", "#181A1B");
        private SolidColorBrush BrushCardBg() => Res("CardBackground", "#23272A");
        private SolidColorBrush BrushHeader() => Res("HeaderBackground", "#23272A");
        private SolidColorBrush BrushAccent() => Res("AccentBrush", "#1B4F5C");
        private SolidColorBrush BrushPrimaryText() => Res("PrimaryText", "#F5F8FA");
        private SolidColorBrush BrushSecondaryText() => Res("SecondaryText", "#B0B3B8");
        private SolidColorBrush BrushBorder() => Res("BorderBrush", "#444950");
        private SolidColorBrush BrushHover() => Res("HoverBackgroundBrush", "#2D7A8F");
        private SolidColorBrush BrushOnAccent() => Res("OnAccentForeground", "#F5F8FA");

        #region Report Entry Point

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

                BuildVisualReport(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error displaying report");
                ShowErrorMessage("Unable to display report. Please check the logs for details.", "Error Displaying Report");
            }
        }

        #endregion

        #region Report Layout

        // **************************************************
        // Function: BuildVisualReport
        // Description: Constructs all visual components of the report
        private void BuildVisualReport(ReportStatistics stats)
        {
            AddReportHeader();

            AddSectionTitle("Detections");
            AddStatCard("Total Detections", stats.TotalDetections.ToString());
            if (stats.ClassBreakdown.Count > 0)
                AddPillRow(stats.ClassBreakdown.Select(kvp => (kvp.Key, kvp.Value)));

            AddSpacer(20);

            // Movement patterns
            AddSectionTitle("Movement Patterns");
            AddBarChart("Upstream", stats.UpstreamCount, stats.TotalDetections, BrushAccent());
            AddBarChart("Downstream", stats.DownstreamCount, stats.TotalDetections, BrushHover());
            int unknownDirection = stats.TotalDetections - stats.UpstreamCount - stats.DownstreamCount;
            if (unknownDirection > 0)
                AddBarChart("Unknown", unknownDirection, stats.TotalDetections, BrushSecondaryText());

            AddSpacer(20);

            // Species
            AddSectionTitle("Species");
            if (stats.SpeciesBreakdown.Count > 0)
            {
                int speciesMax = stats.SpeciesBreakdown.Values.Max();
                foreach (var kvp in stats.SpeciesBreakdown.OrderByDescending(x => x.Value))
                    AddBarChart(kvp.Key, kvp.Value, speciesMax, BrushHover(), isCount: true);
            }
            else
            {
                AddEmptyNote("No species data available.");
            }

            AddSpacer(20);

            // Location analysis
            AddSectionTitle("Location");
            if (stats.DetectionsByLocation.Count > 0)
            {
                int locationMax = stats.DetectionsByLocation.Values.Max();
                foreach (var kvp in stats.DetectionsByLocation.OrderByDescending(x => x.Value))
                    AddBarChart(kvp.Key, kvp.Value, locationMax, BrushAccent(), isCount: true);
            }
            else
            {
                AddEmptyNote("No location data available.");
            }

            // Activity by day line chart
            AddSectionTitle("Observations by Day");
            if (stats.DetectionsByDate.Count > 0)
            {
                var points = stats.DetectionsByDate
                    .OrderBy(x => x.Key)
                    .Select(kvp => (kvp.Key.ToString("MMM d, ''yy"), kvp.Value))
                    .ToList();
                reportPanel.Children.Add(CreateLineChart(points, BrushHover(), 180));
            }
            else
            {
                AddEmptyNote("No date data available.");
            }

            AddSpacer(24);

            // Daily trend + rolling 7-day average
            AddSectionTitle("Observations by Time of Day");
            if (stats.DetectionsByHour.Count > 0)
            {
                var points = Enumerable.Range(0, 24)
                    .Select(h => (FormatHourLabel(h),
                                  stats.DetectionsByHour.ContainsKey(h) ? stats.DetectionsByHour[h] : 0))
                    .ToList();
                reportPanel.Children.Add(CreateLineChart(points, BrushAccent(), 180));
            }
            else
            {
                AddEmptyNote("No time-of-day data available.");
            }
        }

        // **************************************************
        // Function: AddPillRow
        // Description: Compact inline display of label/count pairs — no bars
        private void AddPillRow(IEnumerable<(string label, int count)> items)
        {
            var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };

            foreach (var (label, count) in items)
            {
                var pill = new Border
                {
                    Background = BrushCardBg(),
                    BorderBrush = BrushBorder(),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 0, 8, 8)
                };
                
                pill.Child = new TextBlock
                {
                    FontSize = 13,
                    Foreground = BrushPrimaryText(),
                    Inlines =
                    {
                        new System.Windows.Documents.Run(label)       { FontWeight = FontWeights.SemiBold },
                        new System.Windows.Documents.Run($"  {count}")
                    }
                };

                panel.Children.Add(pill);
            }

            reportPanel.Children.Add(panel);
        }

        private string FormatHourLabel(int hour)
        {
            if (hour == 0) return "12a";
            if (hour == 12) return "12p";
            return hour < 12 ? $"{hour}a" : $"{hour - 12}p";
        }

        private void AddEmptyNote(string message)
        {
            reportPanel.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 13,
                FontStyle = FontStyles.Italic,
                Foreground = BrushSecondaryText(),
                Margin = new Thickness(0, 0, 0, 12)
            });
        }

        // AddStatCard no longer takes a color — uses accent from resources
        private void AddStatCard(string label, string value)
        {
            var card = new Border
            {
                Background = BrushCardBg(),
                BorderBrush = BrushBorder(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 16)
            };

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = BrushPrimaryText()
            });
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = BrushSecondaryText(),
                Margin = new Thickness(0, 4, 0, 0)
            });

            card.Child = panel;
            reportPanel.Children.Add(card);
        }

        private void AddReportHeader()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
            panel.Children.Add(new TextBlock
            {
                Text = "Analysis Dashboard",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = BrushPrimaryText()
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"Generated on {DateTime.Now:MMMM dd, yyyy} at {DateTime.Now:h:mm tt}",
                FontSize = 12,
                Foreground = BrushSecondaryText(),
                Margin = new Thickness(0, 4, 0, 0)
            });
            reportPanel.Children.Add(panel);
        }

        #endregion

        #region Shared UI Helpers

        // **************************************************
        // Function: AddSectionTitle
        private void AddSectionTitle(string title)
        {
            reportPanel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushPrimaryText(),
                Margin = new Thickness(0, 10, 0, 12)
            });
        }

        // **************************************************
        // Function: AddBarChart
        // color parameter is now a SolidColorBrush resolved from resources at call site
        private void AddBarChart(string label, int value, int maxValue, SolidColorBrush color, bool isCount = false)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            container.Children.Add(CreateBarChartLabelGrid(label, value, maxValue, color, isCount));
            container.Children.Add(CreateBarChartBars(value, maxValue, color));
            reportPanel.Children.Add(container);
        }

        // **************************************************
        // Function: AddSpacer
        private void AddSpacer(int height)
        {
            reportPanel.Children.Add(new Border { Height = height });
        }

        // **************************************************
        // Function: CreateStatCard
        private Border CreateStatCard(string label, string value, string icon = "")
        {
            var card = CreateCardBorder();
            var panel = new StackPanel();

            if (!string.IsNullOrEmpty(icon))
                panel.Children.Add(CreateIconTextBlock(icon));

            panel.Children.Add(CreateValueTextBlock(value));
            panel.Children.Add(CreateLabelTextBlock(label));

            card.Child = panel;
            return card;
        }

        private Grid CreateDashboardGrid()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 25) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            return grid;
        }

        private Border CreateCardBorder() => new Border
        {
            Background = BrushCardBg(),
            BorderBrush = BrushBorder(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(15),
            Margin = new Thickness(0, 0, 5, 10)
        };

        private TextBlock CreateIconTextBlock(string icon) => new TextBlock
        {
            Text = icon,
            FontSize = 20,
            Margin = new Thickness(0, 0, 0, 5),
            Foreground = BrushPrimaryText()
        };

        private TextBlock CreateValueTextBlock(string v) => new TextBlock
        {
            Text = v,
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = BrushAccent()
        };

        private TextBlock CreateLabelTextBlock(string label) => new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = BrushSecondaryText()
        };

        private Grid CreateBarChartLabelGrid(string label, int value, int maxValue, SolidColorBrush color, bool isCount)
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
                Foreground = BrushPrimaryText(),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(labelText, 0);

            var valueText = new TextBlock
            {
                Text = valueDisplay,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = color
            };
            Grid.SetColumn(valueText, 1);

            labelGrid.Children.Add(labelText);
            labelGrid.Children.Add(valueText);
            return labelGrid;
        }

        private Grid CreateBarChartBars(int value, int maxValue, SolidColorBrush color)
        {
            double percentage = maxValue > 0 ? (value * 100.0 / maxValue) : 0;

            var grid = new Grid();
            grid.Children.Add(new Border
            {
                Background = BrushBorder(),   // track uses border tone
                Height = 24,
                CornerRadius = new CornerRadius(4)
            });
            grid.Children.Add(new Border
            {
                Background = color,
                Height = 24,
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(1, percentage * 3.8)
            });

            return grid;
        }

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
                Foreground = BrushPrimaryText(),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Run an analysis to see your detection records here",
                FontSize = 14,
                Foreground = BrushSecondaryText(),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            return panel;
        }

        // **************************************************
        // Function: CreateLineChart
        // Description: Creates a responsive line chart from ordered (label, value) points
        // color is now a SolidColorBrush resolved from resources at call site
        private FrameworkElement CreateLineChart(List<(string label, int value)> points, SolidColorBrush color, double height = 180, double availableWidth = 0)
        {
            int maxValue = points.Count > 0 ? Math.Max(1, points.Max(p => p.value)) : 1;
            double totalWidth = (availableWidth > 200) ? availableWidth : (reportPanel?.ActualWidth > 200 ? reportPanel.ActualWidth : 720);

            const double leftMargin = 56;
            const double rightMargin = 12;
            const double topMargin = 8;
            const double bottomMargin = 36;

            double chartWidth = totalWidth - leftMargin - rightMargin;
            double chartHeight = height - topMargin - bottomMargin;

            var container = new Grid
            {
                Margin = new Thickness(0, 6, 0, 12),
                Width = double.NaN,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var canvas = new Canvas { Width = totalWidth, Height = height, Background = Brushes.Transparent };

            var gridLineColor = BrushBorder();
            var axisColor = BrushBorder();
            var tickColor = BrushSecondaryText();

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
                    Stroke = gridLineColor,
                    StrokeThickness = 1
                });

                var lbl = new TextBlock
                {
                    Text = ((int)Math.Round((1 - frac) * maxValue)).ToString(),
                    FontSize = 11,
                    Foreground = tickColor,
                    Width = leftMargin - 8,
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(lbl, 0);
                Canvas.SetTop(lbl, y - 8);
                canvas.Children.Add(lbl);
            }

            double baseY = topMargin + chartHeight;
            canvas.Children.Add(new Line
            {
                X1 = leftMargin,
                X2 = leftMargin + chartWidth,
                Y1 = baseY,
                Y2 = baseY,
                Stroke = axisColor,
                StrokeThickness = 1.5
            });

            var poly = new Polyline
            {
                Stroke = color,
                StrokeThickness = 2,
                Fill = Brushes.Transparent
            };

            int labelEvery = Math.Max(1, points.Count / 8);

            for (int i = 0; i < points.Count; i++)
            {
                double x = leftMargin + (points.Count == 1
                    ? chartWidth / 2
                    : (i * (chartWidth / Math.Max(1, points.Count - 1))));
                double y = topMargin + (1.0 - (double)points[i].value / maxValue) * chartHeight;
                poly.Points.Add(new System.Windows.Point(x, y));

                var marker = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = color,
                    Stroke = new SolidColorBrush(Colors.Transparent), // no white outline in dark mode
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
                    Stroke = axisColor,
                    StrokeThickness = 1
                });

                if (i % labelEvery == 0 || i == points.Count - 1)
                {
                    var xl = new TextBlock
                    {
                        Text = points[i].label,
                        FontSize = 10,
                        Foreground = tickColor
                    };
                    Canvas.SetLeft(xl, x - 20);
                    Canvas.SetTop(xl, baseY + 8);
                    canvas.Children.Add(xl);
                }
            }

            canvas.Children.Add(poly);
            container.Children.Add(canvas);

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
    }
}