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
using FishLens_App.Models;

namespace FishLens_App
{
    public partial class History : Page
    {
        #region Fields

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
        private string _currentGroupBy = "species"; // "species", "datetime", "location"


        #endregion

        #region Constructor

        // **************************************************
        // Function: Constructor
        // Description: Initializes the History page with required dependencies
        public History(IProjectPathResolver pathResolver, IFileSystemManager fileSystemManager, ILogger<MainWindow> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _fileSystemManager = fileSystemManager ?? throw new ArgumentNullException(nameof(fileSystemManager));

            InitializeComponent();
        }

        #endregion

        #region Button Event Handlers

        // **************************************************
        // Function: SaveButtonClick
        // Description: Handles save button click event
        public void SaveButtonClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Changes saved successfully!",
                           "Success",
                           MessageBoxButton.OK,
                           MessageBoxImage.Information);
        }

        // **************************************************
        // Function: GenerateReportClick
        // Description: Generates and displays a filtered analysis report from CSV data
        public void GenerateReportClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string csvPath = _pathResolver.ResolveCsvScriptDirectory();

                if (!File.Exists(csvPath))
                {
                    ShowNoDataMessage();
                    return;
                }

                string[] allLines = File.ReadAllLines(csvPath);

                if (allLines.Length <= 1)
                {
                    // No data rows (only header or empty)
                    ShowNoDataMessage();
                    return;
                }

                // Skip the header row (first line) which contains field names
                var dataLines = allLines.Skip(1).ToArray();

                // Apply filters to data rows only
                var filteredLines = ApplyFilters(dataLines);

                if (filteredLines.Length == 0)
                {
                    ShowNoMatchingDataMessage();
                    return;
                }

                // Generate and display report
                DisplayReport(filteredLines);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating report");
                ShowErrorMessage("Unable to generate report. Please check the logs for details.", "Error Generating Report");
            }
        }

        // **************************************************
        // Function: ExportReportClick
        // Description: Exports the current report to a text file
        public void ExportReportClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentReportText))
                {
                    ShowNoReportMessage();
                    return;
                }

                string reportPath = CreateReportFile();
                bool shouldOpenFile = ShowReportExportedMessage(reportPath);

                if (shouldOpenFile)
                {
                    OpenReportFile(reportPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting report");
                ShowErrorMessage("Unable to export report. Please check the logs for details.", "Error Exporting Report");
            }
        }

        // Additional button handlers and filter helpers moved into Button Event Handlers region
        // **************************************************
        // Function: GroupBy_SelectionChanged
        // Description: Handles group by selection changes and regenerates report
        public void GroupBy_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (groupByCombo == null || groupByCombo.SelectedItem == null)
                return;

            var selectedItem = (ComboBoxItem)groupByCombo.SelectedItem;
            string content = selectedItem.Content.ToString().ToLower();

            if (content.Contains("species"))
                _currentGroupBy = "species";
            else if (content.Contains("date"))
                _currentGroupBy = "datetime";
            else if (content.Contains("location"))
                _currentGroupBy = "location";

            // Regenerate report if one exists
            if (!string.IsNullOrEmpty(_currentReportText))
            {
                GenerateReportClick(sender, e);
            }
        }

        // **************************************************
        // Function: ConfidenceSlider_ValueChanged
        // Description: Updates confidence filter and display when slider changes
        public void ConfidenceSlider_ValueChanged(object sender, RoutedPropertyChangedEventHandler<double> e)
        {
            if (confidenceSlider == null || confidenceValueText == null)
                return;

            _filterMinConfidence = confidenceSlider.Value / 100.0; // Convert to 0-1 range
            confidenceValueText.Text = $"{confidenceSlider.Value:F0}%";
        }

        // **************************************************
        // Function: ApplyFiltersClick
        // Description: Applies all selected filters and regenerates the report
        public void ApplyFiltersClick(object sender, RoutedEventArgs e)
        {
            // Update filter values from UI
            UpdateFiltersFromUI();

            // Regenerate report with filters
            GenerateReportClick(sender, e);
        }

        // **************************************************
        // Function: ClearFiltersClick
        // Description: Resets all filters to default values
        public void ClearFiltersClick(object sender, RoutedEventArgs e)
        {
            // Reset filter state
            _filterStartDate = null;
            _filterEndDate = null;
            _filterSpecies = "All";
            _filterDirection = "All";
            _filterMinConfidence = 0.0;

            // Reset UI controls
            if (startDatePicker != null)
                startDatePicker.SelectedDate = null;

            if (endDatePicker != null)
                endDatePicker.SelectedDate = null;

            if (speciesFilter != null)
                speciesFilter.SelectedIndex = 0;

            if (directionFilter != null)
                directionFilter.SelectedIndex = 0;

            if (cameraFilter != null)
                cameraFilter.SelectedIndex = 0;

            if (confidenceSlider != null)
                confidenceSlider.Value = 0;

            // Regenerate report
            GenerateReportClick(sender, e);
        }

        // **************************************************
        // Function: RefreshReportClick
        // Description: Refreshes the current report with latest data
        public void RefreshReportClick(object sender, RoutedEventArgs e)
        {
            GenerateReportClick(sender, e);
        }

        // **************************************************
        // Function: AllDatesClick
        // Description: Clears date filters to show all dates
        public void AllDatesClick(object sender, RoutedEventArgs e)
        {
            _filterStartDate = null;
            _filterEndDate = null;

            if (startDatePicker != null)
                startDatePicker.SelectedDate = null;

            if (endDatePicker != null)
                endDatePicker.SelectedDate = null;

            MessageBox.Show("Date filters cleared. Click 'Apply Filters' to update the report.",
                            "Filters Updated",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
        }

        // **************************************************
        // Function: ReportType_SelectionChanged
        // Description: 
        public void ReportType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        // **************************************************
        // Function: PrintReportClick
        // Description: 
        public void PrintReportClick(object sender, RoutedEventArgs e)
        {

        }

        #endregion

        #region Statistics Calculations

        // **************************************************
        // Function: CalculateStatistics
        // Description: Analyzes CSV data and calculates comprehensive statistics including new features
        private ReportStatistics CalculateStatistics(string[] csvLines)
        {
            var stats = InitializeStatistics();
            stats.TotalDetections = csvLines.Length;
            double totalConfidence = 0;
            double totalCorrectness = 0;
            int correctnessCount = 0;
            var uniqueDates = new HashSet<DateTime>();

            foreach (string line in csvLines)
            {
                string[] columns = line.Split(',');

                if (columns.Length < 8)
                    continue;

                // CSV layout (reference):
                // 0: video_file, 1: track_id, 2: image_path, 3: likely_class, 4: confidence,
                // 5: start_time_sec, 6: end_time_sec, 7: avg_confidence, 8: direction,
                // 9: species, 10: species_confidence
                string videoName = columns.Length > 0 ? columns[0] : string.Empty;

                // Capture both the species column (index 9) and the likely_class (index 3).
                string species = string.Empty;
                if (columns.Length > 9)
                {
                    species = columns[9].Trim();
                }
                else
                {
                    species = string.Empty;
                }

                string likelyClass = columns.Length > 3 ? columns[3].Trim() : string.Empty;

                // Direction is expected at index 8 in the provided CSV layout; fall back
                // to index 7 if needed.
                string direction = string.Empty;
                if (columns.Length > 8)
                {
                    direction = columns[8].Trim();
                }
                else if (columns.Length > 7)
                {
                    direction = columns[7].Trim();
                }

                ProcessSpeciesData(stats, species, likelyClass);
                ProcessDirectionData(stats, direction);
                ProcessVideoData(stats, videoName);
                totalConfidence += ProcessConfidenceData(stats, columns);

                // NEW: Process date/time data
                if (columns.Length > 1 && DateTime.TryParse(columns[1], out DateTime timestamp))
                {
                    DateTime dateOnly = timestamp.Date;
                    uniqueDates.Add(dateOnly);

                    if (stats.DetectionsByDate.ContainsKey(dateOnly))
                        stats.DetectionsByDate[dateOnly]++;
                    else
                        stats.DetectionsByDate[dateOnly] = 1;

                    ProcessGroupedByDateTime(stats, timestamp, species);
                }

                // NEW: Process location data (extract from video name or separate column)
                string location = ExtractLocationFromVideo(videoName);
                ProcessLocationData(stats, location, species);

                // NEW: Process correctness/accuracy data (if available in columns)
                if (columns.Length > 8 && double.TryParse(columns[8], out double correctness))
                {
                    totalCorrectness += correctness;
                    correctnessCount++;
                }
            }

            stats.AverageConfidence = CalculateAverageConfidence(totalConfidence, stats.TotalDetections);
            stats.AverageCorrectness = correctnessCount > 0 ? totalCorrectness / correctnessCount : 0;

            // Calculate fish per day
            stats.FishPerDay = uniqueDates.Count > 0 ? (double)stats.FishCount / uniqueDates.Count : 0;

            // Estimate upstream count (using direction ratio and total)
            stats.EstimatedUpstreamCount = CalculateEstimatedUpstreamCount(stats);

            // Placeholder for average length
            stats.AverageLengthCm = 0; // To be implemented when length data is available

            return stats;
        }

        #endregion

        #region Report Display

        // **************************************************
        // Function: DisplayReport
        // Description: Renders the enhanced visual report with analytics and charts
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

        #region UI Components

        // **************************************************
        // Function: AddReportHeader
        // Description: Creates and adds the report header section
        private void AddReportHeader(int totalDetections)
        {
            var headerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };

            var title = CreateTitleTextBlock();
            var subtitle = CreateSubtitleTextBlock();

            headerPanel.Children.Add(title);
            headerPanel.Children.Add(subtitle);
            reportPanel.Children.Add(headerPanel);
        }

        // **************************************************
        // Function: AddDashboardOverview
        // Description: Creates the overview dashboard with key metrics
        private void AddDashboardOverview(ReportStatistics stats)
        {
            var grid = CreateDashboardGrid();

            var totalCard = CreateStatCard("Total Detections", stats.TotalDetections.ToString(), "#0D3640", "");
            Grid.SetColumn(totalCard, 0);
            Grid.SetRow(totalCard, 0);
            Grid.SetColumnSpan(totalCard, 2);

            var confCard = CreateStatCard("Avg Confidence", $"{100 * stats.AverageConfidence:F1}%", "#1E88E5", "");
            Grid.SetColumn(confCard, 0);
            Grid.SetRow(confCard, 1);

            var highConfCard = CreateStatCard("High Confidence", stats.HighConfidenceCount.ToString(), "#43A047", "");
            Grid.SetColumn(highConfCard, 1);
            Grid.SetRow(highConfCard, 1);

            grid.Children.Add(totalCard);
            grid.Children.Add(confCard);
            grid.Children.Add(highConfCard);
            reportPanel.Children.Add(grid);
        }

        // **************************************************
        // Function: CreateStatCard
        // Description: Creates a styled statistics card with icon, value, and label
        private Border CreateStatCard(string label, string value, string color, string icon = "")
        {
            var card = CreateCardBorder();
            var panel = new StackPanel();

            if (!string.IsNullOrEmpty(icon))
            {
                panel.Children.Add(CreateIconTextBlock(icon));
            }

            panel.Children.Add(CreateValueTextBlock(value, color));
            panel.Children.Add(CreateLabelTextBlock(label));

            card.Child = panel;
            return card;
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
        // Function: AddVideoBreakdown
        // Description: Adds bar charts for top 10 videos by detection count
        private void AddVideoBreakdown(ReportStatistics stats)
        {
            if (stats.VideoDetections.Count == 0)
                return;

            int maxDetections = stats.VideoDetections.Values.Max();

            foreach (var kvp in stats.VideoDetections.OrderByDescending(x => x.Value).Take(10))
            {
                AddBarChart(kvp.Key, kvp.Value, maxDetections, "#7E57C2", true);
            }
        }

        // **************************************************
        // Function: AddTimeDistribution
        // Description: Adds a 24-hour activity distribution chart
        private void AddTimeDistribution(ReportStatistics stats)
        {
            int maxHourly = stats.DetectionsByHour.Values.Max();

            for (int hour = 0; hour < 24; hour++)
            {
                int count = stats.DetectionsByHour.ContainsKey(hour) ? stats.DetectionsByHour[hour] : 0;
                string timeLabel = $"{hour:D2}:00";
                AddBarChart(timeLabel, count, maxHourly, "#FF6F00", true);
            }
        }

        // **************************************************
        // Function: AddSectionTitle
        // Description: Adds a formatted section title to the report
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

        // **************************************************
        // Function: AddBarChart
        // Description: Creates and adds a horizontal bar chart with label and value
        private void AddBarChart(string label, int value, int maxValue, string color, bool isCount = false)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            var labelGrid = CreateBarChartLabelGrid(label, value, maxValue, color, isCount);
            var barContainer = CreateBarChartBars(value, maxValue, color);

            container.Children.Add(labelGrid);
            container.Children.Add(barContainer);
            reportPanel.Children.Add(container);
        }

        // **************************************************
        // Function: AddSpacer
        // Description: Adds vertical spacing between report sections
        private void AddSpacer(int height)
        {
            reportPanel.Children.Add(new Border { Height = height });
        }

        // **************************************************
        // Function: ShowEmptyState
        // Description: Displays an empty state message when no history is available
        public void ShowEmptyState()
        {
            var emptyPanel = CreateEmptyStatePanel();
            // Add empty state to the report panel (report area) since historyList was removed
            reportPanel.Children.Add(emptyPanel);
        }

        #endregion

        #region Helper Methods - Filters

        // **************************************************
        // Function: PassesSpeciesFilter
        // Description: Checks if a species value passes the current species filter
        private bool PassesSpeciesFilter(string species)
        {
            return _filterSpecies == "All" || species.Equals(_filterSpecies, StringComparison.OrdinalIgnoreCase);
        }

        // **************************************************
        // Function: PassesDirectionFilter
        // Description: Checks if a direction value passes the current direction filter
        private bool PassesDirectionFilter(string direction)
        {
            return _filterDirection == "All" || direction.Contains(_filterDirection, StringComparison.OrdinalIgnoreCase);
        }

        // **************************************************
        // Function: PassesConfidenceFilter
        // Description: Checks if a confidence value passes the minimum confidence threshold
        private bool PassesConfidenceFilter(string[] columns)
        {
            if (columns.Length > 3 && double.TryParse(columns[3], out double confidence))
            {
                return confidence >= _filterMinConfidence;
            }
            return true;
        }

        // **************************************************
        // Function: UpdateFiltersFromUI
        // Description: Updates internal filter state from UI control values
        private void UpdateFiltersFromUI()
        {
            // Update date filters
            if (startDatePicker?.SelectedDate != null)
                _filterStartDate = startDatePicker.SelectedDate.Value;
            else
                _filterStartDate = null;

            if (endDatePicker?.SelectedDate != null)
                _filterEndDate = endDatePicker.SelectedDate.Value;
            else
                _filterEndDate = null;

            // Update species filter
            if (speciesFilter?.SelectedItem != null)
            {
                var selectedItem = (ComboBoxItem)speciesFilter.SelectedItem;
                string content = selectedItem.Content.ToString();
                _filterSpecies = content.Contains("All") ? "All" : content;
            }

            // Update direction filter
            if (directionFilter?.SelectedItem != null)
            {
                var selectedItem = (ComboBoxItem)directionFilter.SelectedItem;
                string content = selectedItem.Content.ToString();
                _filterDirection = content.Contains("All") ? "All" : content;
            }
        }

        // **************************************************
        // Function: ApplyFilters (ENHANCED)
        // Description: Filters CSV data based on current filter criteria including date range
        private string[] ApplyFilters(string[] csvLines)
        {
            var filtered = new List<string>();

            foreach (string line in csvLines)
            {
                string[] columns = line.Split(',');

                if (columns.Length < 8)
                    continue;

                if (!PassesSpeciesFilter(columns[2]))
                    continue;

                if (!PassesDirectionFilter(columns[7]))
                    continue;

                if (!PassesConfidenceFilter(columns))
                    continue;

                if (!PassesDateFilter(columns))
                    continue;

                filtered.Add(line);
            }

            return filtered.ToArray();
        }

        // **************************************************
        // Function: PassesDateFilter
        // Description: Checks if a detection date falls within the selected date range
        private bool PassesDateFilter(string[] columns)
        {
            // If no date filters set, pass everything
            if (!_filterStartDate.HasValue && !_filterEndDate.HasValue)
                return true;

            // Try to parse date from column 1 (adjust index based on your CSV structure)
            if (columns.Length > 1 && DateTime.TryParse(columns[1], out DateTime detectionDate))
            {
                if (_filterStartDate.HasValue && detectionDate.Date < _filterStartDate.Value.Date)
                    return false;

                if (_filterEndDate.HasValue && detectionDate.Date > _filterEndDate.Value.Date)
                    return false;
            }

            return true;
        }

        #endregion

        #region Helper Methods - Statistics

        // **************************************************
        // Function: InitializeStatistics
        // Description: Creates and initializes a new ReportStatistics object with enhanced fields
        private ReportStatistics InitializeStatistics()
        {
            return new ReportStatistics
            {
                VideoDetections = new Dictionary<string, int>(),
                SpeciesBreakdown = new Dictionary<string, int>(),
                DetectionsByHour = new Dictionary<int, int>(),
                DetectionsByDate = new Dictionary<DateTime, int>(),
                DetectionsByLocation = new Dictionary<string, int>(),
                GroupedBySpecies = new Dictionary<string, Dictionary<string, int>>(),
                GroupedByDateTime = new Dictionary<DateTime, Dictionary<string, int>>(),
                GroupedByLocation = new Dictionary<string, Dictionary<string, int>>()
            };
        }


        // **************************************************
        // Function: ProcessSpeciesData
        // Description: Updates statistics with species information from a data row
        private void ProcessSpeciesData(ReportStatistics stats, string species, string likelyClass)
        {
            // Use likelyClass for overall fish/bird counts (more reliable classification)
            if (!string.IsNullOrEmpty(likelyClass))
            {
                if (likelyClass.Equals("fish", StringComparison.OrdinalIgnoreCase))
                    stats.FishCount++;
                else if (likelyClass.Equals("bird", StringComparison.OrdinalIgnoreCase))
                    stats.BirdCount++;
            }
            else
            {
                // Fallback to species column if likelyClass isn't provided
                if (species.Equals("fish", StringComparison.OrdinalIgnoreCase))
                    stats.FishCount++;
                else if (species.Equals("bird", StringComparison.OrdinalIgnoreCase))
                    stats.BirdCount++;
            }

            // Track breakdown by species label (species column)
            if (stats.SpeciesBreakdown.ContainsKey(species))
                stats.SpeciesBreakdown[species]++;
            else
                stats.SpeciesBreakdown[species] = 1;
        }

        // **************************************************
        // Function: ProcessDirectionData
        // Description: Updates statistics with direction information from a data row
        private void ProcessDirectionData(ReportStatistics stats, string direction)
        {
            if (direction.Contains("upstream", StringComparison.OrdinalIgnoreCase))
                stats.UpstreamCount++;
            else if (direction.Contains("downstream", StringComparison.OrdinalIgnoreCase))
                stats.DownstreamCount++;
        }

        // **************************************************
        // Function: ProcessVideoData
        // Description: Updates statistics with video detection counts
        private void ProcessVideoData(ReportStatistics stats, string videoName)
        {
            if (stats.VideoDetections.ContainsKey(videoName))
                stats.VideoDetections[videoName]++;
            else
                stats.VideoDetections[videoName] = 1;
        }

        // **************************************************
        // Function: ProcessConfidenceData
        // Description: Updates confidence statistics and returns the confidence value
        private double ProcessConfidenceData(ReportStatistics stats, string[] columns)
        {
            // avg_confidence is expected at index 7 per CSV layout (0-based).
            if (columns.Length > 7)
            {
                var raw = columns[7].Trim().TrimEnd('%');
                if (double.TryParse(raw, out double confidence))
                {
                    // Normalize percent-like values (e.g., 81 or 81.0) to 0-1 range
                    if (confidence > 1)
                        confidence = confidence / 100.0;

                    if (confidence >= 0.8)
                        stats.HighConfidenceCount++;

                    return confidence;
                }
            }

            return 0;
        }

        // **************************************************
        // Function: CalculateAverageConfidence
        // Description: Calculates average confidence from total and count
        private double CalculateAverageConfidence(double totalConfidence, int totalDetections)
        {
            return totalDetections > 0 ? totalConfidence / totalDetections : 0;
        }

        #endregion

        #region Helper Methods - Report Text Generation

        // **************************************************
        // Function: AppendReportHeader
        // Description: Appends the report header section to the StringBuilder
        private void AppendReportHeader(StringBuilder sb)
        {
            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine("              FISHLENS ANALYSIS REPORT");
            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
        }

        // **************************************************
        // Function: AppendSummaryStatistics
        // Description: Appends summary statistics section to the StringBuilder
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

        // **************************************************
        // Function: AppendMovementStatistics
        // Description: Appends movement direction statistics to the StringBuilder
        private void AppendMovementStatistics(StringBuilder sb, ReportStatistics stats)
        {
            sb.AppendLine("MOVEMENT DIRECTION");
            sb.AppendLine($"Upstream: {stats.UpstreamCount} ({CalculatePercentage(stats.UpstreamCount, stats.TotalDetections):F1}%)");
            sb.AppendLine($"Downstream: {stats.DownstreamCount} ({CalculatePercentage(stats.DownstreamCount, stats.TotalDetections):F1}%)");
            sb.AppendLine();
        }

        // **************************************************
        // Function: AppendTopVideos
        // Description: Appends top 5 videos by detection count to the StringBuilder
        private void AppendTopVideos(StringBuilder sb, ReportStatistics stats)
        {
            sb.AppendLine("TOP VIDEOS");
            foreach (var video in stats.VideoDetections.OrderByDescending(x => x.Value).Take(5))
            {
                sb.AppendLine($"  {video.Key}: {video.Value} detections");
            }
        }

        // **************************************************
        // Function: AppendReportFooter
        // Description: Appends the closing line to the report
        private void AppendReportFooter(StringBuilder sb)
        {
            sb.AppendLine("═══════════════════════════════════════════════════════");
        }

        // **************************************************
        // Function: CalculatePercentage
        // Description: Calculates percentage value for reporting
        private double CalculatePercentage(int value, int total)
        {
            return total > 0 ? (value * 100.0 / total) : 0;
        }

        #endregion

        #region Helper Methods - UI Element Creation

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
        // Description: Creates the grid layout for the dashboard overview
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
        // Description: Creates a text block for displaying an emoji icon
        private TextBlock CreateIconTextBlock(string icon)
        {
            return new TextBlock
            {
                Text = icon,
                FontSize = 20,
                Margin = new Thickness(0, 0, 0, 5)
            };
        }

        // **************************************************
        // Function: CreateValueTextBlock
        // Description: Creates a text block for displaying a stat card value
        private TextBlock CreateValueTextBlock(string value, string color)
        {
            return new TextBlock
            {
                Text = value,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
            };
        }

        // **************************************************
        // Function: CreateLabelTextBlock
        // Description: Creates a text block for displaying a stat card label
        private TextBlock CreateLabelTextBlock(string label)
        {
            return new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#657786"))
            };
        }

        // **************************************************
        // Function: CreateBarChartLabelGrid
        // Description: Creates the label grid for a bar chart showing name and value
        private Grid CreateBarChartLabelGrid(string label, int value, int maxValue, string color, bool isCount)
        {
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

            return labelGrid;
        }

        // **************************************************
        // Function: CreateBarChartBars
        // Description: Creates the visual bar elements for a bar chart
        private Grid CreateBarChartBars(int value, int maxValue, string color)
        {
            double percentage = maxValue > 0 ? (value * 100.0 / maxValue) : 0;

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

            return grid;
        }

        // **************************************************
        // Function: CreateEmptyStatePanel
        // Description: Creates the panel displayed when no history data exists
        private StackPanel CreateEmptyStatePanel()
        {
            var emptyPanel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 80, 0, 0)
            };

            emptyPanel.Children.Add(new TextBlock
            {
                Text = string.Empty,
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

            return emptyPanel;
        }

        #endregion

        #region Helper Methods - File Operations

        // **************************************************
        // Function: CreateReportFile
        // Description: Creates a new report file and returns its path
        private string CreateReportFile()
        {
            string csvPath = _pathResolver.ResolveCsvScriptDirectory();
            string reportsDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(csvPath), "Reports");
            Directory.CreateDirectory(reportsDir);

            string reportFileName = $"FishLens_Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string reportPath = System.IO.Path.Combine(reportsDir, reportFileName);

            File.WriteAllText(reportPath, _currentReportText);

            return reportPath;
        }

        // **************************************************
        // Function: OpenReportFile
        // Description: Opens a report file in the default system application
        private void OpenReportFile(string reportPath)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = reportPath,
                UseShellExecute = true
            });
        }

        #endregion

        #region Helper Methods - View Configuration

        // **************************************************
        // Function: ConfigureReportView
        // Description: Configures the UI to display the report view
        private void ConfigureReportView()
        {
            placeholderPanel.Visibility = Visibility.Collapsed;
            videoPlayer.Visibility = Visibility.Collapsed;
            // 'videoControls' control was removed/renamed in XAML; ensure video player is hidden.
            reportScrollViewer.Visibility = Visibility.Visible;
            reportControls.Visibility = Visibility.Visible;
            viewTitle.Text = "Analysis Report";
        }

        // **************************************************
        // Function: ClearPreviousReport
        // Description: Clears all children from the report panel
        private void ClearPreviousReport()
        {
            reportPanel.Children.Clear();
        }

        #endregion

        #region Helper Methods - Message Boxes

        // **************************************************
        // Function: ShowNoDataMessage
        // Description: Displays a message box when no data is available
        private void ShowNoDataMessage()
        {
            MessageBox.Show("No data available to generate a report.",
                            "No Data",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
        }

        // **************************************************
        // Function: ShowNoMatchingDataMessage
        // Description: Displays a message box when filters return no results
        private void ShowNoMatchingDataMessage()
        {
            MessageBox.Show("No data matches the current filters.",
                            "No Matching Data",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
        }

        // **************************************************
        // Function: ShowNoReportMessage
        // Description: Displays a message box when no report exists to export
        private void ShowNoReportMessage()
        {
            MessageBox.Show("No report to export.",
                            "No Report",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
        }

        // **************************************************
        // Function: ShowReportExportedMessage
        // Description: Displays a success message after exporting and asks to open
        private bool ShowReportExportedMessage(string reportPath)
        {
            var result = MessageBox.Show($"Report exported successfully!\n\nLocation: {reportPath}\n\nWould you like to open it now?",
                           "Report Exported",
                           MessageBoxButton.YesNo,
                           MessageBoxImage.Information);

            return result == MessageBoxResult.Yes;
        }

        // **************************************************
        // Function: ShowErrorMessage
        // Description: Displays a generic error message box
        private void ShowErrorMessage(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // **************************************************
        // Function: ProcessGroupedByDateTime
        // Description: Groups detections by date and time for time-based analysis
        private void ProcessGroupedByDateTime(ReportStatistics stats, DateTime timestamp, string species)
        {
            if (stats.GroupedByDateTime.ContainsKey(timestamp.Date))
            {
                if (stats.GroupedByDateTime[timestamp.Date].ContainsKey(species))
                    stats.GroupedByDateTime[timestamp.Date][species]++;
                else
                    stats.GroupedByDateTime[timestamp.Date][species] = 1;
            }
            else
            {
                stats.GroupedByDateTime[timestamp.Date] = new Dictionary<string, int> { { species, 1 } };
            }
        }

        // **************************************************
        // Function: ProcessLocationData
        // Description: Updates statistics with location-based grouping
        private void ProcessLocationData(ReportStatistics stats, string location, string species)
        {
            if (stats.DetectionsByLocation.ContainsKey(location))
                stats.DetectionsByLocation[location]++;
            else
                stats.DetectionsByLocation[location] = 1;

            // Group by location
            if (stats.GroupedByLocation.ContainsKey(location))
            {
                if (stats.GroupedByLocation[location].ContainsKey(species))
                    stats.GroupedByLocation[location][species]++;
                else
                    stats.GroupedByLocation[location][species] = 1;
            }
            else
            {
                stats.GroupedByLocation[location] = new Dictionary<string, int> { { species, 1 } };
            }
        }

        // **************************************************
        // Function: ExtractLocationFromVideo
        // Description: Extracts location name from video filename or path
        private string ExtractLocationFromVideo(string videoName)
        {
            // Try to extract location from video name
            // Examples: "KenoUpstream.mp4" -> "Keno Dam"
            //          "LinkRiver_2024.mp4" -> "Link River Dam"

            string lower = videoName.ToLower();

            if (lower.Contains("keno"))
                return "Keno Dam";
            if (lower.Contains("link") || lower.Contains("river"))
                return "Link River Dam";
            if (lower.Contains("spencer"))
                return "Spencer Creek";

            return "Unknown Location";
        }

        // **************************************************
        // Function: CalculateEstimatedUpstreamCount
        // Description: Estimates total upstream count based on detection patterns
        private double CalculateEstimatedUpstreamCount(ReportStatistics stats)
        {
            // Estimate total upstream count for the report period as upstream minus downstream.
            // Assumes every fish triggers the sensor and counts are for the same period.
            return stats.UpstreamCount - stats.DownstreamCount;
        }

        // **************************************************
        // Function: BuildVisualReport (ENHANCED)
        // Description: Constructs all visual components of the enhanced report
        private void BuildVisualReport(ReportStatistics stats)
        {
            AddReportHeader(stats.TotalDetections);
            AddDashboardOverview(stats);
            AddSpacer(20);

            // Main count and fish per day
            AddMainCountSection(stats);
            AddSpacer(20);

            AddSectionTitle("Detection Distribution");
            AddDetectionCharts(stats);
            AddSpacer(20);

            // Grouping section based on current selection
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
        // Function: AddMainCountSection
        // Description: Displays main count and fish per day metrics
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
                case "species":
                    AddSpeciesGrouping(stats);
                    break;
                case "datetime":
                    AddDateTimeGrouping(stats);
                    break;
                case "location":
                    AddLocationGrouping(stats);
                    break;
            }
        }

        // **************************************************
        // Function: AddSpeciesGrouping
        // Description: Displays detections grouped by species
        private void AddSpeciesGrouping(ReportStatistics stats)
        {
            foreach (var kvp in stats.SpeciesBreakdown.OrderByDescending(x => x.Value))
            {
                AddBarChart(kvp.Key, kvp.Value, stats.TotalDetections, "#7E57C2");
            }
        }

        // **************************************************
        // Function: AddDateTimeGrouping
        // Description: Displays detections grouped by date
        private void AddDateTimeGrouping(ReportStatistics stats)
        {
            foreach (var kvp in stats.DetectionsByDate.OrderBy(x => x.Key))
            {
                string dateLabel = kvp.Key.ToString("MMM dd, yyyy");
                int maxCount = stats.DetectionsByDate.Values.Max();
                AddBarChart(dateLabel, kvp.Value, maxCount, "#FF6F00", true);
            }
        }

        // **************************************************
        // Function: AddLocationGrouping
        // Description: Displays detections grouped by location
        private void AddLocationGrouping(ReportStatistics stats)
        {
            int maxCount = stats.DetectionsByLocation.Count > 0 ? stats.DetectionsByLocation.Values.Max() : 1;

            foreach (var kvp in stats.DetectionsByLocation.OrderByDescending(x => x.Value))
            {
                AddBarChart(kvp.Key, kvp.Value, maxCount, "#00ACC1", true);
            }
        }

        // **************************************************
        // Function: AddUpstreamEstimation
        // Description: Displays estimated upstream count with explanation
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

            var title = new TextBlock
            {
                Text = "Total upstream count for the report period:",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F57C00")),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var valueGrid = new Grid();
            valueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            valueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var valueText = new TextBlock
            {
                Text = $"≈ {stats.EstimatedUpstreamCount:F0} fish",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E65100"))
            };
            Grid.SetColumn(valueText, 0);

            valueGrid.Children.Add(valueText);

            var detectedText = new TextBlock
            {
                Text = $"Detected: {stats.UpstreamCount} upstream - {stats.DownstreamCount} downstream",
                FontSize = 13,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B6914")),
                Margin = new Thickness(0, 8, 0, 0)
            };

            panel.Children.Add(title);
            panel.Children.Add(valueGrid);
            panel.Children.Add(detectedText);
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

            // Add confidence distribution
            AddConfidenceDistribution(stats);
        }

        // **************************************************
        // Function: AddConfidenceDistribution
        // Description: Shows distribution of detections by confidence level
        private void AddConfidenceDistribution(ReportStatistics stats)
        {
            var lowConf = stats.TotalDetections - stats.HighConfidenceCount;

            AddBarChart("High Confidence (≥80%)", stats.HighConfidenceCount, stats.TotalDetections, "#43A047");
            AddBarChart("Lower Confidence (<80%)", lowConf, stats.TotalDetections, "#FB8C00");
        }

        // **************************************************
        // Function: AddLocationBreakdown
        // Description: Displays detailed breakdown by location
        private void AddLocationBreakdown(ReportStatistics stats)
        {
            if (stats.DetectionsByLocation.Count == 0) return;

            int maxDetections = stats.DetectionsByLocation.Values.Max();
            foreach (var kvp in stats.DetectionsByLocation.OrderByDescending(x => x.Value))
            {
                AddBarChart(kvp.Key, kvp.Value, maxDetections, "#00ACC1", true);

                // Show species breakdown for this location
                if (stats.GroupedByLocation.ContainsKey(kvp.Key))
                {
                    AddLocationSpeciesBreakdown(kvp.Key, stats.GroupedByLocation[kvp.Key]);
                }
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
                var speciesText = new TextBlock
                {
                    Text = $"  └─ {species.Key}: {species.Value}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#657786")),
                    Margin = new Thickness(0, 2, 0, 2)
                };
                breakdownPanel.Children.Add(speciesText);
            }

            reportPanel.Children.Add(breakdownPanel);
        }

        // **************************************************
        // Function: AddDailyTrendGraph
        // Description: Creates a visual trend graph showing detections over time
        private void AddDailyTrendGraph(ReportStatistics stats)
        {
            if (stats.DetectionsByDate.Count == 0) return;

            var sortedDates = stats.DetectionsByDate.OrderBy(x => x.Key).ToList();
            int maxCount = sortedDates.Max(x => x.Value);

            foreach (var kvp in sortedDates)
            {
                string dateLabel = kvp.Key.ToString("MMM dd");
                AddBarChart(dateLabel, kvp.Value, maxCount, "#7E57C2", true);
            }

            // Add average line indicator
            double avgPerDay = stats.FishPerDay;
            var avgIndicator = new TextBlock
            {
                Text = $"Daily Average: {avgPerDay:F1} fish/day",
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#657786")),
                Margin = new Thickness(0, 10, 0, 0)
            };
            reportPanel.Children.Add(avgIndicator);
        }

        // **************************************************
        // Function: GenerateReportText (ENHANCED)
        // Description: Creates a formatted text report for export with all new metrics
        private string GenerateReportText(ReportStatistics stats)
        {
            var sb = new StringBuilder();

            AppendReportHeader(sb);
            AppendSummaryStatistics(sb, stats);
            AppendEnhancedMetrics(sb, stats);
            AppendMovementStatistics(sb, stats);
            AppendAIPerformanceMetrics(sb, stats);
            AppendLocationStatistics(sb, stats);
            AppendTopVideos(sb, stats);
            AppendReportFooter(sb);

            return sb.ToString();
        }

        // **************************************************
        // Function: AppendEnhancedMetrics
        // Description: Appends new metrics to exported report
        private void AppendEnhancedMetrics(StringBuilder sb, ReportStatistics stats)
        {
            sb.AppendLine("ENHANCED METRICS");
            sb.AppendLine($"Fish Per Day: {stats.FishPerDay:F2}");
            sb.AppendLine($"Estimated Upstream Total: ~{stats.EstimatedUpstreamCount:F0} fish");
            sb.AppendLine($"Average Length: {(stats.AverageLengthCm > 0 ? $"{stats.AverageLengthCm:F1} cm" : "N/A")}");
            sb.AppendLine();
        }

        // **************************************************
        // Function: AppendAIPerformanceMetrics
        // Description: Appends AI performance metrics to exported report
        private void AppendAIPerformanceMetrics(StringBuilder sb, ReportStatistics stats)
        {
            sb.AppendLine("AI PERFORMANCE");
            sb.AppendLine($"Average Confidence: {stats.AverageConfidence:F2}%");
            sb.AppendLine($"High Confidence Detections: {stats.HighConfidenceCount} ({CalculatePercentage(stats.HighConfidenceCount, stats.TotalDetections):F1}%)");
            if (stats.AverageCorrectness > 0)
                sb.AppendLine($"Average Correctness: {stats.AverageCorrectness:F2}%");
            sb.AppendLine();
        }

        // **************************************************
        // Function: AppendLocationStatistics
        // Description: Appends location breakdown to exported report
        private void AppendLocationStatistics(StringBuilder sb, ReportStatistics stats)
        {
            sb.AppendLine("LOCATION BREAKDOWN");
            foreach (var location in stats.DetectionsByLocation.OrderByDescending(x => x.Value))
            {
                sb.AppendLine($"{location.Key}: {location.Value} detections ({CalculatePercentage(location.Value, stats.TotalDetections):F1}%)");
            }
            sb.AppendLine();
        }

        #endregion
    }
}