// ***************************************************************************************************************************
// File: History.xaml.cs
// Description: This is the code behind for the History page, this will allow users to view their detection history, generate reports, and export data. It includes enhanced filtering options and a more comprehensive report layout with additional statistics and charts.
// Notes: N/A
// ***************************************************************************************************************************

using FishLens_App.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Data.SqlClient;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;
using FishLens_App.Models;

namespace FishLens_App
{
    public partial class History : Page
    {
        #region Fields

        private readonly IProjectPathResolver _pathResolver;
        private readonly IFileSystemManager _fileSystemManager;
        private readonly ILogger<MainWindow> _logger;
        private const int ReportLayoutSettleDelayMs = 300;
        private int _reportRenderVersion;
        private string _currentReportText;

        // Filter state
        private DateTime? _filterStartDate;
        private DateTime? _filterEndDate;
        private string _filterSpecies = "All";
        private string _filterDirection = "All";
        private string _filterCamera = "All";
        private string _filterRun = "All";
        private double _filterMinConfidence = 0.0;
        private string _currentGroupBy = "species"; // "species", "datetime", "location"
        private string _currentReportStyle = "Standard Report";
        private Dictionary<string, string[]> _lastGroupedLinesByRun = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private bool _isUpdatingConfidenceControls;
        private bool _isHistoryLoaded;
        private StackPanel _printReportPanel;
        private StackPanel ActiveReportPanel => _printReportPanel ?? reportPanel;
        private static FrameworkElement _reportResourceScope;

        private static readonly Dictionary<string, string> _reportBrushRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            ["#0F172A"] = "ReportTitleTextBrush",
            ["#0D3640"] = "ReportTableHeaderBrush",
            ["#14171A"] = "ReportBodyTextBrush",
            ["#1F2937"] = "ReportBodyTextBrush",
            ["#657786"] = "ReportSecondaryTextBrush",
            ["#64748B"] = "ReportSecondaryTextBrush",
            ["#AAB8C2"] = "ReportMutedTextBrush",
            ["#94A3B8"] = "ReportMutedTextBrush",
            ["#F5F8FA"] = "ReportSurfaceBrush",
            ["#F8FAFC"] = "ReportSurfaceBrush",
            ["#FFFFFF"] = "ReportOnTableHeaderBrush",
            ["#E1E8ED"] = "ReportBorderBrush",
            ["#D9E2EC"] = "ReportBorderBrush",
            ["#E6EDF2"] = "ReportGridBrush",
            ["#CCDDE6"] = "ReportAxisBrush",
            ["#244F5A"] = "ReportTableHeaderBorderBrush",
            ["#1E88E5"] = "ReportInfoBrush",
            ["#2F80ED"] = "ReportSecondaryInfoBrush",
            ["#00ACC1"] = "ReportSecondaryInfoBrush",
            ["#7E57C2"] = "ReportSpecialBrush",
            ["#FF6F00"] = "ReportTimeBrush",
            ["#F57C00"] = "ReportWarmTitleBrush",
            ["#E65100"] = "ReportWarmTitleBrush",
            ["#8B6914"] = "ReportWarmTextBrush",
            ["#FFF9E6"] = "ReportWarmSurfaceBrush",
            ["#FFF7E8"] = "ReportWarmSurfaceBrush",
            ["#FFE082"] = "ReportWarmBorderBrush",
            ["#FFD37A"] = "ReportWarmBorderBrush",
            ["#43A047"] = "ReportConfidenceHighBrush",
            ["#FB8C00"] = "ReportConfidenceMidBrush",
            ["#2AB5B5"] = "DirectionUpstreamBrush",
            ["#E05C5C"] = "DirectionDownstreamBrush",
            ["#E8A038"] = "DirectionUnknownBrush",
        };

        // Frozen brush cache avoids creating fallback SolidColorBrush objects on every report render.
        private static readonly Dictionary<string, SolidColorBrush> _brushCache = new();
        private static SolidColorBrush Brush(string hex)
        {
            if (_reportBrushRoles.TryGetValue(hex, out string resourceKey)
                && TryFindReportBrush(resourceKey) is SolidColorBrush resourceBrush)
            {
                return resourceBrush;
            }

            if (_brushCache.TryGetValue(hex, out var b)) return b;
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            _brushCache[hex] = brush;
            return brush;
        }

        private static Color ReportColor(string hex)
        {
            if (_reportBrushRoles.TryGetValue(hex, out string resourceKey)
                && TryFindReportBrush(resourceKey) is SolidColorBrush resourceBrush)
            {
                return resourceBrush.Color;
            }

            return (Color)ColorConverter.ConvertFromString(hex);
        }

        private static Color ResourceColor(string resourceKey, Color fallback)
        {
            return TryFindReportBrush(resourceKey) is SolidColorBrush resourceBrush
                ? resourceBrush.Color
                : fallback;
        }

        private static Brush ResourceBrush(string resourceKey, Brush fallback)
        {
            return _reportResourceScope?.TryFindResource(resourceKey) as Brush
                ?? Application.Current?.Resources[resourceKey] as Brush
                ?? fallback;
        }

        private static SolidColorBrush TryFindReportBrush(string resourceKey)
        {
            return _reportResourceScope?.TryFindResource(resourceKey) as SolidColorBrush
                ?? Application.Current?.Resources[resourceKey] as SolidColorBrush;
        }
        private string[] _lastFilteredLines = Array.Empty<string>();
        private ReportStatistics _lastStats;

        private bool IsAllHistoryScope => string.Equals(_filterRun, "All", StringComparison.OrdinalIgnoreCase);


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

            Loaded += (s, e) =>
            {
                LoadRunFilter();
                LoadLocationFilter();
                ResetFiltersToDefaults();
                App.LocationChanged += OnLocationChanged;
                App.RunChanged += OnRunChanged;
                App.AnalysisStateChanged += OnAnalysisStateChanged;
                // Apply immediately in case analysis was already running when user navigated here
                ApplyAnalysisLock(App.IsAnalyzing);
                _isHistoryLoaded = true;

                // Backfill no-fish rows for all runs into DB so "Videos w/ No Fish" reports
                // work correctly even for runs processed before DB sync was introduced.
                var bfApp = Application.Current as App;
                if (bfApp != null && bfApp.CurrentOrganizationId > 0)
                {
                    string bfHistoryDir = _pathResolver.ResolveAllHistoryFolder();
                    int bfOrgId         = bfApp.CurrentOrganizationId;
                    int bfUserId        = bfApp.CurrentUserId;
                    string bfConn       = bfApp.connectionString;
                    _ = System.Threading.Tasks.Task.Run(() =>
                        FishLens_App.Services.DbSyncService.BackfillNoFishRuns(bfHistoryDir, bfOrgId, bfUserId, bfConn));
                }

                GenerateReportClick(this, new RoutedEventArgs());
            };
            Unloaded += (s, e) =>
            {
                _isHistoryLoaded = false;
                App.LocationChanged -= OnLocationChanged;
                App.RunChanged -= OnRunChanged;
                App.AnalysisStateChanged -= OnAnalysisStateChanged;
            };
        }

        // ****************************************************************
        // Function: OnAnalysisStateChanged / ApplyAnalysisLock
        // Description: Disables Generate Report button and shows banner while analysis runs
        private void OnAnalysisStateChanged(bool isAnalyzing) =>
            Dispatcher.Invoke(() => ApplyAnalysisLock(isAnalyzing));

        private void ApplyAnalysisLock(bool isAnalyzing)
        {
            if (generateReportButton != null)
                generateReportButton.IsEnabled = !isAnalyzing;
            if (historyAnalysisBanner != null)
                historyAnalysisBanner.Visibility = isAnalyzing ? Visibility.Visible : Visibility.Collapsed;
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
        // Description: Generates and displays a filtered analysis report from the selected data source
        public void GenerateReportClick(object sender, RoutedEventArgs e)
        {
            try
            {
                RefreshReportFromSelectedScope(deferDisplay: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating report");
                ShowErrorMessage("Unable to generate report. Please check the logs for details.", "Error Generating Report");
            }
        }

        // **************************************************
        // Function: ExportReportClick
        // Description: Exports the current report as a .txt file to a user-chosen location
        public void ExportReportClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!RefreshReportFromSelectedScope() || string.IsNullOrEmpty(_currentReportText))
                {
                    ShowNoReportMessage();
                    return;
                }

                string defaultName = $"FishLens_Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                var dlg = new SaveFileDialog
                {
                    Title       = "Export Report",
                    FileName    = defaultName,
                    DefaultExt  = ".txt",
                    Filter      = "Text file (*.txt)|*.txt|All files (*.*)|*.*",
                    FilterIndex = 1
                };

                if (dlg.ShowDialog() != true) return;

                File.WriteAllText(dlg.FileName, _currentReportText);

                var result = MessageBox.Show(
                    $"Report saved to:\n{dlg.FileName}\n\nWould you like to open it now?",
                    "Report Exported", MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = dlg.FileName,
                        UseShellExecute = true
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting report");
                ShowErrorMessage("Unable to export report. Please check the logs for details.", "Error Exporting Report");
            }
        }

        // **************************************************
        // Function: ConfidenceSlider_ValueChanged
        // Description: Updates confidence filter and display when slider changes
        public void ConfidenceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (confidenceSlider == null || confidenceValueBox == null || _isUpdatingConfidenceControls)
                return;

            _filterMinConfidence = confidenceSlider.Value / 100.0; // Convert to 0-1 range
            _isUpdatingConfidenceControls = true;
            confidenceValueBox.Text = $"{confidenceSlider.Value:F0}";
            _isUpdatingConfidenceControls = false;
        }

        // **************************************************
        // Function: ConfidenceValueBox_LostFocus
        // Description: Applies a typed confidence threshold when the textbox loses focus
        public void ConfidenceValueBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyConfidenceValueFromTextBox();
        }

        // **************************************************
        // Function: ConfidenceValueBox_KeyDown
        // Description: Applies a typed confidence threshold when the user presses Enter
        public void ConfidenceValueBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            ApplyConfidenceValueFromTextBox();
            e.Handled = true;
        }

        // **************************************************
        // Function: ApplyConfidenceValueFromTextBox
        // Description: Parses the typed percentage and syncs it back to the slider
        private void ApplyConfidenceValueFromTextBox()
        {
            if (confidenceSlider == null || confidenceValueBox == null)
                return;

            string raw = (confidenceValueBox.Text ?? string.Empty).Trim().TrimEnd('%');
            if (!double.TryParse(raw, out double value))
                value = confidenceSlider.Value;

            value = Math.Max(0, Math.Min(100, Math.Round(value)));

            _isUpdatingConfidenceControls = true;
            confidenceSlider.Value = value;
            confidenceValueBox.Text = $"{value:F0}";
            _isUpdatingConfidenceControls = false;

            _filterMinConfidence = value / 100.0;
        }

        // **************************************************
        // Function: ApplyFiltersClick
        // Description: Saves current UI filter selections and regenerates the report.
        public void ApplyFiltersClick(object sender, RoutedEventArgs e)
        {
            UpdateFiltersFromUI();
            GenerateReportClick(sender, e);
        }

        // **************************************************
        // Function: ResetFiltersToDefaults
        // Description: Resets all filter state and UI controls to their default (show-all) values.
        //              Called on page load and when the user clears filters.
        private void ResetFiltersToDefaults()
        {
            _filterStartDate    = null;
            _filterEndDate      = null;
            _filterSpecies      = "All";
            _filterDirection    = "All";
            _filterCamera       = "All";
            _filterMinConfidence = 0.0;
            _filterRun          = "All";

            if (startDatePicker  != null) startDatePicker.SelectedDate  = null;
            if (endDatePicker    != null) endDatePicker.SelectedDate    = null;
            if (speciesFilter    != null) speciesFilter.SelectedIndex   = 0;
            if (directionFilter  != null) directionFilter.SelectedIndex = 0;
            if (cameraFilter     != null) cameraFilter.SelectedIndex    = 0;
            if (confidenceSlider != null) confidenceSlider.Value        = 0;
            if (confidenceValueBox != null) confidenceValueBox.Text     = "0";
            if (runFilter        != null) runFilter.SelectedIndex       = 0;
        }

        // **************************************************
        // Function: ClearFiltersClick
        // Description: Resets all filters to default values
        public void ClearFiltersClick(object sender, RoutedEventArgs e)
        {
            ResetFiltersToDefaults();
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
        }

        // **************************************************
        // Function: ReportType_SelectionChanged
        // Description: Stores the selected report style — report only rebuilds when the user
        //              explicitly clicks Apply Filters or Generate Report.
        public void ReportType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
                _currentReportStyle = item.Content?.ToString() ?? "Standard Report";

            if (_isHistoryLoaded)
                GenerateReportClick(sender, new RoutedEventArgs());
        }

        // **************************************************
        // Function: PrintReportClick
        // Description: Prints the current report via the system print dialog (includes Print to PDF)
        public void PrintReportClick(object sender, RoutedEventArgs e)
        {
            if (!RefreshReportFromSelectedScope() || ActiveReportPanel.Children.Count == 0)
            {
                ShowNoReportMessage();
                return;
            }

            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true) return;

            string[] screenReportLines = _lastFilteredLines?.ToArray() ?? Array.Empty<string>();

            try
            {
                // Render the actual visual report (charts, stat cards, etc.) rather than plain text.
                var capabilities = dlg.PrintQueue.GetPrintCapabilities(dlg.PrintTicket);
                double pw = capabilities.PageImageableArea?.ExtentWidth  ?? dlg.PrintableAreaWidth;
                double ph = capabilities.PageImageableArea?.ExtentHeight ?? dlg.PrintableAreaHeight;
                double panelW = ActiveReportPanel.ActualWidth > 0 ? ActiveReportPanel.ActualWidth : pw - 96;

                _printReportPanel = CreateLightPrintReportPanel(panelW);
                _reportResourceScope = _printReportPanel;
                DisplayReport(screenReportLines);

                // The ActiveReportPanel lives inside a ScrollViewer whose viewport constrains layout,
                // so ActualHeight only reflects the visible area.  Force an unconstrained measure+arrange
                // at the panel's real width so DesiredSize.Height equals the full scrollable content height.
                ActiveReportPanel.Measure(new Size(panelW, double.PositiveInfinity));
                ActiveReportPanel.Arrange(new Rect(0, 0, panelW, ActiveReportPanel.DesiredSize.Height));
                ActiveReportPanel.UpdateLayout();

                var paginator = new ReportVisualPaginator(ActiveReportPanel, new Size(pw, ph));
                dlg.PrintDocument(paginator, "FishLens Report");
            }
            finally
            {
                _reportResourceScope = null;
                _printReportPanel = null;
            }
        }

        private StackPanel CreateLightPrintReportPanel(double width)
        {
            var panel = new StackPanel
            {
                Width = Math.Max(720, width),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            panel.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("Themes/NormalTheme.xaml", UriKind.Relative)
            });

            return panel;
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
            int confidenceCount = 0;   // tracks rows that actually have a positive confidence value
            double totalCorrectness = 0;
            int correctnessCount = 0;
            var uniqueDates = new HashSet<DateTime>();
            var uniqueVideos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string line in csvLines)
            {
                string[] columns = line.Split(',');

                if (columns.Length < 8)
                    continue;

                // CSV layout (reference):
                // 0: video_file, 1: location, 2: species, 3: species_confidence, 4: likely_class,
                // 5: confidence, 6: direction, 7: start_time_sec, 8: end_time_sec, 9: video_timestamp
                string videoName = columns.Length > 0 ? columns[0] : string.Empty;

                // Capture both the species column (index 2) and the likely_class (index 4).
                string species = string.Empty;
                if (columns.Length > 2)
                {
                    species = columns[2].Trim();
                }
                else
                {
                    species = string.Empty;
                }

                string likelyClass = columns.Length > 4 ? columns[4].Trim() : string.Empty;

                // Direction is at index 6 in the CSV layout.
                string direction = string.Empty;
                if (columns.Length > 6)
                {
                    direction = columns[6].Trim();
                }

                ProcessSpeciesData(stats, species, likelyClass);
                if (!string.IsNullOrWhiteSpace(videoName))
                    uniqueVideos.Add(videoName);

                // No-fish rows only contribute to NoFishCount and TotalVideoCount.
                // Skip all fish-specific stats (direction, location, hourly, daily, confidence).
                if (IsNoFishClass(likelyClass))
                    continue;

                ProcessDirectionData(stats, direction);
                // Only track fish detections in VideoDetections (for bar charts)
                ProcessVideoData(stats, videoName);
                double rowConf = ProcessConfidenceData(stats, columns);
                if (rowConf > 0)
                {
                    totalConfidence += rowConf;
                    confidenceCount++;
                }

                // col 9: video_timestamp (full datetime string)
                DateTime? timestamp = null;
                if (columns.Length > 9)
                {
                    if (DateTime.TryParse(columns[9].Trim(), out DateTime ts))
                        timestamp = ts;
                }

                if (timestamp.HasValue)
                {
                    DateTime dateOnly = timestamp.Value.Date;
                    uniqueDates.Add(dateOnly);

                    if (stats.DetectionsByDate.ContainsKey(dateOnly))
                        stats.DetectionsByDate[dateOnly]++;
                    else
                        stats.DetectionsByDate[dateOnly] = 1;

                    ProcessGroupedByDateTime(stats, timestamp.Value, species);

                    // Track min/max detection timestamps
                    if (!stats.MinDetectionTimestamp.HasValue || timestamp.Value < stats.MinDetectionTimestamp.Value)
                        stats.MinDetectionTimestamp = timestamp.Value;
                    if (!stats.MaxDetectionTimestamp.HasValue || timestamp.Value > stats.MaxDetectionTimestamp.Value)
                        stats.MaxDetectionTimestamp = timestamp.Value;

                    // Hourly counts
                    int hr = timestamp.Value.Hour;
                    if (stats.DetectionsByHour.ContainsKey(hr)) stats.DetectionsByHour[hr]++; else stats.DetectionsByHour[hr] = 1;
                }

                // Location from column 1; fall back to name-based extraction
                string location = columns.Length > 1 ? columns[1].Trim() : string.Empty;
                if (string.IsNullOrEmpty(location))
                    location = ExtractLocationFromVideo(videoName);
                ProcessLocationData(stats, location, species);

                // Date × Location matrix
                if (timestamp.HasValue)
                {
                    DateTime dateKey = timestamp.Value.Date;
                    if (!stats.DetectionsByDateLocation.TryGetValue(dateKey, out var locDay))
                        stats.DetectionsByDateLocation[dateKey] = locDay = new Dictionary<string, int>();
                    if (locDay.ContainsKey(location)) locDay[location]++; else locDay[location] = 1;
                }

                // NEW: Process correctness/accuracy data (if available in columns)
                if (columns.Length > 6 && double.TryParse(columns[6], out double correctness))
                {
                    totalCorrectness += correctness;
                    correctnessCount++;
                }
            }

            // Exclude no-fish rows from the detection count so the "Total Detections" card reflects
            // only actual detection events, not videos where nothing was seen.
            stats.TotalDetections -= stats.NoFishCount;
            stats.TotalVideoCount  = uniqueVideos.Count;

            stats.AverageConfidence = CalculateAverageConfidence(totalConfidence, confidenceCount);

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

        private async void ScheduleDisplayReport(string[] csvLines)
        {
            int renderVersion = ++_reportRenderVersion;

            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await Task.Delay(ReportLayoutSettleDelayMs);

            if (!_isHistoryLoaded || renderVersion != _reportRenderVersion)
                return;

            reportScrollViewer.UpdateLayout();
            DisplayReport(csvLines);
        }

        // **************************************************
        // Function: DisplayReport
        // Description: Renders the enhanced visual report with analytics and charts
        private void DisplayReport(string[] csvLines)
        {
            try
            {
                _lastFilteredLines = csvLines;
                _lastGroupedLinesByRun = IsAllHistoryScope ? GroupLinesByRun(csvLines) : new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                ConfigureReportView();
                ClearPreviousReport();

                var stats = CalculateStatistics(csvLines);
                _lastStats = stats;
                _currentReportText = GenerateReportText(stats);
                if (IsAllHistoryScope && _lastGroupedLinesByRun.Count > 1)
                    BuildAllHistoryVisualReport(stats, _lastGroupedLinesByRun);
                else
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
            var subtitle = CreateSubtitleTextBlock(_lastStats);

            headerPanel.Children.Add(title);
            headerPanel.Children.Add(subtitle);
            ActiveReportPanel.Children.Add(headerPanel);
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
            ActiveReportPanel.Children.Add(grid);
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
            // Only show fish; this is a fish monitoring application, so bird detections are not charted.
            AddBarChart("Fish Detected", stats.FishCount, Math.Max(1, stats.TotalDetections), "#1E88E5");
        }

        // **************************************************
        // Function: AddMovementCharts
        // Description: Adds bar charts showing upstream, downstream, and indecisive movement
        private void AddMovementCharts(ReportStatistics stats)
        {
            int indecisive = stats.TotalDetections - stats.UpstreamCount - stats.DownstreamCount;
            if (indecisive < 0) indecisive = 0;
            AddBarChart("Upstream",   stats.UpstreamCount,   stats.TotalDetections, "#2AB5B5");
            AddBarChart("Downstream", stats.DownstreamCount, stats.TotalDetections, "#E05C5C");
            AddBarChart("Indecisive", indecisive,            stats.TotalDetections, "#E8A038");
        }

        // **************************************************
        // Function: AddVideoBreakdown
        // Description: Adds bar charts for top 10 videos by detection count.
        //   Bar fill = fish in this video / total fish (real share).
        //   Label shows "n / total" with the column header printed once at the top.
        private void AddVideoBreakdown(ReportStatistics stats)
        {
            if (stats.VideoDetections.Count == 0)
                return;

            // One-time column header so the fraction format is explained without repeating per row
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 4), Tag = "col_header" };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.Children.Add(new TextBlock
            {
                Text       = "Video",
                FontSize   = 11,
                FontStyle  = FontStyles.Italic,
                Foreground = Brush("#657786")
            });
            var countHeader = new TextBlock
            {
                Text       = "fish / total",
                FontSize   = 11,
                FontStyle  = FontStyles.Italic,
                Foreground = Brush("#657786")
            };
            Grid.SetColumn(countHeader, 1);
            headerGrid.Children.Add(countHeader);
            ActiveReportPanel.Children.Add(headerGrid);

            foreach (var kvp in stats.VideoDetections.OrderByDescending(x => x.Value).Take(10))
            {
                var container = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

                // Label row: video name left, "n / total" right
                var labelGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                labelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                labelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameText = new TextBlock
                {
                    Text         = ShortenVideoPath(kvp.Key),
                    FontSize     = 13,
                    FontWeight   = FontWeights.Medium,
                    Foreground   = Brush("#14171A"),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var fractionText = new TextBlock
                {
                    Text       = $"{kvp.Value} / {stats.TotalDetections}",
                    FontSize   = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brush("#7E57C2")
                };
                Grid.SetColumn(fractionText, 1);
                labelGrid.Children.Add(nameText);
                labelGrid.Children.Add(fractionText);

                // Bar fill = real share of total fish
                var barContainer = CreateBarChartBars(kvp.Value, stats.TotalDetections, "#7E57C2");

                container.Children.Add(labelGrid);
                container.Children.Add(barContainer);
                ActiveReportPanel.Children.Add(container);
            }
        }

        // **************************************************
        // Function: AddTimeDistribution
        // Description: Adds a 24-hour activity distribution chart
        private void AddTimeDistribution(ReportStatistics stats)
        {
            // Build ordered hour points (0-23)
            var points = new List<(string label, int value)>();
            for (int hour = 0; hour < 24; hour++)
            {
                int count = stats.DetectionsByHour.ContainsKey(hour) ? stats.DetectionsByHour[hour] : 0;
                points.Add(($"{hour:D2}:00", count));
            }

            var chart = CreateLineChart(points, "#FF6F00", 140, ActiveReportPanel?.ActualWidth ?? 0);
            ActiveReportPanel.Children.Add(chart);
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
                Foreground = Brush("#14171A"),
                Margin = new Thickness(0, 10, 0, 12),
                Tag = "section_header"   // used by ReportVisualPaginator for header repetition
            };
            ActiveReportPanel.Children.Add(textBlock);
        }

        // **************************************************
        // Function: AddBarChart
        // Description: Creates and adds a horizontal bar chart with label and value.
        //   maxValue - denominator for both bar fill width and the label percentage
        //   isCount  - when true, label shows the raw count only (no percentage), used for
        //              within-group comparisons like video/location/date breakdowns
        private void AddBarChart(string label, int value, int maxValue, string color, bool isCount = false)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            var labelGrid = CreateBarChartLabelGrid(label, value, maxValue, color, isCount);
            var barContainer = CreateBarChartBars(value, maxValue, color);

            container.Children.Add(labelGrid);
            container.Children.Add(barContainer);
            ActiveReportPanel.Children.Add(container);
        }

        // **************************************************
        // Function: AddSpacer
        // Description: Adds vertical spacing between report sections
        private void AddSpacer(int height)
        {
            ActiveReportPanel.Children.Add(new Border { Height = height });
        }

        // **************************************************
        // Function: ShowEmptyState
        // Description: Displays an empty state message when no history is available
        public void ShowEmptyState()
        {
            var emptyPanel = CreateEmptyStatePanel();
            // Add empty state to the report panel (report area) since historyList was removed
            ActiveReportPanel.Children.Add(emptyPanel);
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
        // Function: PassesCameraFilter
        // Description: Checks if a location value passes the current camera/location filter
        private bool PassesCameraFilter(string location)
        {
            return _filterCamera == "All" || location.Equals(_filterCamera, StringComparison.OrdinalIgnoreCase);
        }

        // **************************************************
        // Function: LoadLocationFilter
        // Description: Populates the location ComboBox from app.Configuration (populated from DB at sign-in).
        private void LoadLocationFilter()
        {
            try
            {
                var names = new List<string> { "All Locations" };
                var locations = (Application.Current as App)?.Configuration?.Locations;
                if (locations != null)
                {
                    foreach (var loc in locations)
                    {
                        if (!string.IsNullOrWhiteSpace(loc.Name) && !names.Contains(loc.Name))
                            names.Add(loc.Name);
                    }
                }

                cameraFilter.ItemsSource = names;
                cameraFilter.SelectedIndex = 0;
            }
            catch { /* non-critical; leave dropdown empty */ }
        }

        private void OnLocationChanged()
        {
            Dispatcher.Invoke(LoadLocationFilter);
        }

        private void OnRunChanged()
        {
            Dispatcher.Invoke(LoadRunFilter);
        }

        // **************************************************
        // Function: FilterPanel_PreviewMouseWheel
        // Description: Normalises scroll speed on the filter panel ScrollViewer
        private void FilterPanel_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta * 0.2);
                e.Handled = true;
            }
        }

        // **************************************************
        // Function: ReportPanel_PreviewMouseWheel
        // Description: Slows scroll speed on the report ScrollViewer (content is tall)
        private void ReportPanel_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta * 0.5);
                e.Handled = true;
            }
        }

        // **************************************************
        // Function: RunFilter_SelectionChanged
        // Description: Constrains date pickers to the selected run's data range
        public void RunFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (runFilter?.SelectedItem is not string selectedRun) return;
            _filterRun = selectedRun switch
            {
                "All History"      => "All",
                "Current Session"  => "CurrentSession",
                _                  => selectedRun
            };
            ConstrainDatePickersForRun(_filterRun);

            if (_isHistoryLoaded)
                GenerateReportClick(sender, new RoutedEventArgs());
        }

        // **************************************************
        // Function: ConstrainDatePickersForRun
        // Description: Reads the CSV for a run to find min/max dates and sets picker bounds
        private void ConstrainDatePickersForRun(string filterRun)
        {
            try
            {
                DateTime? minDate = null;
                DateTime? maxDate = null;

                foreach (string line in ReadAllDataLines(filterRun))
                {
                    var cols = line.Split(',');
                    if (cols.Length > 9 && DateTime.TryParse(cols[9].Trim(), out DateTime ts))
                    {
                        if (!minDate.HasValue || ts < minDate.Value) minDate = ts;
                        if (!maxDate.HasValue || ts > maxDate.Value) maxDate = ts;
                    }
                }

                ApplyDatePickerBounds(minDate?.Date, maxDate?.Date);
            }
            catch { /* non-critical */ }
        }

        private void ApplyDatePickerBounds(DateTime? minDate, DateTime? maxDate)
        {
            DateTime start = minDate ?? DateTime.Today;
            DateTime end = maxDate ?? start;
            if (end < start)
                end = start;

            if (startDatePicker != null)
            {
                startDatePicker.DisplayDateStart = start;
                startDatePicker.DisplayDateEnd = end;
                startDatePicker.DisplayDate = start;
                if (startDatePicker.SelectedDate.HasValue &&
                    (startDatePicker.SelectedDate.Value.Date < start || startDatePicker.SelectedDate.Value.Date > end))
                    startDatePicker.SelectedDate = null;
            }

            if (endDatePicker != null)
            {
                endDatePicker.DisplayDateStart = start;
                endDatePicker.DisplayDateEnd = end;
                endDatePicker.DisplayDate = end;
                if (endDatePicker.SelectedDate.HasValue &&
                    (endDatePicker.SelectedDate.Value.Date < start || endDatePicker.SelectedDate.Value.Date > end))
                    endDatePicker.SelectedDate = null;
            }
        }

        // **************************************************
        // Function: LoadRunFilter
        // Description: Populates the run ComboBox from app.Configuration (populated from DB at sign-in).
        private void LoadRunFilter()
        {
            try
            {
                // 'Current Session' always appears first after 'All History' so the user can
                // quickly report on just the videos they have analyzed in this startup session.
                var items = new List<string> { "All History", "Current Session" };
                var runs = (Application.Current as App)?.Configuration?.Runs;
                if (runs != null)
                {
                    foreach (var run in runs)
                    {
                        if (!string.IsNullOrWhiteSpace(run.Name))
                            items.Add(run.Name);
                    }
                }

                if (runFilter != null)
                {
                    runFilter.ItemsSource = items;
                    runFilter.SelectedIndex = 0;
                }
            }
            catch { /* non-critical */ }
        }

        // **************************************************
        // Function: PassesConfidenceFilter
        // Description: Checks if a confidence value passes the minimum confidence threshold
        private bool PassesConfidenceFilter(double? avgConfidence)
        {
            // If no threshold set, allow all
            if (_filterMinConfidence <= 0.0) return true;

            if (!avgConfidence.HasValue)
            {
                // If we have no data for confidence, don't include when a positive filter is set
                return false;
            }

            return avgConfidence.Value >= _filterMinConfidence;
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

            // Update camera/location filter
            if (cameraFilter?.SelectedItem != null)
            {
                string content = cameraFilter.SelectedItem.ToString();
                _filterCamera = content.Contains("All Locations") ? "All" : content;
            }

            // Update run filter
            if (runFilter?.SelectedItem is string selectedRun)
                _filterRun = selectedRun switch
                {
                    "All History"     => "All",
                    "Current Session" => "CurrentSession",
                    _                 => selectedRun
                };
        }

        private bool RefreshReportFromSelectedScope(bool deferDisplay = false)
        {
            // All History and run-specific scopes use the database when organization context
            // is available; Current Session remains local to the active session CSVs.
            string[] dataLines = ReadAllDataLines(_filterRun);

            if (dataLines.Length == 0)
            {
                ShowBlankReportState();
                return false;
            }

            var filteredLines = ApplyFilters(dataLines);

            if (filteredLines.Length == 0)
            {
                ShowBlankReportState();
                return false;
            }

            if (deferDisplay)
                ScheduleDisplayReport(filteredLines);
            else
                DisplayReport(filteredLines);

            UpdatePrintReportButtonState(true);
            return true;
        }

        // **************************************************
        // Function: ReadAllDataLines
        // Description: Returns all data rows (no header) for the given run scope.
        //              For CurrentSession, merges session_fish.csv with session_no_fish.csv.
        //              No-fish rows are padded to the 10-column fish schema so all existing
        //              filter and statistics logic applies without modification:
        //                col 0  video_file
        //                col 1  location
        //                col 2  species       (empty ? normalised to "No Fish Detected" in stats)
        //                col 3  species_conf  (0)
        //                col 4  likely_class  ("no_fish")
        //                col 5  confidence    (0)
        //                col 6  direction     (empty)
        //                col 7  start_time    (0)
        //                col 8  end_time      (0)
        //                col 9  video_timestamp
        // **************************************************
        private string[] ReadAllDataLines(string filterRun)
        {
            bool useDb    = string.IsNullOrWhiteSpace(filterRun) || filterRun == "All";
            bool useRunDb = !useDb && filterRun != "CurrentSession";
            var app = Application.Current as App;
            string[] result;

            if (useDb && app != null && app.CurrentOrganizationId > 0)
            {
                result = ReadAllDataLinesFromDb(app.CurrentOrganizationId, app.connectionString);
            }
            else if (useRunDb && app != null && app.CurrentOrganizationId > 0)
            {
                result = ReadAllDataLinesFromDb(app.CurrentOrganizationId, app.connectionString, filterRun);
            }
            else
            {
                string csvPath = GetCsvPathForRun(filterRun);
                var lines = new List<string>();

                if (File.Exists(csvPath))
                    lines.AddRange(File.ReadAllLines(csvPath).Skip(1)); // skip header

                if (filterRun == "CurrentSession")
                {
                    string activeRun = app?.ActiveRun ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(activeRun))
                    {
                        string noFishPath = _pathResolver.ResolveSessionNoFishCsvPath(activeRun);
                        if (File.Exists(noFishPath))
                        {
                            foreach (string line in File.ReadLines(noFishPath).Skip(1))
                            {
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    var parts = line.Split(',');
                                    if (parts.Length >= 3)
                                    {
                                        // Pad to fish schema; timestamp (col 2 in no-fish) moves to col 9 and run to col 10.
                                        string padded = $"{parts[0]},{parts[1]},,0,no_fish,0,,0,0,{parts[2]},{activeRun}";
                                        lines.Add(padded);
                                    }
                                }
                            }
                        }
                    }
                }

                result = lines.ToArray();
            }

            return result;
        }

        // **************************************************
        // Function: ReadAllDataLinesFromDb
        // Description: Queries FishDetections for the org and returns rows in the same
        //              11-column CSV format used by all existing filter and stats logic.
        // **************************************************
        private string[] ReadAllDataLinesFromDb(int orgId, string connectionString, string runName = null)
        {
            var lines = new List<string>();
            try
            {
                using var conn = new SqlConnection(connectionString);
                conn.Open();
                using var cmd = new SqlCommand($"{DatabaseConfig.Schema}.GetFishDetectionsByOrg", conn);
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@pOrgId",        orgId);
                cmd.Parameters.AddWithValue("@pRunName",      runName != null ? (object)runName : DBNull.Value);
                cmd.Parameters.AddWithValue("@pLocationName", DBNull.Value);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string videoFile   = reader["VideoFile"]?.ToString()    ?? string.Empty;
                    string location    = reader["LocationName"]?.ToString() ?? string.Empty;
                    string species     = reader["Species"]?.ToString()      ?? string.Empty;
                    string speciesConf = reader["SpeciesConfidence"] == DBNull.Value
                        ? "0" : ((double)reader["SpeciesConfidence"]).ToString("F4");
                    string likelyClass = reader["LikelyClass"]?.ToString()  ?? string.Empty;
                    string confidence  = reader["Confidence"] == DBNull.Value
                        ? "0" : ((double)reader["Confidence"]).ToString("F4");
                    string direction   = reader["Direction"]?.ToString()    ?? string.Empty;
                    string startTime   = reader["StartTimeSec"]?.ToString() ?? "0";
                    string endTime     = reader["EndTimeSec"]?.ToString()   ?? "0";
                    string timestamp   = reader["DetectionTimestamp"] == DBNull.Value
                        ? string.Empty
                        : ((DateTime)reader["DetectionTimestamp"]).ToString("yyyy/MM/dd HH:mm:ss");
                    string run         = reader["RunName"]?.ToString()      ?? string.Empty;

                    lines.Add($"{videoFile},{location},{species},{speciesConf},{likelyClass},{confidence},{direction},{startTime},{endTime},{timestamp},{run}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading detections from DB for org {OrgId}", orgId);
            }
            return lines.ToArray();
        }

        private string ExtractRunFromColumns(string[] columns)
        {
            if (columns.Length > 10 && !string.IsNullOrWhiteSpace(columns[10].Trim()))
                return columns[10].Trim();

            if (columns.Length > 0)
            {
                try
                {
                    string videoPath = columns[0].Trim();
                    string folderPath = System.IO.Path.GetDirectoryName(videoPath);
                    if (!string.IsNullOrWhiteSpace(folderPath))
                    {
                        string folderName = System.IO.Path.GetFileName(folderPath);
                        if (!string.IsNullOrWhiteSpace(folderName))
                            return folderName;
                    }
                }
                catch { }
            }

            return "Unknown Run";
        }

        private Dictionary<string, string[]> GroupLinesByRun(string[] csvLines)
        {
            return csvLines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .GroupBy(line => ExtractRunFromColumns(line.Split(',')), StringComparer.OrdinalIgnoreCase)
                .OrderBy(group =>
                {
                    var stats = CalculateStatistics(group.ToArray());
                    return stats.MinDetectionTimestamp ?? DateTime.MaxValue;
                })
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        }

        // **************************************************
        // Function: GetCsvPathForRun
        // Description: Returns the primary CSV path based on the active run filter
        private string GetCsvPathForRun(string runFilter)
        {
            if (string.IsNullOrWhiteSpace(runFilter) || runFilter == "All")
                return _pathResolver.ResolveAllTimeMasterFishCsvPath();

            if (runFilter == "CurrentSession")
            {
                string activeRun = (Application.Current as App)?.ActiveRun ?? string.Empty;
                if (string.IsNullOrWhiteSpace(activeRun))
                    return _pathResolver.ResolveAllTimeMasterFishCsvPath(); // fallback if no run active
                return _pathResolver.ResolveSessionCsvPath(activeRun);
            }

            return _pathResolver.ResolveRunCsvPath(runFilter);
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
                if (columns.Length == 0) continue;

                // video name
                string videoName = columns.Length > 0 ? columns[0].Trim() : string.Empty;

                // species: column 2
                string species = columns.Length > 2 ? columns[2].Trim() : string.Empty;

                // direction: column 6
                string direction = columns.Length > 6 ? columns[6].Trim() : string.Empty;

                // confidence: column 5
                double? avgConf = null;
                if (columns.Length > 5)
                {
                    var raw = columns[5].Trim().TrimEnd('%');
                    if (double.TryParse(raw, out var d))
                    {
                        if (d > 1) d = d / 100.0;
                        avgConf = d;
                    }
                }

                // timestamp: column 9 (full datetime string)
                DateTime? timestamp = null;
                if (columns.Length > 9)
                {
                    if (DateTime.TryParse(columns[9].Trim(), out DateTime ts)) timestamp = ts;
                }

                // location: column 1, fall back to filename parse
                string location = columns.Length > 1 && !string.IsNullOrWhiteSpace(columns[1].Trim())
                    ? columns[1].Trim()
                    : ExtractLocationFromVideo(videoName);

                // Exclude non-fish rows from the visual table — bird detections
                // skip the per-detection filters, but still appear in the table.
                string likelyClassRaw = columns.Length > 4 ? columns[4].Trim() : string.Empty;
                bool isNoFish = IsNoFishClass(likelyClassRaw);
                bool isBird   = likelyClassRaw.Equals("bird", StringComparison.OrdinalIgnoreCase);

                if (isBird) continue; // birds never shown in table

                if (!isNoFish)
                {
                    // Apply per-detection filters only to fish rows
                    if (!PassesSpeciesFilter(species))    continue;
                    if (!PassesDirectionFilter(direction)) continue;
                    if (!PassesConfidenceFilter(avgConf)) continue;
                }

                // Date and camera filters apply to all rows (including no-fish)
                if (!PassesDateFilter(timestamp))   continue;
                if (!PassesCameraFilter(location))  continue;

                filtered.Add(line);
            }

            return filtered.ToArray();
        }

        // **************************************************
        // Function: PassesDateFilter
        // Description: Checks if a detection date falls within the selected date range
        private bool PassesDateFilter(DateTime? detectionTimestamp)
        {
            // If no date filters set, pass everything
            if (!_filterStartDate.HasValue && !_filterEndDate.HasValue)
                return true;

            // If there's no timestamp available for the row, exclude it when a date filter is active
            if (!detectionTimestamp.HasValue)
                return false;

            var detectionDate = detectionTimestamp.Value.Date;
            if (_filterStartDate.HasValue && detectionDate < _filterStartDate.Value.Date)
                return false;

            if (_filterEndDate.HasValue && detectionDate > _filterEndDate.Value.Date)
                return false;

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
                GroupedByLocation = new Dictionary<string, Dictionary<string, int>>(),
                DetectionsByDateLocation = new Dictionary<DateTime, Dictionary<string, int>>()
            };
        }


        // **************************************************
        // Function: IsNoFishClass
        // Description: Returns true for any LikelyClass value that represents a "no fish" outcome —
        //              covers both "no_fish" (YOLO found no tracks) and "not_fish" (YOLO or user
        //              classified the track as not a fish). Both should count as "no fish" in reports.
        // **************************************************
        private static bool IsNoFishClass(string likelyClass)
        {
            bool result = false;
            if (!string.IsNullOrWhiteSpace(likelyClass))
            {
                result = likelyClass.Equals("no_fish",  StringComparison.OrdinalIgnoreCase)
                      || likelyClass.Equals("not_fish", StringComparison.OrdinalIgnoreCase);
            }
            return result;
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
                else if (IsNoFishClass(likelyClass))
                    stats.NoFishCount++;
            }
            else
            {
                // Fallback to species column if likelyClass isn't provided
                if (species.Equals("fish", StringComparison.OrdinalIgnoreCase))
                    stats.FishCount++;
                else if (species.Equals("bird", StringComparison.OrdinalIgnoreCase))
                    stats.BirdCount++;
            }

            // Track breakdown by species label; normalize blank entries to a readable display name.
            string displaySpecies = string.IsNullOrWhiteSpace(species)
                ? (likelyClass.Equals("fish", StringComparison.OrdinalIgnoreCase)
                    ? "Unknown Species"
                    : "No Fish Detected")
                : species;

            if (stats.SpeciesBreakdown.ContainsKey(displaySpecies))
                stats.SpeciesBreakdown[displaySpecies]++;
            else
                stats.SpeciesBreakdown[displaySpecies] = 1;
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
            // confidence is at index 5 per CSV layout (0-based).
            if (columns.Length > 5)
            {
                var raw = columns[5].Trim().TrimEnd('%');
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
        private void AppendReportHeader(StringBuilder sb, ReportStatistics stats, string style = "Standard Report")
        {
            sb.AppendLine("-------------------------------------------------------");
            sb.AppendLine("              FISHLENS ANALYSIS REPORT");
            sb.AppendLine($"              {style.ToUpper()}");
            sb.AppendLine("-------------------------------------------------------");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            if (stats.MinDetectionTimestamp.HasValue && stats.MaxDetectionTimestamp.HasValue)
            {
                sb.AppendLine($"Data Range: {stats.MinDetectionTimestamp.Value:yyyy-MM-dd HH:mm:ss} to {stats.MaxDetectionTimestamp.Value:yyyy-MM-dd HH:mm:ss}");
            }
            sb.AppendLine();
        }

        // **************************************************
        // Function: AppendSummaryStatistics
        // Description: Legacy summary writer retained because text export still routes through it.
        private void AppendSummaryStatistics(StringBuilder sb, ReportStatistics stats) { }

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
            sb.AppendLine("-------------------------------------------------------");
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
                Foreground = Brush("#0D3640"),
                Margin = new Thickness(0, 0, 0, 5)
            };
        }

        // **************************************************
        // Function: CreateSubtitleTextBlock
        // Description: Creates the subtitle text block showing generation time
        private TextBlock CreateSubtitleTextBlock(ReportStatistics stats = null, string prefix = null)
        {
            string generatedText = $"Generated on {DateTime.Now:MMMM dd, yyyy} at {DateTime.Now:h:mm tt}";
            string dateRangeText = string.Empty;
            if (stats?.MinDetectionTimestamp.HasValue == true && stats?.MaxDetectionTimestamp.HasValue == true)
            {
                dateRangeText = $"{stats.MinDetectionTimestamp.Value:MMMM dd, yyyy} - {stats.MaxDetectionTimestamp.Value:MMMM dd, yyyy}";
            }

            string text = generatedText;
            if (!string.IsNullOrWhiteSpace(prefix) && !string.IsNullOrWhiteSpace(dateRangeText))
                text = $"{prefix} | {dateRangeText} | {generatedText}";
            else if (!string.IsNullOrWhiteSpace(prefix))
                text = $"{prefix} | {generatedText}";
            else if (!string.IsNullOrWhiteSpace(dateRangeText))
                text = $"{dateRangeText} | {generatedText}";

            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = Brush("#657786")
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
                Background = Brush("#F5F8FA"),
                BorderBrush = Brush("#E1E8ED"),
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
                Foreground = Brush(color)
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
                Foreground = Brush("#657786")
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
                Foreground = Brush("#14171A"),
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
                Foreground = Brush(color)
            };
            Grid.SetColumn(valueText, 1);

            labelGrid.Children.Add(labelText);
            labelGrid.Children.Add(valueText);

            return labelGrid;
        }

        // **************************************************
        // Function: CreateBarChartBars
        // Description: Creates the visual bar elements using proportional star-column layout
        //              so 100% always fills the full track regardless of screen width.
        private Grid CreateBarChartBars(int value, int maxValue, string color)
        {
            double pct   = maxValue > 0 ? Math.Clamp(value * 100.0 / maxValue, 0, 100) : 0;
            double empty = 100.0 - pct;

            var grid = new Grid { Height = 24, Margin = new Thickness(0, 2, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, pct),   GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, empty), GridUnitType.Star) });

            // Gray track spans both columns
            var track = new Border
            {
                Background        = Brush("#E1E8ED"),
                CornerRadius      = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetColumnSpan(track, 2);

            // Colored fill in column 0
            var fill = new Border
            {
                Background        = Brush(color),
                CornerRadius      = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetColumn(fill, 0);

            grid.Children.Add(track);
            grid.Children.Add(fill);
            return grid;
        }

        // **************************************************
        // Function: CreateLineChart
        // Description: Renders a line chart into a single DrawingVisual-backed Image so the entire
        //              chart is ONE UIElement (not hundreds of Lines/Ellipses/TextBlocks).
        //              This is the primary scroll-performance fix for large daily/hourly charts.
        private FrameworkElement CreateLineChart(List<(string label, int value)> points, string color, double height = 180, double availableWidth = 0)
        {
            // Render at actual width or fall back; use an Image that stretches to fill
            double totalWidth = (availableWidth > 200) ? availableWidth : GetReportContentWidth();

            const double leftMargin   = 56;
            const double rightMargin  = 40;
            const double topMargin    = 8;
            const double bottomMargin = 36;

            int maxValue    = points.Count > 0 ? Math.Max(1, points.Max(p => p.value)) : 1;
            double chartW   = totalWidth - leftMargin - rightMargin;
            double chartH   = height - topMargin - bottomMargin;
            double baseY    = topMargin + chartH;

            var dv  = new DrawingVisual();
            var tf  = new Typeface("Segoe UI");
            var gray     = Brush("#657786");
            var gridPen  = new Pen(Brush("#E6EDF2"), 1)  { DashStyle = DashStyles.Dash }; gridPen.Freeze();
            var axisPen  = new Pen(Brush("#CCDDE6"), 1.5);                                axisPen.Freeze();
            var dataPen  = new Pen(Brush(color), 2);                                      dataPen.Freeze();
            var dotFill  = Brush(color);
            var dotStroke = new Pen(Brush("#FFFFFF"), 1);                                dotStroke.Freeze();

            using (var ctx = dv.RenderOpen())
            {
                // --- Y-axis grid lines + labels ---
                int yTicks = Math.Min(4, maxValue);
                if (yTicks <= 0) yTicks = 1;
                for (int t = 0; t <= yTicks; t++)
                {
                    double frac  = (double)t / yTicks;
                    double y     = topMargin + frac * chartH;
                    ctx.DrawLine(gridPen, new Point(leftMargin, y), new Point(leftMargin + chartW, y));

                    int lv = (int)Math.Round((double)(yTicks - t) * maxValue / yTicks);
                    var ft = new FormattedText(lv.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                                               FlowDirection.LeftToRight, tf, 11, gray, 96);
                    ctx.DrawText(ft, new Point(leftMargin - ft.Width - 4, y - ft.Height / 2));
                }

                // --- X axis ---
                ctx.DrawLine(axisPen, new Point(leftMargin, baseY), new Point(leftMargin + chartW, baseY));

                // --- Build polyline points + data markers ---
                if (points.Count > 0)
                {
                    var geom = new StreamGeometry();
                    using (var sg = geom.Open())
                    {
                        int labelEvery = Math.Max(1, points.Count / 8);
                        for (int i = 0; i < points.Count; i++)
                        {
                            double x = leftMargin + (points.Count == 1 ? chartW / 2
                                                      : i * (chartW / Math.Max(1, points.Count - 1)));
                            double y = topMargin + (1.0 - (double)points[i].value / maxValue) * chartH;

                            if (i == 0) sg.BeginFigure(new Point(x, y), false, false);
                            else        sg.LineTo(new Point(x, y), true, false);

                            // Dot
                            ctx.DrawEllipse(dotFill, dotStroke, new Point(x, y), 3, 3);

                            // X tick
                            ctx.DrawLine(axisPen, new Point(x, baseY), new Point(x, baseY + 5));

                            // X label (sparse)
                            if (i % labelEvery == 0 || i == points.Count - 1)
                            {
                                var lft = new FormattedText(points[i].label,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    FlowDirection.LeftToRight, tf, 10, gray, 96);
                                ctx.DrawText(lft, new Point(x - lft.Width / 2, baseY + 8));
                            }
                        }
                        sg.Close();
                    }
                    geom.Freeze();
                    ctx.DrawGeometry(null, dataPen, geom);
                }
            }

            // Wrap in an Image so it's a single UIElement in the panel
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(totalWidth), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();

            var img = new System.Windows.Controls.Image
            {
                Source              = rtb,
                Width               = totalWidth,
                Height              = height,
                Stretch             = Stretch.None,
                Margin              = new Thickness(0, 6, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            return img;
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
                Foreground = Brush("#14171A"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            });

            emptyPanel.Children.Add(new TextBlock
            {
                Text = "Run an analysis to see your detection records here",
                FontSize = 14,
                Foreground = Brush("#657786"),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            return emptyPanel;
        }

        #endregion

        #region Helper Methods - File Operations

        // Function: ShortenVideoPath
        // Description: Returns a compact display path: "parent_folder/filename" for table columns.
        //              Keeps paths readable without overflowing narrow cells.
        private static string ShortenVideoPath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return fullPath;
            string fileName  = System.IO.Path.GetFileName(fullPath);
            string parentDir = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(fullPath) ?? string.Empty);
            return string.IsNullOrEmpty(parentDir) ? fileName : $"{parentDir}/{fileName}";
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
            ActiveReportPanel.ClearValue(FrameworkElement.WidthProperty);
            ActiveReportPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            // Force a layout pass so ActiveReportPanel.ActualWidth is correct before charts are rendered.
            reportScrollViewer.UpdateLayout();
        }

        private double GetReportContentWidth()
        {
            double viewport = reportScrollViewer?.ViewportWidth ?? 0;
            if (viewport > 260) return Math.Max(200, viewport - 60);

            double scrollWidth = reportScrollViewer?.ActualWidth ?? 0;
            if (scrollWidth > 260) return Math.Max(200, scrollWidth - 90);

            double panelWidth = ActiveReportPanel?.ActualWidth ?? 0;
            if (panelWidth > 200) return panelWidth;

            return 720;
        }

        // **************************************************
        // Function: ClearPreviousReport
        // Description: Clears all children from the report panel
        private void ClearPreviousReport()
        {
            ActiveReportPanel.Children.Clear();
        }

        private void UpdatePrintReportButtonState(bool hasPrintableReport)
        {
            if (printReportButton != null)
                printReportButton.IsEnabled = hasPrintableReport;
        }

        private void ShowBlankReportState()
        {
            _reportRenderVersion++;
            _lastFilteredLines = Array.Empty<string>();
            _lastGroupedLinesByRun.Clear();
            _lastStats = null;
            _currentReportText = null;
            UpdatePrintReportButtonState(false);

            ClearPreviousReport();
            reportScrollViewer.Visibility = Visibility.Collapsed;
            videoPlayer.Visibility = Visibility.Collapsed;
            placeholderPanel.Visibility = Visibility.Visible;
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
        // Function: BuildVisualReport
        // Description: Dispatches to the appropriate report builder based on the selected mode
        private void BuildVisualReport(ReportStatistics stats, bool includeHeader = true)
        {
            switch (_currentReportStyle)
            {
                case "Summary Only":
                    BuildSummaryOnlyReport(stats, includeHeader);
                    break;
                case "Detailed Analysis":
                    BuildDetailedReport(stats, includeHeader);
                    break;
                case "Data Table View":
                    BuildDataTableReport(stats, _lastFilteredLines, includeHeader);
                    break;
                default:
                    BuildStandardReport(stats, includeHeader);
                    break;
            }
        }

        private void BuildAllHistoryVisualReport(ReportStatistics combinedStats, Dictionary<string, string[]> linesByRun)
        {
            AddReportHeader(combinedStats.TotalDetections);
            AddSectionTitle("Runs Included");

            foreach (var runGroup in linesByRun)
            {
                var runStats = CalculateStatistics(runGroup.Value);

                var runPanel = new Border
                {
                    Background = Brush("#F5F8FA"),
                    BorderBrush = Brush("#E1E8ED"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14),
                    Margin = new Thickness(0, 0, 0, 12)
                };

                var stack = new StackPanel();
                stack.Children.Add(new TextBlock
                {
                    Text = runGroup.Key,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brush("#0D3640"),
                    Margin = new Thickness(0, 0, 0, 4)
                });
                stack.Children.Add(CreateSubtitleTextBlock(runStats, "Run Range"));

                var summary = new TextBlock
                {
                    Text = $"Fish: {runStats.FishCount}   |   Videos: {(runStats.TotalVideoCount > 0 ? runStats.TotalVideoCount : runStats.VideoDetections.Count)}   |   Net Upstream: {runStats.UpstreamCount - runStats.DownstreamCount}",
                    FontSize = 12,
                    Foreground = Brush("#14171A"),
                    Margin = new Thickness(0, 10, 0, 0)
                };
                stack.Children.Add(summary);

                if (runStats.DetectionsByLocation.Count > 0)
                {
                    string locations = string.Join(", ", runStats.DetectionsByLocation
                        .OrderByDescending(x => x.Value)
                        .Take(3)
                        .Select(x => $"{x.Key} ({x.Value})"));
                    stack.Children.Add(new TextBlock
                    {
                        Text = $"Top Locations: {locations}",
                        FontSize = 12,
                        Foreground = Brush("#657786"),
                        Margin = new Thickness(0, 6, 0, 0)
                    });
                }

                runPanel.Child = stack;
                ActiveReportPanel.Children.Add(runPanel);
            }

            AddSpacer(16);
            AddSectionTitle("Combined Summary");
            BuildVisualReport(combinedStats, includeHeader: false);
        }

        // **************************************************
        // Function: BuildStandardReport
        // Description: Constructs all visual components of the standard report
        private void BuildStandardReport(ReportStatistics stats, bool includeHeader = true)
        {
            if (includeHeader)
                AddReportHeader(stats.TotalDetections);

            // Key metrics row; each stat appears exactly once here.
            AddMainCountSection(stats);
            AddSpacer(20);

            // Detection grouping (species / date / location)
            AddGroupBySection(stats);
            AddSpacer(20);

            AddSectionTitle("Movement Patterns & Estimates");
            AddMovementCharts(stats);
            AddUpstreamEstimation(stats);
            AddSpacer(20);

            AddSectionTitle("Confidence Distribution");
            AddConfidenceDistribution(stats);
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
        // Function: BuildSummaryOnlyReport
        // Description: Renders a compact one-page snapshot: stat cards + location summary + date range
        private void BuildSummaryOnlyReport(ReportStatistics stats, bool includeHeader = true)
        {
            if (includeHeader)
                AddReportHeader(stats.TotalDetections);
            AddSpacer(10);

            // --- Row 1: Total Fish | Total Videos | Net Upstream ---
            var row1 = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            for (int i = 0; i < 3; i++)
                row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int net = stats.UpstreamCount - stats.DownstreamCount;
            var totalCard = CreateStatCard("Total Fish", stats.FishCount.ToString(), "#1E88E5");
            Grid.SetColumn(totalCard, 0);
            var videosCard = CreateStatCard("Total Videos", stats.TotalVideoCount > 0 ? stats.TotalVideoCount.ToString() : stats.VideoDetections.Count.ToString(), "#0D3640");
            Grid.SetColumn(videosCard, 1);
            var netCard = CreateStatCard("Net Upstream", net.ToString(), "#43A047");
            Grid.SetColumn(netCard, 2);
            row1.Children.Add(totalCard);
            row1.Children.Add(videosCard);
            row1.Children.Add(netCard);
            ActiveReportPanel.Children.Add(row1);

            // --- Row 2: Upstream | Downstream | Indecisive ---
            var row2 = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            for (int i = 0; i < 3; i++)
                row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int indecisive = stats.TotalDetections - stats.UpstreamCount - stats.DownstreamCount;
            if (indecisive < 0) indecisive = 0;
            var upCard   = CreateStatCard("Upstream",   stats.UpstreamCount.ToString(),   "#2AB5B5");
            Grid.SetColumn(upCard,   0);
            var downCard = CreateStatCard("Downstream", stats.DownstreamCount.ToString(), "#E05C5C");
            Grid.SetColumn(downCard, 1);
            var indCard  = CreateStatCard("Indecisive", indecisive.ToString(),            "#E8A038");
            Grid.SetColumn(indCard,  2);
            row2.Children.Add(upCard);
            row2.Children.Add(downCard);
            row2.Children.Add(indCard);
            ActiveReportPanel.Children.Add(row2);

            // --- Date range ---
            if (stats.MinDetectionTimestamp.HasValue && stats.MaxDetectionTimestamp.HasValue)
            {
                AddSectionTitle("Date Range");
                var dateText = new TextBlock
                {
                    Text = $"{stats.MinDetectionTimestamp.Value:yyyy-MM-dd}  \u2192  {stats.MaxDetectionTimestamp.Value:yyyy-MM-dd}",
                    FontSize = 14,
                    Foreground = Brush("#14171A"),
                    Margin = new Thickness(0, 0, 0, 16)
                };
                ActiveReportPanel.Children.Add(dateText);
            }

            // --- Location summary ---
            if (stats.DetectionsByLocation.Count > 0)
            {
                AddSectionTitle("Location Summary");
                foreach (var kvp in stats.DetectionsByLocation.OrderByDescending(x => x.Value))
                {
                    var row = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0, 0, 0, 6)
                    };
                    row.Children.Add(new TextBlock
                    {
                        Text = kvp.Key,
                        FontSize = 13,
                        Foreground = Brush("#14171A"),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    row.Children.Add(new TextBlock
                    {
                        Text = "  \u2014  ",
                        FontSize = 13,
                        Foreground = Brush("#657786"),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    row.Children.Add(new TextBlock
                    {
                        Text = $"{kvp.Value} fish",
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brush("#1E88E5"),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    ActiveReportPanel.Children.Add(row);
                }
            }
        }

        // **************************************************
        // Function: BuildDetailedReport
        // Description: Renders the Standard report plus per-video table, daily trend, and location/species cross-tab
        private void BuildDetailedReport(ReportStatistics stats, bool includeHeader = true)
        {
            // Start with the full standard layout
            BuildStandardReport(stats, includeHeader);

            // Ensure daily trend appears even if DetectionsByDate guard already fired
            if (stats.DetectionsByDate.Count > 0)
            {
                // Already added in BuildStandardReport; no duplicate needed.
            }

            // --- Per-video table ---
            AddSpacer(20);
            AddSectionTitle("Per-Video Breakdown");
            AddPerVideoTable();

            // --- Location \u00d7 species cross-tab (already included via AddLocationBreakdown in standard,
            //     but show a dedicated cross-tab grid here for clarity) ---
            if (stats.DetectionsByDateLocation.Count > 0 && stats.DetectionsByLocation.Count > 0)
            {
                AddSpacer(20);
                AddSectionTitle("Date \u00d7 Location Detections");
                AddDateLocationMatrix(stats);
            }
        }

        // **************************************************
        // Function: AddPerVideoTable
        // Description: Renders a scrollable table of per-row CSV data for Detailed Analysis mode
        private void AddPerVideoTable()
        {
            if (_lastFilteredLines.Length == 0) return;

            var outerScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var table = new Grid();
            string[] headers = { "Run", "Video File", "Location", "Species", "Direction", "Confidence", "Start (s)", "End (s)", "Timestamp" };
            foreach (var _ in headers)
                table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 100 });

            // Header row
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < headers.Length; c++)
            {
                var hdr = new Border
                {
                    Background = Brush("#0D3640"),
                    BorderBrush = Brush("#244F5A"),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Child = new TextBlock
                    {
                        Text = headers[c],
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 12,
                        Foreground = Brush("#FFFFFF"),
                        Margin = new Thickness(8, 5, 16, 5)
                    }
                };
                Grid.SetColumn(hdr, c);
                Grid.SetRow(hdr, 0);
                table.Children.Add(hdr);
            }

            int row = 1;
            foreach (var line in _lastFilteredLines)
            {
                string[] cols = line.Split(',');
                if (cols.Length < 8) continue;

                string video    = cols[0].Trim();
                string location = cols.Length > 1 ? cols[1].Trim() : "";
                string species  = cols.Length > 2 ? cols[2].Trim() : "";
                string conf     = cols.Length > 5 ? cols[5].Trim() : "";
                string dir      = cols.Length > 6 ? cols[6].Trim() : "";
                string start    = cols.Length > 7 ? cols[7].Trim() : "";
                string end      = cols.Length > 8 ? cols[8].Trim() : "";
                string ts       = cols.Length > 9 ? cols[9].Trim() : "";
                string run      = ExtractRunFromColumns(cols);

                // Detect no-fish rows (likely_class col 4 == "no_fish" or "not_fish")
                bool isNoFish = cols.Length > 4 && IsNoFishClass(cols[4].Trim());

                if (isNoFish)
                {
                    // Show the video and location, but blank the fish-specific fields
                    species = "No Fish";
                    conf    = "";
                    dir     = "";
                    start   = "";
                    end     = "";
                }
                else if (double.TryParse(conf, out double confVal))
                {
                    conf = $"{confVal * 100:F1}%";
                }

                string[] cells = { run, ShortenVideoPath(video), location, species, dir, conf, start, end, ts };
                var rowBgColor = row % 2 == 0
                    ? ReportColor("#F5F8FA")
                    : ResourceColor("ReportSurfaceAltBrush", Colors.White);

                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int c = 0; c < cells.Length; c++)
                {
                    // Grey-out text for no-fish rows so they're visually distinct
                    var fg = isNoFish ? Brush("#AAB8C2") : Brush("#14171A");
                    var cell = new Border
                    {
                        Background = new SolidColorBrush(rowBgColor),
                        BorderBrush = Brush("#E1E8ED"),
                        BorderThickness = new Thickness(0, 0, 1, 1),
                        Child = new TextBlock
                        {
                            Text = cells[c],
                            FontSize = 11,
                            Foreground = fg,
                            Margin = new Thickness(8, 3, 16, 3),
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            MaxWidth = c == 1 ? 220 : 160
                        }
                    };
                    Grid.SetColumn(cell, c);
                    Grid.SetRow(cell, row);
                    table.Children.Add(cell);
                }
                row++;
            }

            outerScroll.Content = table;
            ActiveReportPanel.Children.Add(outerScroll);
        }

        // **************************************************
        // Function: AddDateLocationMatrix
        // Description: Renders a Date x Location grid -- rows = dates, cols = locations, cells = fish count
        private void AddDateLocationMatrix(ReportStatistics stats)
        {
            var sortedDates     = stats.DetectionsByDateLocation.Keys.OrderBy(d => d).ToList();
            var sortedLocations = stats.DetectionsByLocation.Keys.OrderBy(l => l).ToList();

            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var table = new Grid();
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 110 });
            foreach (var _ in sortedLocations)
                table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 100 });

            // Header row
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var dateHdrCell = new Border { Background = Brush("#0D3640"),
                Child = new TextBlock { Text = "Date", FontWeight = FontWeights.SemiBold, FontSize = 12,
                    Foreground = Brush("#FFFFFF"), Margin = new Thickness(8, 6, 12, 6) } };
            Grid.SetColumn(dateHdrCell, 0); Grid.SetRow(dateHdrCell, 0);
            table.Children.Add(dateHdrCell);

            for (int c = 0; c < sortedLocations.Count; c++)
            {
                var locHdrCell = new Border { Background = Brush("#0D3640"),
                    Child = new TextBlock { Text = sortedLocations[c], FontWeight = FontWeights.SemiBold,
                        FontSize = 12, Foreground = Brush("#FFFFFF"), Margin = new Thickness(8, 6, 12, 6),
                        TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 160 } };
                Grid.SetColumn(locHdrCell, c + 1); Grid.SetRow(locHdrCell, 0);
                table.Children.Add(locHdrCell);
            }

            // Data rows
            for (int r = 0; r < sortedDates.Count; r++)
            {
                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var rowBg = r % 2 == 0
                    ? ReportColor("#F5F8FA")
                    : ResourceColor("ReportSurfaceAltBrush", Colors.White);

                var dataDate = new Border { Background = new SolidColorBrush(rowBg),
                    Child = new TextBlock { Text = sortedDates[r].ToString("yyyy-MM-dd"),
                        FontSize = 12, FontWeight = FontWeights.Medium,
                        Foreground = Brush("#14171A"), Margin = new Thickness(8, 4, 12, 4) } };
                Grid.SetColumn(dataDate, 0); Grid.SetRow(dataDate, r + 1);
                table.Children.Add(dataDate);

                for (int c = 0; c < sortedLocations.Count; c++)
                {
                    bool has = stats.DetectionsByDateLocation[sortedDates[r]]
                        .TryGetValue(sortedLocations[c], out int cnt);
                    var valCell = new Border { Background = new SolidColorBrush(rowBg),
                        Child = new TextBlock { Text = has ? cnt.ToString() : "\u2014",
                            FontSize = 12, Margin = new Thickness(8, 4, 12, 4),
                            TextAlignment = TextAlignment.Right,
                            Foreground = has ? Brush("#1E88E5") : Brush("#AAB8C2"),
                            FontWeight = has ? FontWeights.SemiBold : FontWeights.Normal } };
                    Grid.SetColumn(valCell, c + 1); Grid.SetRow(valCell, r + 1);
                    table.Children.Add(valCell);
                }
            }

            scroll.Content = table;
            ActiveReportPanel.Children.Add(scroll);
        }

        // **************************************************
        // Function: AddLocationSpeciesCrossTab
        // Description: Renders a location \u00d7 species cross-tab grid in Detailed Analysis mode
        private void AddLocationSpeciesCrossTab(ReportStatistics stats)
        {
            // Collect all unique species across all locations
            var allSpecies = stats.GroupedByLocation.Values
                .SelectMany(d => d.Keys)
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            var locations = stats.GroupedByLocation.Keys.OrderBy(l => l).ToList();

            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var table = new Grid();
            // Column 0 = location label, then one column per species
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 130 });
            foreach (var _ in allSpecies)
                table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 90 });

            // Header row
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var locHdr = new TextBlock { Text = "Location", FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(6, 4, 12, 4) };
            Grid.SetColumn(locHdr, 0); Grid.SetRow(locHdr, 0);
            table.Children.Add(locHdr);
            for (int c = 0; c < allSpecies.Count; c++)
            {
                var spHdr = new TextBlock { Text = allSpecies[c], FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(6, 4, 12, 4) };
                Grid.SetColumn(spHdr, c + 1); Grid.SetRow(spHdr, 0);
                table.Children.Add(spHdr);
            }

            for (int r = 0; r < locations.Count; r++)
            {
                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var rowBg = r % 2 == 0
                    ? ReportColor("#F5F8FA")
                    : ResourceColor("ReportSurfaceAltBrush", Colors.White);

                var locCell = new Border
                {
                    Background = new SolidColorBrush(rowBg),
                    Child = new TextBlock { Text = locations[r], FontSize = 12, Margin = new Thickness(6, 3, 12, 3) }
                };
                Grid.SetColumn(locCell, 0); Grid.SetRow(locCell, r + 1);
                table.Children.Add(locCell);

                for (int c = 0; c < allSpecies.Count; c++)
                {
                    int count = stats.GroupedByLocation[locations[r]].TryGetValue(allSpecies[c], out int v) ? v : 0;
                    var valCell = new Border
                    {
                        Background = new SolidColorBrush(rowBg),
                        Child = new TextBlock
                        {
                            Text = count > 0 ? count.ToString() : "—",
                            FontSize = 12,
                            Margin = new Thickness(6, 3, 12, 3),
                            Foreground = count > 0
                                ? Brush("#1E88E5")
                                : Brush("#AAB8C2")
                        }
                    };
                    Grid.SetColumn(valCell, c + 1); Grid.SetRow(valCell, r + 1);
                    table.Children.Add(valCell);
                }
            }

            scroll.Content = table;
            ActiveReportPanel.Children.Add(scroll);
        }

        // **************************************************
        // Function: BuildDataTableReport
        // Description: Renders a raw scrollable data grid of every filtered CSV row
        private void BuildDataTableReport(ReportStatistics stats, string[] lines, bool includeHeader = true)
        {
            if (includeHeader)
                AddReportHeader(stats.TotalDetections);
            AddSpacer(4);

            // Quick summary line
            var summaryParts = new List<string> { $"Fish: {stats.FishCount}", $"Upstream: {stats.UpstreamCount}", $"Downstream: {stats.DownstreamCount}" };
            if (stats.NoFishCount > 0)
                summaryParts.Add($"No Fish: {stats.NoFishCount}");
            ActiveReportPanel.Children.Add(new TextBlock
            {
                Text = string.Join("   |   ", summaryParts),
                FontSize = 12,
                Foreground = Brush("#657786"),
                Margin = new Thickness(0, 0, 0, 10)
            });

            if (lines.Length == 0)
            {
                ActiveReportPanel.Children.Add(new TextBlock
                {
                    Text = "No data rows to display.",
                    FontSize = 13,
                    Foreground = Brush("#657786"),
                    Margin = new Thickness(0, 10, 0, 0)
                });
                return;
            }

            // Outer scroll that allows horizontal scrolling for many columns
            var outerScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 600,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var table = new Grid();
            string[] headers = IsAllHistoryScope
                ? new[] { "#", "Run", "Video File", "Location", "Species", "Direction", "Confidence", "Start (s)", "End (s)", "Timestamp" }
                : new[] { "#", "Video File", "Location", "Species", "Direction", "Confidence", "Start (s)", "End (s)", "Timestamp" };
            foreach (var _ in headers)
                table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 80 });

            // Header row
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < headers.Length; c++)
            {
                var hdr = new Border
                {
                    Background = Brush("#0D3640"),
                    Child = new TextBlock
                    {
                        Text = headers[c],
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 12,
                        Foreground = Brush("#FFFFFF"),
                        Margin = new Thickness(8, 5, 16, 5)
                    }
                };
                Grid.SetColumn(hdr, c);
                Grid.SetRow(hdr, 0);
                table.Children.Add(hdr);
            }

            int rowIdx = 1;
            foreach (var line in lines)
            {
                string[] cols = line.Split(',');
                if (cols.Length < 8) continue;

                string video    = cols[0].Trim();
                string location = cols.Length > 1 ? cols[1].Trim() : "";
                string species  = cols.Length > 2 ? cols[2].Trim() : "";
                string conf     = cols.Length > 5 ? cols[5].Trim() : "";
                string dir      = cols.Length > 6 ? cols[6].Trim() : "";
                string start    = cols.Length > 7 ? cols[7].Trim() : "";
                string end      = cols.Length > 8 ? cols[8].Trim() : "";
                string ts       = cols.Length > 9 ? cols[9].Trim() : "";
                string run      = ExtractRunFromColumns(cols);

                if (double.TryParse(conf, out double confVal))
                    conf = $"{confVal * 100:F1}%";

                string[] cells = IsAllHistoryScope
                    ? new[] { rowIdx.ToString(), run, ShortenVideoPath(video), location, species, dir, conf, start, end, ts }
                    : new[] { rowIdx.ToString(), ShortenVideoPath(video), location, species, dir, conf, start, end, ts };
                var rowBg = rowIdx % 2 == 0
                    ? ReportColor("#F5F8FA")
                    : ResourceColor("ReportSurfaceAltBrush", Colors.White);

                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int c = 0; c < cells.Length; c++)
                {
                    var cell = new Border
                    {
                        Background = new SolidColorBrush(rowBg),
                        Child = new TextBlock
                        {
                            Text = cells[c],
                            FontSize = 11,
                            Foreground = Brush("#14171A"),
                            Margin = new Thickness(8, 3, 16, 3),
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            MaxWidth = 200
                        }
                    };
                    Grid.SetColumn(cell, c);
                    Grid.SetRow(cell, rowIdx);
                    table.Children.Add(cell);
                }
                rowIdx++;
            }

            outerScroll.Content = table;
            ActiveReportPanel.Children.Add(outerScroll);

            // --- Date × Location matrix ---
            if (stats.DetectionsByDateLocation.Count > 0 && stats.DetectionsByLocation.Count > 0)
            {
                AddSpacer(16);
                AddSectionTitle("Date \u00d7 Location Detections");
                AddDateLocationMatrix(stats);
            }
        }
        private void AddMainCountSection(ReportStatistics stats)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 25) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var mainCountCard = CreateStatCard("Total Fish Detected", stats.FishCount.ToString(), "#1E88E5", "");
            Grid.SetColumn(mainCountCard, 0);
            grid.Children.Add(mainCountCard);

            var fishPerDayCard = CreateStatCard("Fish Per Day", $"{stats.FishPerDay:F1}", "#43A047", "");
            Grid.SetColumn(fishPerDayCard, 1);
            grid.Children.Add(fishPerDayCard);

            var confCard = CreateStatCard("Avg Confidence", $"{100 * stats.AverageConfidence:F1}%", "#7E57C2", "");
            Grid.SetColumn(confCard, 2);
            grid.Children.Add(confCard);

            var noFishCard = CreateStatCard("Videos w/ No Fish",
                stats.NoFishCount > 0 ? stats.NoFishCount.ToString() : "—",
                "#657786", "");
            Grid.SetColumn(noFishCard, 3);
            grid.Children.Add(noFishCard);

            ActiveReportPanel.Children.Add(grid);
        }

        // **************************************************
        // Function: AddGroupBySection
        // Description: Displays data grouped by current selection (species/datetime/location)
        private void AddGroupBySection(ReportStatistics stats)
        {
            AddSectionTitle($"\U0001F41F Grouped by {_currentGroupBy.ToUpper()}");

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
                Background = Brush("#FFF9E6"),
                BorderBrush = Brush("#FFE082"),
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
                Foreground = Brush("#F57C00"),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var valueGrid = new Grid();
            valueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            valueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var valueText = new TextBlock
            {
                Text = $"\u2248 {stats.EstimatedUpstreamCount:F0} fish",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("#E65100")
            };
            Grid.SetColumn(valueText, 0);

            valueGrid.Children.Add(valueText);

            var detectedText = new TextBlock
            {
                Text = $"Detected: {stats.UpstreamCount} upstream - {stats.DownstreamCount} downstream",
                FontSize = 13,
                Foreground = Brush("#8B6914"),
                Margin = new Thickness(0, 8, 0, 0)
            };

            panel.Children.Add(title);
            panel.Children.Add(valueGrid);
            panel.Children.Add(detectedText);
            estimationPanel.Child = panel;

            ActiveReportPanel.Children.Add(estimationPanel);
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

            ActiveReportPanel.Children.Add(grid);

            // Add confidence distribution
            AddConfidenceDistribution(stats);
        }

        // **************************************************
        // Function: AddConfidenceDistribution
        // Description: Shows distribution of detections by confidence level
        private void AddConfidenceDistribution(ReportStatistics stats)
        {
            int lowConf = stats.TotalDetections - stats.HighConfidenceCount;
            if (lowConf < 0) lowConf = 0;
            AddBarChart("High Confidence (\u226580%)", stats.HighConfidenceCount, stats.TotalDetections, "#43A047");
            AddBarChart("Lower Confidence (<80%)",    lowConf,                   stats.TotalDetections, "#FB8C00");
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
                Background = Brush("#F5F8FA")
            };

            foreach (var species in speciesData.OrderByDescending(x => x.Value))
            {
                var speciesText = new TextBlock
                {
                    Text = $"  \u21b3 {species.Key}: {species.Value}",
                    FontSize = 12,
                    Foreground = Brush("#657786"),
                    Margin = new Thickness(0, 2, 0, 2)
                };
                breakdownPanel.Children.Add(speciesText);
            }

            ActiveReportPanel.Children.Add(breakdownPanel);
        }

        // **************************************************
        // Function: AddDailyTrendGraph
        // Description: Creates a visual trend graph showing detections over time
        private void AddDailyTrendGraph(ReportStatistics stats)
        {
            if (stats.DetectionsByDate.Count == 0) return;

            var sortedDates = stats.DetectionsByDate.OrderBy(x => x.Key).ToList();
            var points = sortedDates.Select(kvp => (kvp.Key.ToString("MMM dd"), kvp.Value)).ToList();

            var chart = CreateLineChart(points, "#7E57C2", 160, ActiveReportPanel?.ActualWidth ?? 0);
            ActiveReportPanel.Children.Add(chart);
        }

        // **************************************************
        // Function: GenerateReportText
        // Description: Creates a formatted text report matching the currently selected report style
        private string GenerateReportText(ReportStatistics stats)
        {
            var sb = new StringBuilder();
            AppendReportHeader(sb, stats, _currentReportStyle);

            if (IsAllHistoryScope && _lastGroupedLinesByRun.Count > 1)
            {
                sb.AppendLine("RUNS INCLUDED");
                foreach (var runGroup in _lastGroupedLinesByRun)
                {
                    var runStats = CalculateStatistics(runGroup.Value);
                    string rangeText = runStats.MinDetectionTimestamp.HasValue && runStats.MaxDetectionTimestamp.HasValue
                        ? $"{runStats.MinDetectionTimestamp.Value:yyyy-MM-dd} -> {runStats.MaxDetectionTimestamp.Value:yyyy-MM-dd}"
                        : "No date range";
                    sb.AppendLine($"  {runGroup.Key}: Fish={runStats.FishCount}, Videos={(runStats.TotalVideoCount > 0 ? runStats.TotalVideoCount : runStats.VideoDetections.Count)}, Range={rangeText}");
                }
                sb.AppendLine();
            }

            switch (_currentReportStyle)
            {
                case "Summary Only":
                    AppendTextSummaryOnly(sb, stats);
                    break;
                case "Detailed Analysis":
                    AppendTextStandard(sb, stats);
                    AppendTextDataTable(sb);
                    break;
                case "Data Table View":
                    AppendTextDataTable(sb);
                    break;
                default: // Standard Report
                    AppendTextStandard(sb, stats);
                    break;
            }

            AppendReportFooter(sb);
            return sb.ToString();
        }

        // **************************************************
        // Function: AppendTextSummaryOnly
        // Description: Text equivalent of BuildSummaryOnlyReport
        private void AppendTextSummaryOnly(StringBuilder sb, ReportStatistics stats)
        {
            int indecisive = stats.TotalDetections - stats.UpstreamCount - stats.DownstreamCount;
            if (indecisive < 0) indecisive = 0;
            int net        = stats.UpstreamCount - stats.DownstreamCount;
            int totalVids  = stats.TotalVideoCount > 0 ? stats.TotalVideoCount : stats.VideoDetections.Count;

            sb.AppendLine("KEY STATISTICS");
            sb.AppendLine($"  Total Fish Detected : {stats.FishCount}");
            sb.AppendLine($"  Total Videos        : {totalVids}");
            sb.AppendLine($"  Net Upstream        : {net}");
            sb.AppendLine($"  Upstream            : {stats.UpstreamCount}");
            sb.AppendLine($"  Downstream          : {stats.DownstreamCount}");
            sb.AppendLine($"  Indecisive          : {indecisive}");
            if (stats.NoFishCount > 0)
                sb.AppendLine($"  Videos w/ No Fish   : {stats.NoFishCount}");
            sb.AppendLine();

            if (stats.MinDetectionTimestamp.HasValue && stats.MaxDetectionTimestamp.HasValue)
            {
                sb.AppendLine("DATE RANGE");
                sb.AppendLine($"  {stats.MinDetectionTimestamp.Value:yyyy-MM-dd}  →  {stats.MaxDetectionTimestamp.Value:yyyy-MM-dd}");
                sb.AppendLine();
            }

            if (stats.DetectionsByLocation.Count > 0)
            {
                sb.AppendLine("LOCATION SUMMARY");
                foreach (var kvp in stats.DetectionsByLocation.OrderByDescending(x => x.Value))
                    sb.AppendLine($"  {kvp.Key}  —  {kvp.Value} fish");
                sb.AppendLine();
            }
        }

        // **************************************************
        // Function: AppendTextStandard
        // Description: Text equivalent of BuildStandardReport
        private void AppendTextStandard(StringBuilder sb, ReportStatistics stats)
        {
            sb.AppendLine("KEY METRICS");
            sb.AppendLine($"  Total Fish Detected : {stats.FishCount}");
            sb.AppendLine($"  Fish Per Day        : {stats.FishPerDay:F1}");
            sb.AppendLine($"  Avg Confidence      : {100 * stats.AverageConfidence:F1}%");
            if (stats.NoFishCount > 0)
                sb.AppendLine($"  Videos w/ No Fish   : {stats.NoFishCount}");
            sb.AppendLine();

            sb.AppendLine($"GROUPED BY {_currentGroupBy.ToUpper()}");
            switch (_currentGroupBy.ToLower())
            {
                case "species":
                    foreach (var kvp in stats.SpeciesBreakdown.OrderByDescending(x => x.Value))
                        sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
                    break;
                case "datetime":
                    foreach (var kvp in stats.DetectionsByDate.OrderBy(x => x.Key))
                        sb.AppendLine($"  {kvp.Key:MMM dd, yyyy}: {kvp.Value}");
                    break;
                case "location":
                    foreach (var kvp in stats.DetectionsByLocation.OrderByDescending(x => x.Value))
                        sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
                    break;
            }
            sb.AppendLine();

            sb.AppendLine("MOVEMENT PATTERNS");
            sb.AppendLine($"  Upstream            : {stats.UpstreamCount} ({CalculatePercentage(stats.UpstreamCount, stats.TotalDetections):F1}%)");
            sb.AppendLine($"  Downstream          : {stats.DownstreamCount} ({CalculatePercentage(stats.DownstreamCount, stats.TotalDetections):F1}%)");
            sb.AppendLine($"  Est. Upstream Total : ~{stats.EstimatedUpstreamCount:F0} fish");
            sb.AppendLine();

            sb.AppendLine("CONFIDENCE DISTRIBUTION");
            int lowConf = stats.TotalDetections - stats.HighConfidenceCount;
            sb.AppendLine($"  High (=80%) : {stats.HighConfidenceCount} ({CalculatePercentage(stats.HighConfidenceCount, stats.TotalDetections):F1}%)");
            sb.AppendLine($"  Lower (<80%): {lowConf} ({CalculatePercentage(lowConf, stats.TotalDetections):F1}%)");
            sb.AppendLine();

            if (stats.DetectionsByLocation.Count > 0)
            {
                sb.AppendLine("LOCATION ANALYSIS");
                foreach (var kvp in stats.DetectionsByLocation.OrderByDescending(x => x.Value))
                {
                    sb.AppendLine($"  {kvp.Key}: {kvp.Value} ({CalculatePercentage(kvp.Value, stats.TotalDetections):F1}%)");
                    if (stats.GroupedByLocation.ContainsKey(kvp.Key))
                        foreach (var sp in stats.GroupedByLocation[kvp.Key].OrderByDescending(x => x.Value))
                            sb.AppendLine($"      +- {sp.Key}: {sp.Value}");
                }
                sb.AppendLine();
            }

            if (stats.VideoDetections.Count > 0)
            {
                sb.AppendLine("TOP VIDEOS");
                foreach (var kvp in stats.VideoDetections.OrderByDescending(x => x.Value).Take(10))
                    sb.AppendLine($"  {ShortenVideoPath(kvp.Key)}: {kvp.Value} detections");
                sb.AppendLine();
            }
        }

        // **************************************************
        // Function: AppendTextDataTable
        // Description: Text equivalent of BuildDataTableReport with fixed-width columns aligned to content
        private void AppendTextDataTable(StringBuilder sb)
        {
            if (_lastFilteredLines == null || _lastFilteredLines.Length == 0) return;

            // Parse all rows first so we can measure column widths
            string[] hdr = IsAllHistoryScope
                ? new[] { "#", "Run", "Video", "Location", "Species", "Direction", "Confidence", "Start(s)", "End(s)", "Timestamp" }
                : new[] { "#", "Video", "Location", "Species", "Direction", "Confidence", "Start(s)", "End(s)", "Timestamp" };
            var rows = new List<string[]>();
            int rowNum = 1;
            foreach (string line in _lastFilteredLines)
            {
                string[] cols = line.Split(',');
                string video    = cols.Length > 0 ? ShortenVideoPath(cols[0].Trim()) : "";
                string location = cols.Length > 1 ? cols[1].Trim() : "";
                string species  = cols.Length > 2 ? cols[2].Trim() : "";
                string dir      = cols.Length > 6 ? cols[6].Trim() : "";
                string conf     = cols.Length > 5 ? cols[5].Trim() : "";
                string start    = cols.Length > 7 ? cols[7].Trim() : "";
                string end      = cols.Length > 8 ? cols[8].Trim() : "";
                string ts       = cols.Length > 9 ? cols[9].Trim() : "";
                string run      = ExtractRunFromColumns(cols);
                if (double.TryParse(conf, out double confVal)) conf = $"{confVal * 100:F1}%";
                rows.Add(IsAllHistoryScope
                    ? new[] { rowNum.ToString(), run, video, location, species, dir, conf, start, end, ts }
                    : new[] { rowNum.ToString(), video, location, species, dir, conf, start, end, ts });
                rowNum++;
            }

            // Compute max width per column
            int[] widths = new int[hdr.Length];
            for (int c = 0; c < hdr.Length; c++) widths[c] = hdr[c].Length;
            foreach (var r in rows)
                for (int c = 0; c < r.Length; c++)
                    if (r[c].Length > widths[c]) widths[c] = r[c].Length;

            // Build format string: each col padded to its max width, 2 spaces between
            string Fmt(string[] cells) =>
                string.Join("  ", cells.Select((cell, c) => cell.PadRight(widths[c])));

            string separator = string.Join("  ", widths.Select(w => new string('-', w)));

            sb.AppendLine("DATA TABLE");
            sb.AppendLine(Fmt(hdr));
            sb.AppendLine(separator);
            foreach (var r in rows) sb.AppendLine(Fmt(r));
            sb.AppendLine();

            // --- Date × Location matrix ---
            if (_lastStats != null && _lastStats.DetectionsByDateLocation?.Count > 0)
            {
                var sortedDates     = _lastStats.DetectionsByDateLocation.Keys.OrderBy(d => d).ToList();
                var sortedLocations = _lastStats.DetectionsByLocation.Keys.OrderBy(l => l).ToList();

                // Build header
                var matHdr = new[] { "Date" }.Concat(sortedLocations).ToArray();
                int[] mw = matHdr.Select(h => h.Length).ToArray();
                // Measure data widths
                foreach (var date in sortedDates)
                {
                    string dateStr = date.ToString("yyyy-MM-dd");
                    if (dateStr.Length > mw[0]) mw[0] = dateStr.Length;
                    for (int c = 0; c < sortedLocations.Count; c++)
                    {
                        bool has = _lastStats.DetectionsByDateLocation[date].TryGetValue(sortedLocations[c], out int cnt);
                        int len = has ? cnt.ToString().Length : 1;
                        if (len > mw[c + 1]) mw[c + 1] = len;
                    }
                }
                string MatFmt(string[] cells) => string.Join("  ", cells.Select((cell, c) => cell.PadRight(mw[c])));
                string matSep = string.Join("  ", mw.Select(w => new string('-', w)));

                sb.AppendLine("DATE \u00d7 LOCATION DETECTIONS");
                sb.AppendLine(MatFmt(matHdr));
                sb.AppendLine(matSep);
                foreach (var date in sortedDates)
                {
                    var cells = new[] { date.ToString("yyyy-MM-dd") }
                        .Concat(sortedLocations.Select(loc =>
                            _lastStats.DetectionsByDateLocation[date].TryGetValue(loc, out int cnt)
                            ? cnt.ToString() : "\u2014"))
                        .ToArray();
                    sb.AppendLine(MatFmt(cells));
                }
                sb.AppendLine();
            }
        }

        // **************************************************
        // Function: AppendEnhancedMetrics (kept for legacy compatibility, unused by new text generator)
        private void AppendEnhancedMetrics(StringBuilder sb, ReportStatistics stats) { }

        // **************************************************
        // Function: AppendAIPerformanceMetrics (kept for legacy compatibility)
        private void AppendAIPerformanceMetrics(StringBuilder sb, ReportStatistics stats) { }

        // **************************************************
        // Function: AppendLocationStatistics (kept for legacy compatibility)
        private void AppendLocationStatistics(StringBuilder sb, ReportStatistics stats) { }

        #endregion

        // **************************************************
        // Class: ReportVisualPaginator
        // Description: Renders the full report StackPanel to a RenderTargetBitmap (unaffected by
        //   ScrollViewer clip/scroll position), then paginates by cropping bitmap slices.
        //   Page breaks are placed at child-element boundaries so sections are never split mid-element.
        //   When a page break falls inside a table section, the last seen "col_header"-tagged element
        //   is repeated at the top of the continuation page; otherwise the last "section_header" title
        //   is repeated so every page is self-identifying.
        private sealed class ReportVisualPaginator : DocumentPaginator
        {
            private const double Margin = 40.0; // Slightly larger report render while keeping print margins.
            private const double RenderScale = 2.0;

            private readonly Size           _page;
            private readonly BitmapSource   _bitmap;    // full-height render of the report panel
            private readonly double         _sourceW;   // panel WPF width
            private readonly double         _xScale;    // source-units ? print-units (horizontal)
            private readonly List<Slice>    _slices;

            private struct Slice
            {
                public double SrcY;   // top of content in source (bitmap) coordinates
                public double SrcH;   // height of content in source coordinates
                public double RepY;   // top of repeated header in source coords (-1 = none)
                public double RepH;   // height of repeated header in source coords
            }

            public ReportVisualPaginator(StackPanel panel, Size page)
            {
                _page    = page;
                double cw = page.Width  - Margin * 2;
                double ch = page.Height - Margin * 2;

                _sourceW        = Math.Max(1, panel.ActualWidth);
                double sourceH  = Math.Max(1, panel.DesiredSize.Height > panel.ActualHeight
                                            ? panel.DesiredSize.Height
                                            : panel.ActualHeight);

                // Render the full panel to a bitmap. RenderTargetBitmap is not clipped by the
                // parent ScrollViewer, so the entire report content is captured regardless of
                // the current scroll position shown on screen.
                var rtb = new RenderTargetBitmap(
                    (int)Math.Ceiling(_sourceW * RenderScale), (int)Math.Ceiling(sourceH * RenderScale),
                    96 * RenderScale, 96 * RenderScale, PixelFormats.Pbgra32);
                // Paint white background first so transparent regions print as white
                var bg = new DrawingVisual();
                using (var ctx = bg.RenderOpen())
                    ctx.DrawRectangle(ResourceBrush("ReportPrintPageBackgroundBrush", Brushes.White), null, new Rect(0, 0, _sourceW, sourceH));
                rtb.Render(bg);
                rtb.Render(panel);
                _bitmap = rtb;

                _xScale = cw / _sourceW;
                double srcPerPage = ch / _xScale;   // source pixels shown per page

                // Collect every direct child's Y position and Tag
                var kids = new List<(double y, double h, string tag)>();
                foreach (FrameworkElement child in panel.Children)
                {
                    var pt = child.TranslatePoint(new Point(0, 0), panel);
                    kids.Add((pt.Y, child.ActualHeight, child.Tag?.ToString() ?? ""));
                }

                // Build slices: find page break points at child boundaries so no child is split.
                _slices          = new List<Slice>();
                double pageTop   = 0;
                double lastSecY  = -1, lastSecH = 0;   // latest section_header seen
                double lastColY  = -1, lastColH = 0;   // latest col_header seen

                while (pageTop < sourceH - 0.5)
                {
                    double naturalEnd = pageTop + srcPerPage;
                    double breakPt    = Math.Min(naturalEnd, sourceH);
                    bool splitCurrentSection = false;
                    double splitChildY = -1;

                    if (naturalEnd < sourceH)
                    {
                        // Walk children backwards to find one that straddles naturalEnd.
                        // Move the page break to that child's top so it starts fresh on the next page.
                        for (int i = kids.Count - 1; i >= 0; i--)
                        {
                            var (cy, ch2, _) = kids[i];
                            if (cy >= naturalEnd) continue;           // fully on next page
                            if (cy + ch2 <= naturalEnd) break;        // fully on this page — done
                            // Straddles: move break up ONLY if the child doesn't start near the very top
                            // of this page (otherwise, single oversized elements would create empty pages)
                            splitCurrentSection = true;
                            splitChildY = cy;
                            if (cy - pageTop > srcPerPage * 0.10)
                                breakPt = cy;
                            break;
                        }

                        // Prevent orphaned section headers: if the last element fully on this page
                        // is a section_header (graph title), push it to the next page so the title
                        // always stays together with the chart that follows it.
                        for (int i = kids.Count - 1; i >= 0; i--)
                        {
                            var (cy, ch2, tag) = kids[i];
                            if (cy + ch2 > breakPt) continue; // not fully on this page
                            if (cy < pageTop) break;           // before this page
                            // Only push the header if it has room above (avoid infinite empty pages)
                            if (tag == "section_header" && cy - pageTop > srcPerPage * 0.05)
                                breakPt = cy;
                            break;
                        }
                    }

                    // Repeated header for continuation pages: repeat the section_header only when
                    // the next page continues the same section (not when it opens a fresh one).
                    double repY = -1, repH = 0;
                    if (_slices.Count > 0)
                    {
                        var firstNext = kids.Where(k => k.y >= breakPt).OrderBy(k => k.y).FirstOrDefault();
                        bool nextOpensWithSection = firstNext.tag == "section_header";
                        // Only repeat section_header when continuing — never repeat col_header
                        // (col_header is a small label; repeating it caused it to leak onto wrong pages)
                        bool continuingSameSection = splitCurrentSection && lastSecY >= 0 && splitChildY > lastSecY;
                        if (!nextOpensWithSection && continuingSameSection)
                            { repY = lastSecY; repH = lastSecH; }
                    }

                    _slices.Add(new Slice { SrcY = pageTop, SrcH = breakPt - pageTop, RepY = repY, RepH = repH });

                    // Advance tracked headers using elements that appeared on this slice
                    foreach (var (cy, ch2, tag) in kids)
                    {
                        if (cy < pageTop || cy >= breakPt) continue;
                        if (tag == "section_header") { lastSecY = cy; lastSecH = ch2; lastColY = -1; } // new section resets col header
                        if (tag == "col_header")     { lastColY = cy; lastColH = ch2; }
                    }

                    pageTop = breakPt;
                }

                if (_slices.Count == 0)
                    _slices.Add(new Slice { SrcY = 0, SrcH = sourceH, RepY = -1, RepH = 0 });
            }

            public override DocumentPage GetPage(int pageNumber)
            {
                var sl   = _slices[pageNumber];
                double cw = _page.Width  - Margin * 2;
                bool hasRep = sl.RepY >= 0 && sl.RepH > 0;

                var dv = new DrawingVisual();
                using (var ctx = dv.RenderOpen())
                {
                    ctx.DrawRectangle(ResourceBrush("ReportPrintPageBackgroundBrush", Brushes.White), null, new Rect(_page));

                    double drawY = Margin;

                    // Repeated header (col or section) at the top of continuation pages
                    if (hasRep)
                    {
                        var repCrop = Crop(sl.RepY, sl.RepH);
                        if (repCrop != null)
                        {
                            double repPrintH = sl.RepH * _xScale;
                            ctx.DrawImage(repCrop, new Rect(Margin, drawY, cw, repPrintH));
                            drawY += repPrintH + 6;
                        }
                    }

                    // Content slice; scale to fill remaining space between drawY and bottom margin.
                    var contentCrop = Crop(sl.SrcY, sl.SrcH);
                    if (contentCrop != null)
                    {
                        double available = _page.Height - Margin - drawY;
                        double printH    = Math.Min(sl.SrcH * _xScale, available);
                        ctx.DrawImage(contentCrop, new Rect(Margin, drawY, cw, printH));
                    }
                }
                return new DocumentPage(dv, _page, new Rect(_page), new Rect(_page));
            }

            // Safely crop a horizontal band [srcY, srcY+srcH) from the full bitmap
            private BitmapSource Crop(double srcY, double srcH)
            {
                int bw = _bitmap.PixelWidth;
                int bh = _bitmap.PixelHeight;
                int y  = Math.Max(0, (int)Math.Floor(srcY * RenderScale));
                int h  = Math.Min((int)Math.Ceiling(srcH * RenderScale), bh - y);
                if (y >= bh || h <= 0) return null;
                return new CroppedBitmap(_bitmap, new Int32Rect(0, y, bw, h));
            }

            public override bool IsPageCountValid           => true;
            public override int  PageCount                  => _slices.Count;
            public override Size PageSize                   { get => _page; set { } }
            public override IDocumentPaginatorSource Source => null;
        }
    }
}

