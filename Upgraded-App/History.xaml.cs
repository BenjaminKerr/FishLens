using FishLens_App.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Text;
using System.Linq;
using FishLens_App.Models;

namespace FishLens_App
{
    public partial class History : Page // TODO: Rename to Reports.xaml.cs
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
        private string _filterCamera = "All";
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
            GenerateReport();
        }

        #endregion

        #region Button Event Handlers

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

        // **************************************************
        // Function: ConfidenceSlider_ValueChanged
        // Description: Updates confidence filter and display when slider changes
        public void ConfidenceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (confidenceSlider == null || confidenceValueText == null)
                return;

            _filterMinConfidence = confidenceSlider.Value / 100.0;
            confidenceValueText.Text = $"{confidenceSlider.Value:F0}%";
        }

        // **************************************************
        // Function: ApplyFiltersClick
        // Description: Applies all selected filters and regenerates the report
        public void ApplyFiltersClick(object sender, RoutedEventArgs e)
        {
            UpdateFiltersFromUI();
            GenerateReport();
        }

        // **************************************************
        // Function: ClearFiltersClick
        // Description: Resets all filters to default values
        public void ClearFiltersClick(object sender, RoutedEventArgs e)
        {
            _filterStartDate = null;
            _filterEndDate = null;
            _filterSpecies = "All";
            _filterDirection = "All";
            _filterMinConfidence = 0.0;

            if (startDatePicker != null) startDatePicker.SelectedDate = null;
            if (endDatePicker != null) endDatePicker.SelectedDate = null;
            if (speciesFilter != null) speciesFilter.SelectedIndex = 0;
            if (directionFilter != null) directionFilter.SelectedIndex = 0;
            if (cameraFilter != null) cameraFilter.SelectedIndex = 0;
            if (confidenceSlider != null) confidenceSlider.Value = 0;

            GenerateReport();
        }

        // **************************************************
        // Function: RefreshReportClick
        // Description: Refreshes the current report with latest data
        public void RefreshReportClick(object sender, RoutedEventArgs e)
        {
            GenerateReport();
        }

        // **************************************************
        // Function: AllDatesClick
        // Description: Clears date filters to show all dates
        public void AllDatesClick(object sender, RoutedEventArgs e)
        {
            _filterStartDate = null;
            _filterEndDate = null;

            if (startDatePicker != null) startDatePicker.SelectedDate = null;
            if (endDatePicker != null) endDatePicker.SelectedDate = null;

            MessageBox.Show("Date filters cleared. Click 'Apply Filters' to update the report.",
                            "Filters Updated",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
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
            reportScrollViewer.Visibility = Visibility.Visible;
            viewTitle.Text = "Analysis Report";
        }

        // **************************************************
        // Function: ClearPreviousReport
        // Description: Clears all children from the report panel
        private void ClearPreviousReport()
        {
            reportPanel.Children.Clear();
        }

        // **************************************************
        // Function: ShowEmptyState
        // Description: Displays an empty state message when no history is available
        public void ShowEmptyState()
        {
            var emptyPanel = CreateEmptyStatePanel();
            reportPanel.Children.Add(emptyPanel);
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

        #endregion

        #region Helper Methods - File Operations

        // **************************************************
        // Function: CreateReportFile
        // Description: Creates a new report file and returns its path
        private string CreateReportFile()
        {
            string csvPath = _pathResolver.ResolveCsvScriptPath();
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
    }
}