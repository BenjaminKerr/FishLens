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
                if (ShowReportExportedMessage(reportPath))
                    OpenReportFile(reportPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting report");
                ShowErrorMessage("Unable to export report. Please check the logs for details.", "Error Exporting Report");
            }
        }

        // **************************************************
        // Function: ConfidenceSlider_ValueChanged
        // Description: Updates confidence filter threshold as slider moves
        public void ConfidenceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (confidenceSlider == null || confidenceValueText == null) return;

            _filterMinConfidence = confidenceSlider.Value / 100.0;
            confidenceValueText.Text = $"{confidenceSlider.Value:F0}%";
        }

        // **************************************************
        // Function: ApplyFiltersClick
        public void ApplyFiltersClick(object sender, RoutedEventArgs e)
        {
            UpdateFiltersFromUI();
            GenerateReport();
        }

        // **************************************************
        // Function: ClearFiltersClick
        public void ClearFiltersClick(object sender, RoutedEventArgs e)
        {
            _filterStartDate = null;
            _filterEndDate = null;
            _filterSpecies = "All";
            _filterDirection = "All";
            _filterCamera = "All";
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
        public void RefreshReportClick(object sender, RoutedEventArgs e)
        {
            GenerateReport();
        }

        // **************************************************
        // Function: AllDatesClick
        // Description: Clears date filters and prompts user to re-apply
        public void AllDatesClick(object sender, RoutedEventArgs e)
        {
            _filterStartDate = null;
            _filterEndDate = null;

            if (startDatePicker != null) startDatePicker.SelectedDate = null;
            if (endDatePicker != null) endDatePicker.SelectedDate = null;

            MessageBox.Show("Date filters cleared. Click 'Apply Filters' to update the report.",
                            "Filters Updated", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region View Configuration

        // **************************************************
        // Function: ConfigureReportView
        private void ConfigureReportView()
        {
            placeholderPanel.Visibility = Visibility.Collapsed;
            videoPlayer.Visibility = Visibility.Collapsed;
            reportScrollViewer.Visibility = Visibility.Visible;
            viewTitle.Text = "Analysis Report";
        }

        // **************************************************
        // Function: ClearPreviousReport
        private void ClearPreviousReport()
        {
            reportPanel.Children.Clear();
        }

        // **************************************************
        // Function: ShowEmptyState
        public void ShowEmptyState()
        {
            reportPanel.Children.Add(CreateEmptyStatePanel());
        }

        #endregion

        #region Message Boxes

        private void ShowNoDataMessage() =>
            MessageBox.Show("No data available to generate a report.", "No Data", MessageBoxButton.OK, MessageBoxImage.Information);

        private void ShowNoMatchingDataMessage() =>
            MessageBox.Show("No data matches the current filters.", "No Matching Data", MessageBoxButton.OK, MessageBoxImage.Information);

        private void ShowNoReportMessage() =>
            MessageBox.Show("No report to export.", "No Report", MessageBoxButton.OK, MessageBoxImage.Information);

        private bool ShowReportExportedMessage(string reportPath)
        {
            var result = MessageBox.Show(
                $"Report exported successfully!\n\nLocation: {reportPath}\n\nWould you like to open it now?",
                "Report Exported", MessageBoxButton.YesNo, MessageBoxImage.Information);
            return result == MessageBoxResult.Yes;
        }

        private void ShowErrorMessage(string message, string title) =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

        #endregion

        #region File Operations

        // **************************************************
        // Function: CreateReportFile
        private string CreateReportFile()
        {
            string csvPath = _pathResolver.ResolveCsvScriptPath();
            string reportsDir = Path.Combine(Path.GetDirectoryName(csvPath), "Reports");
            Directory.CreateDirectory(reportsDir);

            string reportPath = Path.Combine(reportsDir, $"FishLens_Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(reportPath, _currentReportText);
            return reportPath;
        }

        // **************************************************
        // Function: OpenReportFile
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