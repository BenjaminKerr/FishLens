// **************************************************
// ***********************************
// File: MainWindow.xaml.cs
// Description: Handles the analysis page's functionality
// Author: Benjamin Kerr
// 2025 - 2026
// ***********************************
// **************************************************

using FishLens_App.Helper_Classes;
using FishLens_App.Interfaces;
using FishLens_App.Models;
using FishLens_App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FishLens_App
{
    public partial class MainWindow : Window
    {
        #region Constants

        // Application settings
        private const double DEFAULT_CONFIDENCE_THRESHOLD = 0.7;

        // UI constants - Video buttons
        private const int BUTTON_HEIGHT = 45;
        private const int BUTTON_FONT_SIZE = 13;
        private const int BUTTON_MARGIN = 5;
        private const int BUTTON_PADDING_HORIZONTAL = 12;
        private const int BUTTON_PADDING_VERTICAL = 8;
        private const int BUTTON_CORNER_RADIUS = 6;
        private const int CONTENT_PRESENTER_MARGIN = 8;

        // Directory paths
        private const string SAVED_VIDEOS_FOLDER = "SavedVids";
        private const string SAMPLE_DATA_FOLDER = "sample_data";
        private const string TRASH_FOLDER = ".trash";

        // State
        private string _currentFolderName = string.Empty;

        #endregion

        #region Fields

        private readonly IProjectPathResolver _pathResolver;
        private readonly IFileSystemManager _fileSystemManager;
        private readonly ILogger<MainWindow> _logger;
        private readonly AppConfiguration _config;
        private readonly CheckBoxToggle _checkBoxes;
        // Stack of deletion batches for undo support
        private readonly Stack<DeletionBatch> _deletionHistory = new Stack<DeletionBatch>();
        private static readonly Color COLOR_HIGH = Color.FromRgb(0x5D, 0xCA, 0xA5);
        private static readonly Color COLOR_MID = Color.FromRgb(0xEF, 0x9F, 0x27);
        private static readonly Color COLOR_LOW = Color.FromRgb(0xE2, 0x4B, 0x4A);

        // Persistent Python process - models stay loaded between runs
        private Process _yoloProcess;
        private bool _yoloFastModeAtStart;
        private string _yoloLocationAtStart = string.Empty;
        private string _yoloRunAtStart = string.Empty;
        private TaskCompletionSource<bool> _processingTcs;
        private TaskCompletionSource<bool> _yoloReadyTcs;
        // Incremented before every intentional kill-for-restart. ReadYoloOutputLoop snapshots
        // this at start and only fires _processingTcs.TrySetException on exit when the count
        // hasn't changed - i.e. the process died unexpectedly, not because we restarted it.
        private int _yoloKillCount;
        private int _totalVideos;
        private readonly System.Text.StringBuilder _errorBuilder = new System.Text.StringBuilder();
        private string _currentVideoStatus = string.Empty;

        // Video player state
        private DispatcherTimer _videoTimer;
        private bool _isDraggingScrubber = false;
        private bool _isPlaying = false;
        private int _suppressTimerTicks = 0;
        private string _playbackTempPath = null; // temp MP4 created from ASF for accurate scrubbing
        private bool _processingComplete = false;

        // Sidebar state
        private bool _sidebarCollapsed = false;

        // Multi-track state - all tracks for the currently displayed video
        private List<FishLens_App.Models.Video> _currentTracks = new List<FishLens_App.Models.Video>();
        private int _currentTrackIndex;

        #endregion

        #region Nested Classes

        // **************************************************
        // Type: DeletionBatch
        // Description: Holds UI-only state for a batch of hidden videos so the list entry can be restored.
        //              No files are moved or deleted; no CSV rows are changed.
        // **************************************************
        private class DeletionBatch
        {
            public List<(string originalPath, Grid grid, FishLens_App.Models.Video video, string folder)> Items { get; } = new List<(string, Grid, FishLens_App.Models.Video, string)>();
            // Preserves the full header display text keyed by section key so undo can restore it.
            public Dictionary<string, string> FolderHeaderTexts { get; } = new Dictionary<string, string>();
        }

        private class LibrarySectionContext
        {
            public string SectionKey { get; set; } = string.Empty;
            public string FolderName { get; set; } = string.Empty;
            public string FolderPath { get; set; } = string.Empty;
            public string Run { get; set; } = string.Empty;
        }

        #endregion

        #region Constructors

        // **************************************************
        // Function: Constructor (Parameterized)
        // Description: Initializes MainWindow with dependency injection
        // **************************************************
        public MainWindow(IProjectPathResolver pathResolver, IFileSystemManager fileSystemManager, ILogger<MainWindow> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _fileSystemManager = fileSystemManager ?? throw new ArgumentNullException(nameof(fileSystemManager));
            var app = Application.Current as App;
          


            InitializeComponent();
            app.ApplyCurrentSettings();
            _checkBoxes = GetCheckBoxToggleFromApplication();
            _config = GetConfigurationFromApplication();
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;

            AccountSettingsButton.Visibility = app.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
            if (!app.IsAdmin)
                ButtonGrid.ColumnDefinitions[3].Width = new GridLength(0);
        }

        // **************************************************
        // Function: Constructor (Default)
        // Description: Initializes MainWindow with default dependencies
        // **************************************************
        public MainWindow() : this(
            GetDefaultProjectPathResolver(),
            GetDefaultFileSystemManager(),
            NullLogger<MainWindow>.Instance)
        {
        }

        #endregion

        #region Dependency Creation Helpers

        // **************************************************
        // Function: GetDefaultProjectPathResolver
        // Description: Creates default IProjectPathResolver instance
        // Notes: Used in parameterless constructor
        // **************************************************
        private static IProjectPathResolver GetDefaultProjectPathResolver()
        {
            return new DefaultProjectPathResolver();
        }

        // **************************************************
        // Function: GetDefaultFileSystemManager
        // Description: Creates default IFileSystemManager instance
        // Notes: Used in parameterless constructor
        // **************************************************
        private static IFileSystemManager GetDefaultFileSystemManager()
        {
            return new StandardFileSystemManager();
        }

        // **************************************************
        // Function: GetCheckBoxToggleFromApplication
        // Description: Retrieves CheckBoxToggle instance from application
        // **************************************************
        private CheckBoxToggle GetCheckBoxToggleFromApplication()
        {
            return (Application.Current as App)?.CheckBoxes;
        }

        // **************************************************
        // Function: GetConfigurationFromApplication
        // Description: Retrieves AppConfiguration instance from application
        // **************************************************
        private AppConfiguration GetConfigurationFromApplication()
        {
            return (Application.Current as App)?.Configuration;
        }

        #endregion

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PopulateLocationDropdown();
            UpdateRunDisplay();
            App.LocationChanged += OnLocationChanged;
            App.RunChanged += OnRunChanged;
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            App.LocationChanged -= OnLocationChanged;
            App.RunChanged -= OnRunChanged;
        }

        #region Directory Management

        // **************************************************
        // Function: MakeDirectoryIfNotExists
        // Description: Creates directory if it doesn't exist, or clears it if it does (except .gitkeep)
        // **************************************************
        private void MakeDirectoryIfNotExists(string directory)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                else
                {
                    // Clear directory contents except .gitkeep
                    ClearDirectoryExceptGitkeep(directory);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Permission denied creating directory");
                HandleDirectoryCreationError("Insufficient Permissions");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create directory");
                HandleDirectoryCreationError(ex.Message);
            }
        }

        // **************************************************
        // Function: ClearDirectoryExceptGitkeep
        // Description: Clears directory of all files and subdirectories except .gitkeep
        // **************************************************
        private void ClearDirectoryExceptGitkeep(string directory)
        {
            try
            {
                var dirInfo = new DirectoryInfo(directory);

                // Delete all files except .gitkeep
                foreach (var file in dirInfo.GetFiles())
                {
                    if (file.Name != ".gitkeep")
                    {
                        file.Delete();
                    }
                }

                // Delete all subdirectories
                foreach (var subDir in dirInfo.GetDirectories())
                {
                    subDir.Delete(recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear directory");
                throw;
            }
        }

        // **************************************************
        // Function: HandleDirectoryCreationError
        // Description: Displays error message for directory creation failures
        // **************************************************
        private void HandleDirectoryCreationError(string errorMessage)
        {
            MessageBox.Show(
                $"Cannot create directory: {errorMessage}",
                "Directory Creation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }

        #endregion

        #region Page Navigation

        // **************************************************
        // Function: HomeButtonClick
        // Description: Navigates to the home page
        // **************************************************
        private void HomeButtonClick(object sender, RoutedEventArgs e)
        {
            if (IsCurrentPageSettings())
            {
                //if (CheckForUnsavedChanges())
                //{
                    ExpandSidebar();
                    MainFrame.Visibility = Visibility.Collapsed;
                //}
            }
            else
            {
                ExpandSidebar();
                MainFrame.Visibility = Visibility.Collapsed;
            }
        }

        // **************************************************
        // Function: SignOutButtonClick
        // Description: Navigates back to the signin page
        // **************************************************
        private void SignOutButtonClick(object sender, RoutedEventArgs e)
        {
            if (IsCurrentPageSettings() /*&& !CheckForUnsavedChanges()*/)
                return;

            ((App)Application.Current).ResetSettingsToDefaults();
            AuthWindow signin = new AuthWindow();
            signin.Show();
            this.Close();
        }

        // **************************************************
        // Function: HistoryButtonClick
        // Description: Navigates to the history page
        // **************************************************
        private void HistoryButtonClick(object sender, RoutedEventArgs e)
        {
            if (IsCurrentPageSettings())
            {
                //if (CheckForUnsavedChanges())
                //{
                    CollapseSidebar();
                    NavigateToPage(new History(_pathResolver, _fileSystemManager, _logger), "History");
                //}
            }
            else
            {
                CollapseSidebar();
                NavigateToPage(new History(_pathResolver, _fileSystemManager, _logger), "History");
            }
        }

        // **************************************************
        // Function: SettingsButtonClick
        // Description: Navigates to the settings page
        // **************************************************
        private void SettingsButtonClick(object sender, RoutedEventArgs e)
        {
            CollapseSidebar();
            NavigateToPage(new Settings(_pathResolver, _fileSystemManager, _logger), "Settings");
        }

        private void AccountSettingsButtonClick(object sender, RoutedEventArgs e)
        {
            if (IsCurrentPageSettings() /*&& !CheckForUnsavedChanges()*/)
                return;

            CollapseSidebar();
            NavigateToPage(new AccountSettings(), "AccountSettings");
        }

        // **************************************************
        // Function: IsCurrentPageSettings
        // Description: Returns true if the current frame content is the Settings page
        // **************************************************
        public bool IsCurrentPageSettings() => MainFrame.Content is Settings;

        // **************************************************
        // Function: CheckForUnsavedChanges
        // Description: Prompts the user if there are unsaved settings changes before navigating away
        // **************************************************
        //public bool CheckForUnsavedChanges()
        //{
        //    var page = MainFrame.Content as Settings;
        //    if (page == null)
        //        return true;

        //    var (confidence, hideOutput, hideErrors, highContrast, largeText) = page.GetCurrentValues();

        //    var saved = App.Settings;

        //    bool hasUnsavedChanges =
        //        confidence != (saved.ConfidenceThreshold * 100.0) ||
        //        hideOutput != saved.OutputBox ||
        //        hideErrors != saved.ErrorBox ||
        //        highContrast != saved.HighContrastMode ||
        //        largeText != saved.LargeText;

        //    if (hasUnsavedChanges)
        //    {
        //        var result = MessageBox.Show(
        //            "Unsaved settings changes. Do you want to leave the page?",
        //            "Settings",
        //            MessageBoxButton.YesNo,
        //            MessageBoxImage.Warning);

        //        return result == MessageBoxResult.Yes;
        //    }

        //    return true;
        //}

        // **************************************************
        // Function: NavigateToPage
        // Description: Handles logic common to both navigation functions
        // **************************************************
        private void NavigateToPage(object page, string pageName)
        {
            MainFrame.Visibility = Visibility.Visible;
            _logger.LogInformation("Navigating to {PageName}", pageName);

            try
            {
                MainFrame.Navigate(page);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to navigate to {PageName}", pageName);
                MessageBox.Show(
                    $"Navigation Error: {ex.Message}",
                    "Navigation Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region YOLO Processing

        // **************************************************
        // Function: RunYolo
        // Description: Executes Python YOLO script for video analysis
        // Notes: Original writing credit to Aden Ratliff, async update by Benjamin Kerr
        //          Running async so that UI thread isn't blocked
        // **************************************************
        private async Task RunYolo(string videoFolder)
        {
            _logger.LogInformation("Starting YOLO process with videoFolder: {VideoFolder}", videoFolder);

            // Only restart Python if it's not running at all (e.g. crashed between runs).
            // If it's still in the middle of startup, leave it alone - await _yoloReadyTcs.Task
            // will wait for the existing startup to finish without triggering a second launch.
            if (_yoloProcess == null || _yoloProcess.HasExited)
                EnsureYoloProcessRunning();

            // If the user changed location while models were loading, restart now with the
            // correct env vars before sending any work.
            string currentLocation = (Application.Current as App)?.ActiveLocation ?? "Unknown";
            if (!string.Equals(currentLocation, _yoloLocationAtStart, StringComparison.OrdinalIgnoreCase))
            {
                _yoloKillCount++; // signal ReadYoloOutputLoop that this is an intentional restart
                if (_yoloProcess != null && !_yoloProcess.HasExited)
                    try { _yoloProcess.Kill(); } catch { }
                StartYoloProcess();
            }

            // If the active run changed since Python started, restart so FISHLENS_RUN_FOLDER is correct.
            string currentRun = (Application.Current as App)?.ActiveRun ?? string.Empty;
            if (!string.Equals(currentRun, _yoloRunAtStart, StringComparison.OrdinalIgnoreCase))
            {
                _yoloKillCount++; // signal ReadYoloOutputLoop that this is an intentional restart
                if (_yoloProcess != null && !_yoloProcess.HasExited)
                    try { _yoloProcess.Kill(); } catch { }
                StartYoloProcess();
            }

            // Show inline progress bar
            Dispatcher.Invoke(ShowAnalysisProgress);

            // Wait for Python models to finish loading before sending work.
            // NOTE: this await is inside the try so that if a dying ReadYoloOutputLoop from a
            // previous process restart poisons _yoloReadyTcs, the exception is caught here instead
            // of propagating uncaught up through the async void call chain and crashing the app.
            lock (_errorBuilder) _errorBuilder.Clear();
            _currentVideoStatus = "Processing videos, please wait...";
            _totalVideos = 0;
            _processingTcs = new TaskCompletionSource<bool>();

            try
            {
                if (_yoloReadyTcs != null && !_yoloReadyTcs.Task.IsCompleted)
                {
                    Dispatcher.Invoke(() => SetAnalysisStatus("Starting up, please wait..."));
                    await _yoloReadyTcs.Task;
                }

                _yoloProcess.StandardInput.WriteLine(videoFolder);
                _yoloProcess.StandardInput.Flush();
                await _processingTcs.Task;
            }
            catch (OperationCanceledException)
            {
                // User clicked Cancel - process already killed and restarting
                Dispatcher.Invoke(HideAnalysisProgress);
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(HideAnalysisProgress);
                MessageBox.Show(ex.Message, "Could not process videos.", MessageBoxButton.OK);
            }
        }

        // **************************************************
        // Function: EnsureYoloProcessRunning
        // Description: Starts the Python process if it is not already running.
        //              Does NOT restart due to Fast Mode - that goes through OnFastModeChanged.
        // **************************************************
        private void EnsureYoloProcessRunning()
        {
            if (_yoloProcess == null || _yoloProcess.HasExited)
                StartYoloProcess();
        }

        // **************************************************
        // Function: OnFastModeChanged
        // Description: Restarts Python only when the user saves a Fast Mode change in Settings
        // **************************************************
        private void OnFastModeChanged()
        {
            bool fastMode = _checkBoxes?.FastMode ?? false;
            if (_yoloFastModeAtStart == fastMode) return; // no actual change, ignore

            _yoloKillCount++; // signal ReadYoloOutputLoop that this is an intentional restart
            if (_yoloProcess != null && !_yoloProcess.HasExited)
                try { _yoloProcess.Kill(); } catch { }

            StartYoloProcess();
        }

        // **************************************************
        // Function: OnLocationChanged
        // Description: Refreshes the dropdown when admin changes location list via Settings
        // **************************************************
        private void OnLocationChanged()
        {
            Dispatcher.Invoke(PopulateLocationDropdown);
        }

        // **************************************************
        // Function: OnRunChanged
        // Description: Refreshes the run display when admin changes active run via Settings
        // **************************************************
        private void OnRunChanged()
        {
            Dispatcher.Invoke(UpdateRunDisplay);
        }

        // **************************************************
        // Function: OnConfidenceThresholdChanged
        // Description: Reapplies library button tint when Settings saves a new threshold
        // **************************************************
        private void OnConfidenceThresholdChanged()
        {
            Dispatcher.Invoke(RefreshLibraryConfidenceStyles);
        }

        // **************************************************
        // Function: UpdateRunDisplay
        // Description: Updates the run label in the header to show the current active run
        // **************************************************
        private void UpdateRunDisplay()
        {
            var app = Application.Current as App;
            if (app == null) return;

            // Prefer the App-level property, but fall back to Configuration if they've drifted
            string activeRun = !string.IsNullOrWhiteSpace(app.ActiveRun)
                ? app.ActiveRun
                : (app.Configuration?.ActiveRun ?? string.Empty);

            if (runDisplayLabel != null)
                runDisplayLabel.Text = string.IsNullOrWhiteSpace(activeRun) ? "No run selected" : activeRun;
        }




        // **************************************************
        // Function: GetUpstreamDirectionForActiveLocation
        // Description: Returns "left" or "right" based on the UpstreamDirection of the
        //              currently active location stored in appsettings.json
        // **************************************************
        private string GetUpstreamDirectionForActiveLocation()
        {
            var app = Application.Current as App;
            string activeLocation = app?.ActiveLocation ?? "Unknown";
            if (app?.Configuration?.Locations == null) return "left";

            var match = app.Configuration.Locations
                .FirstOrDefault(l => string.Equals(l.Name, activeLocation, StringComparison.OrdinalIgnoreCase));
            return match?.UpstreamDirection ?? "left";
        }




        // **************************************************
        // Function: LoadUpstreamDirectionMap
        // Description: Returns a dictionary of location name -> upstream direction ("left"/"right")
        //              loaded from appsettings.json. Used for direction-flip logic on location change.
        // **************************************************
        private Dictionary<string, string> LoadUpstreamDirectionMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var app = Application.Current as App;
            if (app?.Configuration?.Locations == null) return map;

            foreach (var loc in app.Configuration.Locations)
            {
                if (!string.IsNullOrWhiteSpace(loc.Name))
                    map[loc.Name] = loc.UpstreamDirection ?? "left";
            }
            return map;
        }




        // **************************************************
        // Function: PopulateLocationDropdown
        // Description: Loads named locations from appsettings.json into the header ComboBox
        // **************************************************
        private void PopulateLocationDropdown()
        {
            try
            {
                var app = Application.Current as App;
                if (app == null) return;

                // Read locations from the shared in-memory Configuration (populated from DB at sign-in)
                var locationNames = app.Configuration?.Locations?
                    .Select(l => l.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList() ?? new List<string>();

                if (locationNames.Count == 0)
                    locationNames.Add("Unknown");

                // ActiveLocation still lives in JSON for now (per your decision)
                string activeLocation = app.ActiveLocation;
                if (string.IsNullOrWhiteSpace(activeLocation) || !locationNames.Contains(activeLocation))
                    activeLocation = locationNames[0];

                // Suppress SelectionChanged while populating
                locationDropdown.SelectionChanged -= LocationDropdown_SelectionChanged;
                locationDropdown.ItemsSource = locationNames;
                locationDropdown.SelectedItem = activeLocation;
                locationDropdown.SelectionChanged += LocationDropdown_SelectionChanged;

                // Keep App in sync
                app.ActiveLocation = locationDropdown.SelectedItem as string ?? "Unknown";
            }
            catch { /* non-critical */ }
        }

        // **************************************************
        // Function: SaveActiveLocationToDatabase
        // Description: Saves the active location to the database
        // **************************************************

        private void SaveActiveLocationToDatabase(string location)
        {
            var app = Application.Current as App;
            if (app == null || app.CurrentUserId <= 0) return;

            try
            {
                using var conn = new System.Data.SqlClient.SqlConnection(app.connectionString);
                conn.Open();
                using var cmd = new System.Data.SqlClient.SqlCommand("kaharra.SaveUserActiveLocation", conn);
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@pUserId", app.CurrentUserId);
                cmd.Parameters.AddWithValue("@pActiveLocation", location ?? "Unknown");
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save active location to DB");
            }
        }








        // **************************************************
        // Function: LocationDropdown_SelectionChanged
        // Description: Persists the newly selected location to appsettings.json and updates App
        // **************************************************
        private void LocationDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string selected = locationDropdown.SelectedItem as string;
            if (string.IsNullOrEmpty(selected)) return;

            // Block location changes while a video batch is processing
            bool isProcessing = _processingTcs != null && !_processingTcs.Task.IsCompleted;
            if (isProcessing)
            {
                string committed = (Application.Current as App)?.ActiveLocation ?? "Unknown";
                locationDropdown.SelectionChanged -= LocationDropdown_SelectionChanged;
                locationDropdown.SelectedItem = locationDropdown.Items.Contains(committed) ? committed : locationDropdown.Items[0];
                locationDropdown.SelectionChanged += LocationDropdown_SelectionChanged;
                return;
            }

            var app = Application.Current as App;
            if (app != null)
            {
                app.ActiveLocation = selected;
                if (app.Configuration != null)
                    app.Configuration.ActiveLocation = selected;
            }

            // Persist to DB so next sign-in remembers the choice
            SaveActiveLocationToDatabase(selected);
        }




        // **************************************************
        // Function: GetLocationCsvPaths
        // Description: Returns the set of CSV paths that location-update operations should touch.
        //              Debug runs only have a single debug.csv; normal runs have four files.
        // **************************************************
        private string[] GetLocationCsvPaths(string activeRun)
        {
            if (activeRun.Equals("debug", StringComparison.OrdinalIgnoreCase))
                return new[] { _pathResolver.ResolveCsvScriptPath() };

            return new[]
            {
                _pathResolver.ResolveSessionCsvPath(activeRun),
                _pathResolver.ResolveRunCsvPath(activeRun),
                _pathResolver.ResolveAllTimeMasterFishCsvPath(),
                _pathResolver.ResolveSessionNoFishCsvPath(activeRun),
            };
        }

        private string ResolveVideoRun(FishLens_App.Models.Video video)
        {
            if (!string.IsNullOrWhiteSpace(video?.Run))
                return video.Run;

            return (Application.Current as App)?.ActiveRun ?? string.Empty;
        }

        private string BuildLibrarySectionKey(string videoPath, string run)
        {
            string folderPath = string.Empty;
            try
            {
                folderPath = Path.GetDirectoryName(videoPath) ?? string.Empty;
            }
            catch { }

            return $"{run ?? string.Empty}|{folderPath}";
        }

        private string GetHeaderTag(string sectionKey) => $"header:{sectionKey}";

        private LibrarySectionContext CreateLibrarySectionContext(string videoPath, string run)
        {
            string folderPath = string.Empty;
            string folderName = _currentFolderName;

            try
            {
                folderPath = Path.GetDirectoryName(videoPath) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(folderPath))
                    folderName = Path.GetFileName(folderPath);
            }
            catch { }

            return new LibrarySectionContext
            {
                SectionKey = BuildLibrarySectionKey(videoPath, run),
                FolderName = folderName ?? string.Empty,
                FolderPath = folderPath,
                Run = run ?? string.Empty,
            };
        }

        private bool SameVideoIdentity(FishLens_App.Models.Video left, FishLens_App.Models.Video right)
        {
            if (left == null || right == null) return false;

            bool pathMatch = !string.IsNullOrWhiteSpace(left.VideoFilePath) &&
                !string.IsNullOrWhiteSpace(right.VideoFilePath) &&
                string.Equals(left.VideoFilePath, right.VideoFilePath, StringComparison.OrdinalIgnoreCase);
            bool fileMatch = string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            bool runMatch = string.Equals(ResolveVideoRun(left), ResolveVideoRun(right), StringComparison.OrdinalIgnoreCase);

            return runMatch && (pathMatch || fileMatch);
        }

        private bool CsvRowMatchesVideo(string[] cols, FishLens_App.Models.Video video, string csvPath)
        {
            if (cols.Length == 0 || video == null) return false;

            string storedPath = cols[0].Trim();
            bool pathMatch = !string.IsNullOrWhiteSpace(video.VideoFilePath) &&
                string.Equals(storedPath, video.VideoFilePath, StringComparison.OrdinalIgnoreCase);
            bool nameMatch = string.Equals(Path.GetFileName(storedPath), video.Name, StringComparison.OrdinalIgnoreCase);
            if (!pathMatch && !nameMatch)
                return false;

            bool isAllHistory = string.Equals(Path.GetFileName(csvPath), "all_history.csv", StringComparison.OrdinalIgnoreCase);
            if (!isAllHistory)
                return true;

            if (cols.Length <= 10)
                return true;

            return string.Equals(cols[10].Trim(), ResolveVideoRun(video), StringComparison.OrdinalIgnoreCase);
        }

        private TextBox FindSectionHeaderTextBox(string sectionKey)
        {
            foreach (var child in videoList.Children)
            {
                if (child is Grid headerGrid &&
                    headerGrid.Tag is string tag &&
                    tag == GetHeaderTag(sectionKey))
                {
                    return headerGrid.Children.OfType<TextBox>().FirstOrDefault();
                }
            }

            return null;
        }

        private string GetSectionDisplayText(LibrarySectionContext sectionContext, IEnumerable<FishLens_App.Models.Video> seedVideos = null)
        {
            var videos = new List<FishLens_App.Models.Video>();
            if (seedVideos != null)
                videos.AddRange(seedVideos.Where(v => v != null));

            foreach (var child in videoList.Children)
            {
                if (child is Grid rowGrid &&
                    rowGrid.Tag is string tag &&
                    tag == sectionContext.SectionKey)
                {
                    foreach (var button in rowGrid.Children.OfType<Button>())
                    {
                        if (button.DataContext is FishLens_App.Models.Video video)
                            videos.Add(video);
                    }
                }
            }

            string runText = videos.Select(ResolveVideoRun)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .SingleOrDefault() ?? sectionContext.Run;
            string locationText = videos.Select(v => v.Location)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() switch
                {
                    var list when list.Count == 0 => "--",
                    var list when list.Count == 1 => list[0],
                    _ => "Mixed Locations",
                };

            return $"{(string.IsNullOrWhiteSpace(runText) ? "--" : runText)} : {locationText} : {sectionContext.FolderName}";
        }

        private void RefreshLibrarySectionHeader(string sectionKey)
        {
            var headerTextBox = FindSectionHeaderTextBox(sectionKey);
            if (headerTextBox == null) return;

            if (headerTextBox.Parent is Grid headerGrid &&
                headerGrid.DataContext is LibrarySectionContext sectionContext)
            {
                headerTextBox.Text = GetSectionDisplayText(sectionContext);
            }
        }

        // **************************************************
        // Function: BulkUpdateLocationInCsvs
        // Description: Overwrites the location column (col 1) in all run CSVs with a new value.
        //              Also flips the direction column (upstream<->downstream) whenever the upstream
        //              direction changed between the row's old location and the new one.
        //              Used when the user corrects a wrong location selection after analysis.
        // **************************************************
        private void BulkUpdateLocationInCsvs(string newLocation, string activeRun)
        {
            var dirMap = LoadUpstreamDirectionMap();
            dirMap.TryGetValue(newLocation, out string newUpstreamDir);

            var csvPaths = GetLocationCsvPaths(activeRun);

            foreach (string path in csvPaths)
            {
                if (!File.Exists(path)) continue;
                try
                {
                    var lines = File.ReadAllLines(path);
                    for (int i = 1; i < lines.Length; i++)
                    {
                        var cols = lines[i].Split(',');
                        if (cols.Length > 1)
                        {
                            // Flip direction if upstream direction differs (fish CSVs only - col 6 exists)
                            if (cols.Length > 6 && newUpstreamDir != null)
                            {
                                string oldLocation = cols[1];
                                if (dirMap.TryGetValue(oldLocation, out string oldUpstreamDir) &&
                                    !oldUpstreamDir.Equals(newUpstreamDir, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (cols[6] == "upstream") cols[6] = "downstream";
                                    else if (cols[6] == "downstream") cols[6] = "upstream";
                                }
                            }

                            cols[1] = newLocation;
                            lines[i] = string.Join(",", cols);
                        }
                    }
                    File.WriteAllLines(path, lines);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update location in {Path}", path);
                }
            }

            MessageBox.Show($"Location updated to \"{newLocation}\" in all run CSV files.",
                "Location Updated", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // **************************************************
        // Function: UpdateLocationForVideosInCsvs
        // Description: Updates the location column only for specific video filenames across
        //              all four run CSVs (session_fish, run_master, all_history, session_no_fish).
        //              Also flips the direction column (upstream<->downstream) whenever the upstream
        //              direction differs between old and new location.  "indecisive" is left alone.
        // **************************************************
        private void UpdateLocationForVideosInCsvs(IEnumerable<FishLens_App.Models.Video> videos, string newLocation)
        {
            var selectedVideos = videos
                .Where(v => v != null)
                .ToList();
            if (selectedVideos.Count == 0) return;

            var dirMap = LoadUpstreamDirectionMap();
            dirMap.TryGetValue(newLocation, out string newUpstreamDir);

            foreach (var runGroup in selectedVideos.GroupBy(v => ResolveVideoRun(v)))
            {
                var csvPaths = GetLocationCsvPaths(runGroup.Key);
                foreach (string path in csvPaths)
                {
                    if (!File.Exists(path)) continue;
                    try
                    {
                        var lines = File.ReadAllLines(path);
                        bool updated = false;
                        for (int i = 1; i < lines.Length; i++)
                        {
                            var cols = lines[i].Split(',');
                            if (cols.Length <= 1) continue;

                            var matchedVideo = runGroup.FirstOrDefault(v => CsvRowMatchesVideo(cols, v, path));
                            if (matchedVideo == null) continue;

                            // Flip direction if upstream direction differs (fish CSVs only - col 6 exists)
                            if (cols.Length > 6 && newUpstreamDir != null)
                            {
                                string oldLocation = cols[1];
                                if (dirMap.TryGetValue(oldLocation, out string oldUpstreamDir) &&
                                    !oldUpstreamDir.Equals(newUpstreamDir, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (cols[6] == "upstream") cols[6] = "downstream";
                                    else if (cols[6] == "downstream") cols[6] = "upstream";
                                    // indecisive / unknown -> unchanged
                                }
                            }

                            cols[1] = newLocation;
                            lines[i] = string.Join(",", cols);
                            updated = true;
                        }

                        if (updated)
                            File.WriteAllLines(path, lines);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to update location in {Path}", path);
                    }
                }
            }

            foreach (var video in selectedVideos)
                video.Location = newLocation;

            bool currentTrackUpdated = false;
            foreach (var track in _currentTracks)
            {
                if (selectedVideos.Any(v => SameVideoIdentity(v, track)))
                {
                    track.Location = newLocation;
                    currentTrackUpdated = true;
                }
            }

            foreach (string sectionKey in selectedVideos.Select(v => BuildLibrarySectionKey(v.VideoFilePath, ResolveVideoRun(v))).Distinct(StringComparer.OrdinalIgnoreCase))
                RefreshLibrarySectionHeader(sectionKey);

            if (currentTrackUpdated && _currentTracks.Count > 0)
                DisplayTrackInUi(_currentTracks[_currentTrackIndex]);
        }

        // **************************************************
        // Function: StartYoloProcess
        // Description: Spawns the persistent Python process and begins the output reader loop
        // **************************************************
        private void StartYoloProcess()
        {
            // Snapshot FastMode and Location so they stay in sync with the env vars we pass.
            _yoloFastModeAtStart = _checkBoxes?.FastMode ?? false;
            _yoloLocationAtStart = (Application.Current as App)?.ActiveLocation ?? "Unknown";
            _yoloRunAtStart = (Application.Current as App)?.ActiveRun ?? string.Empty;

            string yoloScriptPath = _pathResolver.ResolveYoloScriptPath();
            string pythonPath = Path.Combine(_pathResolver.ResolveProjectRoot(), "venv", "Scripts", "python.exe");

            var processInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                WorkingDirectory = _pathResolver.ResolveProjectRoot(),
                Arguments = $"-u \"{yoloScriptPath}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            processInfo.Environment["FISHLENS_FAST_MODE"] = _yoloFastModeAtStart ? "1" : "0";
            processInfo.Environment["FISHLENS_LOCATION"] = (Application.Current as App)?.ActiveLocation ?? "Unknown";
            processInfo.Environment["FISHLENS_UPSTREAM_DIRECTION"] = GetUpstreamDirectionForActiveLocation();

            // Pass the active run folder so Python writes to the correct CSV hierarchy.
            string activeRun = (Application.Current as App)?.ActiveRun ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(activeRun))
                processInfo.Environment["FISHLENS_RUN_FOLDER"] = _pathResolver.ResolveRunFolder(activeRun);
            else
                processInfo.Environment["FISHLENS_RUN_FOLDER"] = string.Empty;

            _yoloReadyTcs = new TaskCompletionSource<bool>();
            _yoloProcess = Process.Start(processInfo);

            // Consume stderr in background to prevent pipe deadlock
            Task.Run(() =>
            {
                string errLine;
                while ((errLine = _yoloProcess.StandardError.ReadLine()) != null)
                    lock (_errorBuilder) _errorBuilder.AppendLine(errLine);
            });

            // Start persistent stdout reader
            Task.Run(ReadYoloOutputLoop);
        }

        // **************************************************
        // Function: ReadYoloOutputLoop
        // Description: Background loop that reads Python stdout for the lifetime of the process
        // **************************************************
        private void ReadYoloOutputLoop()
        {
            // Snapshot the process, ready-TCS, and kill-count for this Python instance at startup.
            // If the process is killed and restarted (e.g. run change), the NEW instance
            // sets _yoloProcess and _yoloReadyTcs to fresh objects.  Without snapshots here,
            // this dying loop would TrySetException on the NEW TCS and crash the next run.
            // _yoloKillCount is snapshotted so we can tell whether the exit was intentional:
            // if it was incremented since we started, the kill was a deliberate restart and we
            // must NOT poison _processingTcs (the new analysis is already underway).
            var myProcess   = _yoloProcess;
            var myReadyTcs  = _yoloReadyTcs;
            int myKillCount = _yoloKillCount;

            string line;
            while ((line = myProcess.StandardOutput.ReadLine()) != null)
            {
                if (line == "[PROGRESS] STARTUP")
                {
                    Dispatcher.Invoke(() => SetAnalysisStatus("Starting up, please wait..."));
                }
                else if (line == "[PROGRESS] READY")
                {
                    myReadyTcs?.TrySetResult(true);
                }
                else if (line.StartsWith("[PROGRESS] TOTAL:") &&
                    int.TryParse(line.Substring("[PROGRESS] TOTAL:".Length), out int total))
                {
                    _totalVideos = total;
                    Dispatcher.Invoke(() =>
                    {
                        analysisProgressBar.Minimum = 0;
                        analysisProgressBar.Maximum = total;
                        analysisProgressBar.Value = 0;
                    });
                }
                else if (line.StartsWith("[PROGRESS] VIDEO:"))
                {
                    string payload = line.Substring("[PROGRESS] VIDEO:".Length);
                    int sep = payload.IndexOf('|');
                    string fraction = sep >= 0 ? payload.Substring(0, sep) : payload;
                    string filename = sep >= 0 ? payload.Substring(sep + 1) : string.Empty;
                    var parts = fraction.Split('/');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int current))
                    {
                        _currentVideoStatus = $"Video {fraction} - {filename}";
                        int capturedCurrent = current;
                        Dispatcher.Invoke(() =>
                        {
                            SetAnalysisStatus(_currentVideoStatus);
                            SetAnalysisFrameInfo(string.Empty); // clear frame line between videos
                            analysisProgressBar.Value = capturedCurrent - 1;
                        });
                    }
                }
                else if (line.StartsWith("[PROGRESS] FRAME:"))
                {
                    string payload = line.Substring("[PROGRESS] FRAME:".Length);
                    var parts = payload.Split('/');
                    if (parts.Length == 2)
                    {
                        int upper;
                        int lower;
                        string frameInfo;
                        int percentage = 0;
                        if (parts[1] == "?")
                        {
                            frameInfo = $"Frame {parts[0]}";
                        }
                        else if (int.TryParse(parts[0], out upper) &&
                                 int.TryParse(parts[1], out lower) &&
                                 lower != 0)
                        {
                            percentage = upper * 100 / lower;
                            frameInfo = $"Frame {percentage}%";
                        }
                        else
                        {
                            frameInfo = "Frame ?";
                        }
                        frameInfo = parts[1] == "?"
                            ? $"Frame {parts[0]}"
                            : $"Video progress: " + percentage + "%";
                        Dispatcher.Invoke(() => SetAnalysisFrameInfo(frameInfo));
                    }
                }
                else if (line == "[PROGRESS] DONE")
                {
                    string error;
                    lock (_errorBuilder) { error = _errorBuilder.ToString(); _errorBuilder.Clear(); }
                    Dispatcher.Invoke(() =>
                    {
                        analysisProgressBar.Value = analysisProgressBar.Maximum;
                        HideAnalysisProgress();
                        DisplayProcessOutputIfNeeded(error);
                    });
                    _processingTcs?.TrySetResult(true);
                }
            }

            // Process exited - fail any pending run or startup wait for THIS instance only.
            // Using the snapshot myReadyTcs prevents a dying old loop from poisoning a freshly
            // created TCS that belongs to the new Python process.
            myReadyTcs?.TrySetException(new Exception("Python process exited unexpectedly."));

            // Only fail _processingTcs if this was NOT an intentional restart kill.
            // When we kill+restart (run change, location change, fast mode), _yoloKillCount is
            // incremented before the kill. If the count changed since this loop started, a new
            // Python process is already loading and we must not poison its _processingTcs.
            if (_yoloKillCount == myKillCount)
                _processingTcs?.TrySetException(new Exception("Python process exited unexpectedly."));
        }

        // **************************************************
        // Function: ShowAnalysisProgress / HideAnalysisProgress / SetAnalysisStatus
        // Description: Helpers to show/hide/update the inline progress area
        // **************************************************
        private void ShowAnalysisProgress()
        {
            analysisProgressArea.Visibility = Visibility.Visible;
            analysisProgressBar.Value = 0;
            analysisStatusText.Text = "Starting up, please wait...";
            analysisFrameText.Text = string.Empty;
            App.RaiseAnalysisStateChanged(true);
        }

        private void HideAnalysisProgress()
        {
            analysisProgressArea.Visibility = Visibility.Collapsed;
            App.RaiseAnalysisStateChanged(false);
        }

        private void SetAnalysisStatus(string status)
        {
            analysisStatusText.Text = status;
        }

        private void SetAnalysisFrameInfo(string info)
        {
            analysisFrameText.Text = info;
        }

        // **************************************************
        // Function: UpdateActionButtonState
        // Description: Enables/disables the Delete, Change Location, and Undo buttons based on
        //              whether any video checkboxes are checked and whether there is undo history.
        // **************************************************
        private void UpdateActionButtonState()
        {
            bool anyChecked = GetSelectedVideoGrids().Count > 0;
            deleteSelectedVideos.IsEnabled = anyChecked;
            changeLocationForSelected.IsEnabled = anyChecked;
            undoLastDelete.IsEnabled = _deletionHistory.Count > 0;
            fishPresentStatus.IsEnabled = _processingComplete;
            fishTravelDirection.IsEnabled = _processingComplete;
            fishSpecies.IsEnabled = _processingComplete;
            saveButton.IsEnabled = _processingComplete;
            fishPresentConfidence.IsEnabled = _processingComplete;
            fishTravelDirection.IsEnabled = _processingComplete;
            fishSpeciesConfidence.IsEnabled = _processingComplete;
            
        }

        // **************************************************
        // Function: InlineCancelClick
        // Description: Cancel button handler in the inline progress area
        // **************************************************
        private void InlineCancelClick(object sender, RoutedEventArgs e)
        {
            OnProcessingCancelled();
        }

        // **************************************************
        // Function: OnProcessingCancelled
        // Description: Kills the Python process mid-run and restarts it ready for next run
        // **************************************************
        private void OnProcessingCancelled()
        {
            _processingTcs?.TrySetCanceled();
            if (_yoloProcess != null && !_yoloProcess.HasExited)
                try { _yoloProcess.Kill(); } catch { }
            _yoloProcess = null;
            HideAnalysisProgress();
            // Restart Python in the background so models are ready for the next run
            EnsureYoloProcessRunning();
        }

        // **************************************************
        // Function: DisplayProcessOutputIfNeeded
        // Description: Shows process errors if present
        // **************************************************
        private void DisplayProcessOutputIfNeeded(string error)
        {
            if (!string.IsNullOrEmpty(error))
            {
                Dispatcher.Invoke(() =>
                    MessageBox.Show($"Python Error:\n{error}", "Process Error", MessageBoxButton.OK, MessageBoxImage.Error)
                );
            }
        }

        #endregion

        #region Video Processing

        // **************************************************
        // Function: OpenFolderClick
        // Description: Opens folder dialog and initiates video processing
        // **************************************************
        private async void OpenFolderClick(object sender, RoutedEventArgs e)
        {
            string sourceFolderPath = _pathResolver.ResolveSourceFolder();
            if (!string.IsNullOrEmpty(sourceFolderPath))
            {
                _currentFolderName = Path.GetFileName(sourceFolderPath);

                await ProcessVideos(sourceFolderPath);

                // Show export button if there is any CSV data - even after a cancel,
                // videos that finished before cancellation are in the CSV and exportable.
                string activeRun = (Application.Current as App)?.ActiveRun ?? string.Empty;
                if (File.Exists(_pathResolver.ResolveRunCsvPath(activeRun)))
                    exportData.Visibility = Visibility.Visible;
            }
        }

        // **************************************************
        // Function: ProcessVideos
        // Description: Orchestrates complete video processing workflow
        // **************************************************
        private async Task ProcessVideos(string inputFolder)
        {
            // Verify video files exist in the selected folder
            var videoFiles = Directory.GetFiles(inputFolder)
                .Where(f => VideoExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            if (videoFiles.Count == 0)
            {
                MessageBox.Show("No video files were found in the selected folder.",
                    "No Videos", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                _logger.LogInformation("Found {Count} video files in {Directory}", videoFiles.Count, inputFolder);

                PrepareDebugRunForReanalysis(videoFiles);

                // Step 1: run YOLO directly on the source folder
                await RunYolo(inputFolder);

                // Step 2: read CSV - always populate from whatever is in the CSV now.
                // If the user cancelled, videos processed before cancel are still in the
                // CSV and should appear. If nothing was processed, the list stays as-is.
                List<(FileInfo vid, FishLens_App.Models.Video data)> videoDataList = CreateSortedListOfVideos(inputFolder);

                if (videoDataList.Count > 0)
                {
                    // Step 3: update detail panel with first video
                    DisplayDataInUi(videoDataList[0].vid.Name, videoDataList[0].data.Run, videoDataList[0].vid.FullName);

                    // Step 4: create sidebar buttons (sorted by confidence)
                    CreateVideoButtonsList(videoDataList);
                    RefreshSessionOverview();
                    UpdateActionButtonState();

                    // Step 5: auto-load first video into player
                    var firstVideoPath = videoDataList[0].vid.FullName;
                    Dispatcher.Invoke(() => LoadVideoInPlayer(firstVideoPath));
                }
            }
            _processingComplete = true;
            UpdateActionButtonState();
        }

        // **************************************************
        // Function: PrepareDebugRunForReanalysis
        // Description: In debug mode, remove existing rows from debug.csv for any video names
        //              present in the folder being analyzed so the rerun writes fresh rows only.
        // **************************************************
        private void PrepareDebugRunForReanalysis(List<string> videoFiles)
        {
            string activeRun = (Application.Current as App)?.ActiveRun ?? string.Empty;
            if (!activeRun.Equals("debug", StringComparison.OrdinalIgnoreCase) || videoFiles.Count == 0)
                return;

            string debugCsvPath = _pathResolver.ResolveCsvScriptPath();
            if (!File.Exists(debugCsvPath))
                return;

            try
            {
                var incomingNames = new HashSet<string>(
                    videoFiles.Select(path => Path.GetFileName(path)),
                    StringComparer.OrdinalIgnoreCase);

                var lines = File.ReadAllLines(debugCsvPath).ToList();
                if (lines.Count == 0)
                    return;

                var remaining = new List<string> { lines[0] };
                for (int i = 1; i < lines.Count; i++)
                {
                    var cols = lines[i].Split(',');
                    string existingName = cols.Length > 0 ? Path.GetFileName(cols[0].Trim()) : string.Empty;
                    if (!incomingNames.Contains(existingName))
                        remaining.Add(lines[i]);
                }

                File.WriteAllLines(debugCsvPath, remaining);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not prepare debug.csv for reanalysis.");
            }
        }


        private static readonly HashSet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".avi", ".mov", ".mkv", ".asf", ".wmv", ".flv", ".webm"
        };

        #endregion

        #region Video Data Management

        public void DeleteSelectedVideosClick(object sender, EventArgs e)
        {
            var selected = GetSelectedVideoGrids();
            if (selected.Count == 0)
            {
                MessageBox.Show("No videos selected for deletion.", "Delete Videos", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!ConfirmDelete(selected.Count)) return;

            // Build an undo batch (UI state only - no files are touched).
            var batch = new DeletionBatch();
            foreach (var (grid, path, video) in selected)
            {
                string sectionKey = grid.Tag as string ?? BuildLibrarySectionKey(path, ResolveVideoRun(video));
                batch.Items.Add((path, grid, video ?? new FishLens_App.Models.Video(), sectionKey));

                // Capture the full header display text for undo reconstruction.
                if (!batch.FolderHeaderTexts.ContainsKey(sectionKey))
                {
                    foreach (var child in videoList.Children)
                    {
                        if (child is Grid hg && hg.Tag is string ht && ht == GetHeaderTag(sectionKey))
                        {
                            foreach (var elem in hg.Children)
                            {
                                if (elem is TextBox tb) { batch.FolderHeaderTexts[sectionKey] = tb.Text; break; }
                            }
                            break;
                        }
                    }
                }
            }
            _deletionHistory.Push(batch);

            // Remove from the UI list only.
            RemoveUiGrids(selected.Select(x => x.grid).ToList());

            // If the currently-playing video was hidden, stop the player.
            foreach (var (_, path, _) in selected)
            {
                if (videoPlayer.Source != null && string.Equals(videoPlayer.Source.LocalPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    videoPlayer.Stop();
                    videoPlayer.Source = null;
                }
            }

            // If the list is now empty, hide the video controls entirely.
            bool anyVideosLeft = videoList.Children.OfType<Grid>().Any(g => g.Tag is string t && !t.StartsWith("header:"));
            if (!anyVideosLeft)
            {
                videoPlayer.Stop();
                videoPlayer.Source = null;
                videoControls.Visibility = Visibility.Collapsed;
            }

            RefreshSessionOverview();
            UpdateActionButtonState();
            MessageBox.Show($"Removed {selected.Count} video(s) from view.", "Removed", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // **************************************************
        // Function: ChangeLocationForSelectedClick
        // Description: Updates the location column in all CSVs for the checked videos only.
        //              Mirrors the delete workflow: user checks videos, clicks the button.
        // **************************************************
        public void ChangeLocationForSelectedClick(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedVideoGrids();
            if (selected.Count == 0)
            {
                MessageBox.Show("No videos selected. Use the checkboxes to select one or more videos first.",
                    "Change Location", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string newLocation = ShowLocationPickerDialog();
            if (newLocation == null) return;

            UpdateLocationForVideosInCsvs(selected.Select(x => x.video), newLocation);
            RefreshSessionOverview();
            UpdateActionButtonState();

            MessageBox.Show(
                $"Location updated to \"{newLocation}\" for {selected.Count} video(s).",
                "Location Updated", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // **************************************************
        // Function: ShowLocationPickerDialog
        // Description: Shows a simple dialog for picking a location from the configured list.
        //              Returns the chosen location string, or null if cancelled.
        // **************************************************
        private string ShowLocationPickerDialog()
        {
            var locations = locationDropdown.Items.Cast<string>().ToList();
            if (locations.Count == 0) return null;

            var dlg = new Window
            {
                Title = "Change Location",
                Width = 340,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
            };

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock
            {
                Text = "Select the new location for the checked videos:",
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap,
            });

            var combo = new ComboBox
            {
                ItemsSource = locations,
                SelectedIndex = 0,
                Margin = new Thickness(0, 0, 0, 14),
            };
            panel.Children.Add(combo);

            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okBtn = new Button { Content = "OK", Width = 70, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancelBtn = new Button { Content = "Cancel", Width = 70, IsCancel = true };
            buttonRow.Children.Add(okBtn);
            buttonRow.Children.Add(cancelBtn);
            panel.Children.Add(buttonRow);

            dlg.Content = panel;

            string result = null;
            okBtn.Click += (s, ev) => { result = combo.SelectedItem as string; dlg.DialogResult = true; };
            cancelBtn.Click += (s, ev) => { dlg.DialogResult = false; };
            dlg.ShowDialog();
            return result;
        }

        // **************************************************
        // Function: GetSelectedVideoGrids
        // Description: Returns list of selected video grids and their file paths
        // **************************************************
        private List<(Grid grid, string path, FishLens_App.Models.Video video)> GetSelectedVideoGrids()
        {
            var result = new List<(Grid, string, FishLens_App.Models.Video)>();
            foreach (var child in videoList.Children)
            {
                if (child is Grid g)
                {
                    CheckBox cb = null;
                    Button btn = null;
                    FishLens_App.Models.Video video = null;
                    foreach (var elem in g.Children)
                    {
                        if (elem is CheckBox c) cb = c;
                        if (elem is Button b)
                        {
                            btn = b;
                            video = b.DataContext as FishLens_App.Models.Video;
                        }
                    }

                    if (cb != null && cb.IsChecked == true && btn != null && btn.Tag is string path)
                    {
                        result.Add((g, path, video));
                    }
                }
            }

            return result;
        }

        // **************************************************
        // Function: ConfirmDelete
        // Description: Confirms removal with the user
        // **************************************************
        private bool ConfirmDelete(int count)
        {
            var result = MessageBox.Show(
                $"Remove {count} selected video(s) from the list?\nThe actual video files will not be deleted.",
                "Remove from List", MessageBoxButton.YesNo, MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        }

        // **************************************************
        // Function: RemoveUiGrids
        // Description: Removes video grids and cleans up empty folder headers/separators
        // **************************************************
        private void RemoveUiGrids(List<Grid> grids)
        {
            foreach (var g in grids)
            {
                videoList.Children.Remove(g);
            }

            var headersToRemove = new List<UIElement>();
            foreach (var child in videoList.Children)
            {
                if (child is Grid headerGrid && headerGrid.Tag is string t && t.StartsWith("header:"))
                {
                    string sectionKey = t.Substring("header:".Length);
                    bool anyRemaining = false;
                    foreach (var c2 in videoList.Children)
                    {
                        if (c2 is Grid g2 && g2.Tag is string t2 && t2 == sectionKey)
                        {
                            anyRemaining = true;
                            break;
                        }
                    }

                    if (!anyRemaining) headersToRemove.Add(headerGrid);
                }
            }

            foreach (var h in headersToRemove)
            {
                int idx = videoList.Children.IndexOf(h);
                if (idx >= 0)
                {
                    videoList.Children.RemoveAt(idx);
                    if (videoList.Children.Count > idx && videoList.Children[idx] is Separator)
                    {
                        videoList.Children.RemoveAt(idx);
                    }
                }
            }
        }

        // **************************************************
        // Function: GetFolderTagForPath
        // Description: Derives the folder tag (folder name) for a given file path
        // **************************************************
        private string GetFolderTagForPath(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir)) return string.Empty;
                return Path.GetFileName(dir);
            }
            catch { return string.Empty; }
        }

        // **************************************************
        // Function: UndoLastDeleteClick
        // Description: Restores the most recently hidden batch back into the UI list.
        //              No files are moved and no CSV rows are changed.
        // **************************************************
        public void UndoLastDeleteClick(object sender, EventArgs e)
        {
            if (_deletionHistory.Count == 0)
            {
                MessageBox.Show("Nothing to undo.", "Undo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var batch = _deletionHistory.Pop();
            RestoreUiForFiles(batch);
            RefreshSessionOverview();
            UpdateActionButtonState();
            MessageBox.Show($"Restored {batch.Items.Count} video(s) to the list.", "Undo Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // **************************************************
        // Function: RestoreUiForFiles
        // Description: Puts the saved Grid elements from a DeletionBatch back into the
        //              video list.  The original Grid is reused so button handlers and
        //              tags are all preserved without re-creating anything.
        // **************************************************
        private void RestoreUiForFiles(DeletionBatch batch)
        {
            foreach (var item in batch.Items)
            {
                // Ensure the folder header exists before re-inserting the row.
                bool headerExists = false;
                foreach (var child in videoList.Children)
                {
                    if (child is Grid g && g.Tag is string t && t == GetHeaderTag(item.folder))
                    {
                        headerExists = true;
                        break;
                    }
                }
                if (!headerExists)
                {
                    var sectionContext = item.grid.DataContext as LibrarySectionContext ??
                        CreateLibrarySectionContext(item.originalPath, ResolveVideoRun(item.video));
                    CreateFolderHeader(sectionContext, batch.FolderHeaderTexts.TryGetValue(item.folder, out var saved) ? saved : null);
                }

                // Re-add the original grid (checkbox state is reset to unchecked).
                if (item.grid.Parent == null)
                {
                    foreach (var elem in item.grid.Children)
                        if (elem is CheckBox cb) cb.IsChecked = false;
                    videoList.Children.Add(item.grid);
                }
            }
        }

        // **************************************************
        // Function: CreateSortedListOfVideos
        // Description: Creates list of videos sorted by confidence rating
        // **************************************************
        private List<(FileInfo vid, FishLens_App.Models.Video data)> CreateSortedListOfVideos(string directory)
        {
            DirectoryInfo vidsInfo = new DirectoryInfo(directory);
            FileInfo[] fileInfos = vidsInfo.GetFiles("*");

            List<(FileInfo vid, FishLens_App.Models.Video data)> videoDataList = new List<(FileInfo, FishLens_App.Models.Video)>();

            foreach (FileInfo vid in fileInfos)
            {
                if (IsVideoFile(vid))
                {
                    FishLens_App.Models.Video data = GetData(vid.Name, vid.FullName);
                    if (string.IsNullOrWhiteSpace(data.VideoFilePath))
                        data.VideoFilePath = vid.FullName;
                    if (string.IsNullOrWhiteSpace(data.Run))
                        data.Run = (Application.Current as App)?.ActiveRun ?? string.Empty;
                    videoDataList.Add((vid, data));
                }
            }

            return videoDataList.OrderBy(x => x.data.AvgConfidence).ToList();
        }

        // **************************************************
        // Function: IsVideoFile
        // Description: Checks if file is a supported video format
        // **************************************************
        private bool IsVideoFile(FileInfo file)
        {
            string extension = file.Extension.ToLower();
            return extension == ".mp4" || extension == ".asf";
        }

        // **************************************************
        // Function: GetData
        // Description: Retrieves summary data for a library video card.
        //              For multi-fish videos, the card uses the lowest track confidence
        //              so the whole video is flagged if any fish falls below threshold.
        // **************************************************
        private FishLens_App.Models.Video GetData(string videoFileName, string videoFilePath = null, string sourceRun = null)
        {
            var tracks = GetAllTracks(videoFileName, sourceRun, videoFilePath);
            if (tracks.Count == 0)
                return new FishLens_App.Models.Video();

            var summary = tracks[0];
            summary.AvgConfidence = tracks.Min(track => track.AvgConfidence);
            return summary;
        }

        // **************************************************
        // Function: GetAllTracks
        // Description: Returns all CSV rows (tracks) for a given video filename.
        //              A video with N detected fish will have N tracks.
        // **************************************************
        private List<FishLens_App.Models.Video> GetAllTracks(string videoFileName, string sourceRun = null, string videoFilePath = null)
        {
            string activeRun = (Application.Current as App)?.ActiveRun ?? string.Empty;
            string effectiveRun = string.IsNullOrWhiteSpace(sourceRun) ? activeRun : sourceRun;
            string csvPath = string.IsNullOrWhiteSpace(effectiveRun)
                ? _pathResolver.ResolveCsvScriptPath()
                : (effectiveRun.Equals("debug", StringComparison.OrdinalIgnoreCase)
                    ? _pathResolver.ResolveCsvScriptPath()
                    : _pathResolver.ResolveRunCsvPath(effectiveRun));

            if (!File.Exists(csvPath))
            {
                bool isDebug = effectiveRun.Equals("debug", StringComparison.OrdinalIgnoreCase);
                if (!isDebug)
                    MessageBox.Show("Analysis data file not found.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                return new List<FishLens_App.Models.Video> { new FishLens_App.Models.Video { Name = videoFileName, VideoFilePath = videoFilePath, Run = effectiveRun } };
            }

            try
            {
                return FishLens_App.Services.CsvUtils.ReadAllTracksFromCsv(csvPath, videoFileName, effectiveRun, videoFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading tracks for {VideoFileName}", videoFileName);
                return new List<FishLens_App.Models.Video> { new FishLens_App.Models.Video { Name = videoFileName, VideoFilePath = videoFilePath, Run = effectiveRun } };
            }
        }

        #endregion

        #region Data Export

        // **************************************************
        // Function: ExportDataClick
        // Description: Shows scope picker then exports analysis data to Excel
        // **************************************************
        private void ExportDataClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // Show scope selection dialog
                string scope = ShowExportScopeDialog();
                if (scope == null) return; // user cancelled

                string csvPath = ResolveExportCsvPath(scope);
                string noFishPath = ResolveExportNoFishCsvPath(scope);

                if (!File.Exists(csvPath))
                {
                    MessageBox.Show("No analysis data found for the selected scope.", "Export Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SaveFileDialog saveFileDialog = CreateExportSaveDialog();
                if (saveFileDialog.ShowDialog() == true)
                {
                    MakeExcelSheetAndInsertData(saveFileDialog, csvPath, noFishPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting data: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // **************************************************
        // Function: ShowExportScopeDialog
        // Description: Shows a popup asking Current Session / Current Run / All History
        //              Returns the scope string or null if cancelled.
        // **************************************************
        private string ShowExportScopeDialog()
        {
            var dialog = new Window
            {
                Title = "Export Scope",
                Width = 340,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = (System.Windows.Media.Brush)Application.Current.Resources["WindowBackground"]
            };

            var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            panel.Children.Add(new TextBlock
            {
                Text = "Which data should the export include?",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["PrimaryText"],
                Margin = new Thickness(0, 0, 0, 14),
                TextWrapping = TextWrapping.Wrap
            });

            var rbSession = new RadioButton { Content = "Current Session", IsChecked = true, Margin = new Thickness(0, 0, 0, 6),
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["PrimaryText"] };
            var rbRun    = new RadioButton { Content = "Current Run",     Margin = new Thickness(0, 0, 0, 6),
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["PrimaryText"] };
            var rbAll    = new RadioButton { Content = "All History",     Margin = new Thickness(0, 0, 0, 14),
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["PrimaryText"] };

            panel.Children.Add(rbSession);
            panel.Children.Add(rbRun);
            panel.Children.Add(rbAll);

            string chosen = null;
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okBtn = new Button { Content = "Export", Width = 80, Height = 32, Margin = new Thickness(0, 0, 8, 0),
                Background = (System.Windows.Media.Brush)Application.Current.Resources["AccentBrush"],
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["OnAccentForeground"],
                BorderThickness = new Thickness(0) };
            var cancelBtn = new Button { Content = "Cancel", Width = 80, Height = 32 };
            okBtn.Click += (s, ev) => { chosen = rbAll.IsChecked == true ? "all" : (rbRun.IsChecked == true ? "run" : "session"); dialog.Close(); };
            cancelBtn.Click += (s, ev) => dialog.Close();
            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            panel.Children.Add(btnPanel);

            dialog.Content = panel;
            dialog.ShowDialog();
            return chosen;
        }

        // **************************************************
        // Function: ResolveExportCsvPath / ResolveExportNoFishCsvPath
        // Description: Returns the correct CSV path based on export scope
        // **************************************************
        private string ResolveExportCsvPath(string scope)
        {
            string activeRun = (Application.Current as App)?.ActiveRun ?? string.Empty;
            return scope switch
            {
                "all"     => _pathResolver.ResolveAllTimeMasterFishCsvPath(),
                "run"     => _pathResolver.ResolveRunCsvPath(activeRun),
                _         => _pathResolver.ResolveSessionCsvPath(activeRun)
            };
        }

        private string ResolveExportNoFishCsvPath(string scope)
        {
            string activeRun = (Application.Current as App)?.ActiveRun ?? string.Empty;
            return scope switch
            {
                "all"     => _pathResolver.ResolveSessionNoFishCsvPath(activeRun),
                "run"     => _pathResolver.ResolveSessionNoFishCsvPath(activeRun),
                _         => _pathResolver.ResolveSessionNoFishCsvPath(activeRun)
            };
        }

        // **************************************************
        // Function: CreateExportSaveDialog
        // Description: Creates configured SaveFileDialog for Excel export
        // **************************************************
        private SaveFileDialog CreateExportSaveDialog()
        {
            return new SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                FileName = $"FishLens_Analysis_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}"
            };
        }

        // **************************************************
        // Function: MakeExcelSheetAndInsertData
        // Description: Creates Excel workbook with Fish Detected, No Fish Detected, and Run Summary sheets
        // Notes: Helper function for ExportDataClick
        // **************************************************
        private void MakeExcelSheetAndInsertData(SaveFileDialog saveFileDialog, string csvPath, string noFishCsvPath = null)
        {
            string excelPath = saveFileDialog.FileName;
            string[] fishLines = File.ReadAllLines(csvPath);

            string[] noFishLines = File.Exists(noFishCsvPath)
                ? File.ReadAllLines(noFishCsvPath)
                : new[] { "video_file,location,video_timestamp" };

            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                // Sheet 1: Fish Detected
                var fishSheet = workbook.Worksheets.Add("Fish Detected");
                WriteDataToWorksheet(fishSheet, fishLines);
                FormatWorksheet(fishSheet, fishLines);

                // Sheet 2: No Fish Detected
                var noFishSheet = workbook.Worksheets.Add("No Fish Detected");
                WriteDataToWorksheet(noFishSheet, noFishLines);
                FormatWorksheet(noFishSheet, noFishLines);

                // Sheet 3: Run Summary
                var summarySheet = workbook.Worksheets.Add("Run Summary");
                BuildRunSummarySheet(summarySheet, fishLines, noFishLines);

                workbook.SaveAs(excelPath);
            }

            ShowExportSuccessMessage(excelPath);
            PromptToOpenExportedFile(excelPath);
        }

        // **************************************************
        // Function: BuildRunSummarySheet
        // Description: Populate the Run Summary sheet with fish tally and totals
        // **************************************************
        private void BuildRunSummarySheet(ClosedXML.Excel.IXLWorksheet sheet, string[] fishLines, string[] noFishLines)
        {
            int upstream = 0, downstream = 0, indecisive = 0;
            int chinookUp = 0, chinookDown = 0;
            int omykissUp = 0, omykissDown = 0;

            // fishLines[0] is header - skip it
            for (int i = 1; i < fishLines.Length; i++)
            {
                var cols = fishLines[i].Split(',');
                string dir     = cols.Length > 6 ? cols[6].Trim().ToLower() : string.Empty;
                string species = cols.Length > 2 ? cols[2].Trim().ToLower() : string.Empty;

                if (dir == "upstream")        upstream++;
                else if (dir == "downstream") downstream++;
                else                          indecisive++;

                bool isChinook = species.Contains("chinook");
                bool isOmykiss = species.Contains("omykiss");

                if (dir == "upstream")
                {
                    if (isChinook) chinookUp++;
                    else if (isOmykiss) omykissUp++;
                }
                else if (dir == "downstream")
                {
                    if (isChinook) chinookDown++;
                    else if (isOmykiss) omykissDown++;
                }
            }

            int totalFish   = fishLines.Length > 1 ? fishLines.Length - 1 : 0;
            int totalNoFish = noFishLines.Length > 1 ? noFishLines.Length - 1 : 0;
            int net         = upstream - downstream;
            int chinookNet  = chinookUp - chinookDown;
            int omykissNet  = omykissUp - omykissDown;

            var rows = new[]
            {
                new[] { "Metric",                           "Value" },
                new[] { "Total Fish Detected",              totalFish.ToString() },
                new[] { "Total No-Fish Videos",             totalNoFish.ToString() },
                new[] { "Upstream",                         upstream.ToString() },
                new[] { "Downstream",                       downstream.ToString() },
                new[] { "Indecisive",                       indecisive.ToString() },
                new[] { "Chinook Net Upstream",             chinookNet.ToString() },
                new[] { "Omykiss Net Upstream",             omykissNet.ToString() },
                new[] { "Net Upstream Count (All Species)", net.ToString() },
                new[] { "Export Date",                      DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
            };

            for (int r = 0; r < rows.Length; r++)
            {
                sheet.Cell(r + 1, 1).Value = rows[r][0];
                sheet.Cell(r + 1, 2).Value = rows[r][1];
            }

            // Header styling
            sheet.Row(1).Style.Font.Bold = true;
            sheet.Row(1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightBlue;
            // Highlight the three net rows
            sheet.Row(7).Style.Font.Bold = true;
            sheet.Row(8).Style.Font.Bold = true;
            sheet.Row(9).Style.Font.Bold = true;
            sheet.Columns().AdjustToContents();
        }

        // **************************************************
        // Function: WriteDataToWorksheet
        // Description: Writes CSV data to Excel worksheet
        // **************************************************
        private void WriteDataToWorksheet(ClosedXML.Excel.IXLWorksheet worksheet, string[] allLines)
        {
            // Confidence columns (0-based): 3 = species_confidence, 5 = confidence
            var confColumns = new HashSet<int> { 3, 5 };

            for (int line = 0; line < allLines.Length; line++)
            {
                string[] columns = allLines[line].Split(',');
                for (int column = 0; column < columns.Length; column++)
                {
                    string raw = columns[column].Trim();
                    // Header row or non-numeric: write as-is
                    if (line == 0 || !confColumns.Contains(column))
                    {
                        worksheet.Cell(line + 1, column + 1).Value = raw;
                        continue;
                    }
                    // Convert stored decimal (0.9377) or legacy percent (93.77%) to display percent
                    string clean = raw.TrimEnd('%');
                    if (double.TryParse(clean, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double val))
                    {
                        if (val <= 1.0) val *= 100.0;  // decimal -> percent
                        worksheet.Cell(line + 1, column + 1).Value = $"{val:F2}%";
                    }
                    else
                    {
                        worksheet.Cell(line + 1, column + 1).Value = raw;
                    }
                }
            }
        }

        // **************************************************
        // Function: FormatWorksheet
        // Description: Applies formatting to Excel worksheet
        // **************************************************
        private void FormatWorksheet(ClosedXML.Excel.IXLWorksheet worksheet, string[] allLines)
        {
            if (allLines.Length > 0)
            {
                var headerRow = worksheet.Row(1);
                headerRow.Style.Font.Bold = true;
                headerRow.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightBlue;
            }

            worksheet.Columns().AdjustToContents();
        }

        // **************************************************
        // Function: ShowExportSuccessMessage
        // Description: Displays success message after export
        // **************************************************
        private void ShowExportSuccessMessage(string excelPath)
        {
            MessageBox.Show($"Data exported successfully to:\n{excelPath}", "Export Successful",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // **************************************************
        // Function: PromptToOpenExportedFile
        // Description: Asks user if they want to open the exported file
        // **************************************************
        private void PromptToOpenExportedFile(string excelPath)
        {
            var result = MessageBox.Show("Would you like to open the exported file?", "Open File",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(excelPath) { UseShellExecute = true });
            }
        }

        // **************************************************
        // Function: RefreshSessionOverview
        // Description: Recalculates and displays net upstream fish count from current CSV
        // **************************************************
        private void RefreshSessionOverview()
        {
            string sourceRun = _currentTracks.Count > 0
                ? _currentTracks[_currentTrackIndex].Run
                : (Application.Current as App)?.ActiveRun ?? string.Empty;
            string location = _currentTracks.Count > 0
                ? _currentTracks[_currentTrackIndex].Location
                : (locationDropdown.SelectedItem as string ?? "--");
            sessionRunText.Text = $"Run: {(string.IsNullOrWhiteSpace(sourceRun) ? "--" : sourceRun)}";
            sessionLocationText.Text = $"Location: {location}";

            string csvPath = string.IsNullOrWhiteSpace(sourceRun)
                ? _pathResolver.ResolveCsvScriptPath()
                : _pathResolver.ResolveRunCsvPath(sourceRun);
            if (!File.Exists(csvPath))
            {
                sessionNetUpstreamText.Text = "Net Upstream: --";
                return;
            }

            int upstreamCount = 0;
            int downstreamCount = 0;
            var lines = File.ReadAllLines(csvPath);
            for (int i = 1; i < lines.Length; i++)
            {
                var cols = lines[i].Split(',');
                if (cols.Length <= 6) continue;
                // Filter to the currently selected location (col 1)
                if (cols.Length > 1 && !string.Equals(cols[1].Trim(), location, StringComparison.OrdinalIgnoreCase)) continue;
                string likelyClass = cols[4].Trim().ToLower();
                if (likelyClass == "bird" || likelyClass == "no_fish" || likelyClass == "n/a") continue;
                string direction = cols[6].Trim().ToLower();
                if (direction == "upstream") upstreamCount++;
                else if (direction == "downstream") downstreamCount++;
            }

            int net = upstreamCount - downstreamCount;
            sessionNetUpstreamText.Text = $"Net Upstream: {net}";
        }

        // **************************************************
        // Function: SaveButtonClick
        // Description: Saves user modifications to CSV file
        // **************************************************
        private void SaveButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string currentVideoName = videoName.Text;

                if (string.IsNullOrEmpty(currentVideoName) || currentVideoName == "--")
                {
                    MessageBox.Show("No video selected to save changes for.", "Save Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Identify the specific track to save by its start_time_sec.
                // This ensures edits to Fish 2 don't overwrite Fish 1's row.
                var currentTrack = (_currentTracks.Count > _currentTrackIndex)
                    ? _currentTracks[_currentTrackIndex]
                    : null;
                string startTimeSec = currentTrack?.StartTime ?? string.Empty;
                string sourceRun = currentTrack?.Run;
                if (string.IsNullOrWhiteSpace(sourceRun))
                    sourceRun = (Application.Current as App)?.ActiveRun ?? string.Empty;

                // run_master.csv and all_history.csv are written together during analysis
                // and must stay in sync - the save must succeed in both.
                string runMasterPath = _pathResolver.ResolveRunCsvPath(sourceRun);
                if (!File.Exists(runMasterPath) || !UpdateCsvFile(runMasterPath, currentTrack, currentVideoName, startTimeSec))
                {
                    MessageBox.Show(
                        "This track was not found in the run master CSV. No changes were saved.",
                        "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string allHistoryPath = _pathResolver.ResolveAllTimeMasterFishCsvPath();
                if (File.Exists(allHistoryPath))
                    UpdateCsvFile(allHistoryPath, currentTrack, currentVideoName, startTimeSec);

                // session_fish.csv is best-effort - only populated for the current session,
                // so prior-session videos won't be present. Silently skip if row is absent.
                string sessionPath = _pathResolver.ResolveSessionCsvPath(sourceRun);
                if (File.Exists(sessionPath))
                    UpdateCsvFile(sessionPath, currentTrack, currentVideoName, startTimeSec);

                if (currentTrack != null)
                    currentTrack.Run = sourceRun;
                RefreshSessionOverview();
                MessageBox.Show("Changes saved successfully!", "Save Successful",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving changes to CSV");
                MessageBox.Show($"Error saving changes: {ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // **************************************************
        // Function: UpdateCsvFile
        // Description: Updates exactly the current track's CSV row (identified by start_time_sec).
        //              Returns true if the row was found and updated, false if not present in this file.
        // **************************************************
        private bool UpdateCsvFile(string csvPath, FishLens_App.Models.Video track, string videoFileName, string startTimeSec)
        {
            EnsureCsvHasRunColumn(csvPath, track?.Run ?? string.Empty);
            string[] lines = File.ReadAllLines(csvPath);
            string[] columns = null;
            string trackRun = track?.Run ?? string.Empty;
            string trackPath = track?.VideoFilePath ?? string.Empty;
            for (int i = 1; i < lines.Length; i++)
            {
                var cols = lines[i].Split(',');
                bool nameMatch = cols.Length > 0 &&
                    string.Equals(Path.GetFileName(cols[0].Trim()), videoFileName,
                        StringComparison.OrdinalIgnoreCase);
                bool timeMatch = cols.Length > 7 &&
                    string.Equals(cols[7].Trim(), startTimeSec, StringComparison.OrdinalIgnoreCase);
                bool runMatch = string.IsNullOrWhiteSpace(trackRun) ||
                    (cols.Length > 10 && string.Equals(cols[10].Trim(), trackRun, StringComparison.OrdinalIgnoreCase));
                bool pathMatch = string.IsNullOrWhiteSpace(trackPath) ||
                    (cols.Length > 0 && string.Equals(cols[0].Trim(), trackPath, StringComparison.OrdinalIgnoreCase));
                if (nameMatch && timeMatch && runMatch && pathMatch)
                {
                    columns = cols;
                    break;
                }
            }

            // Row not present in this file - skip silently (e.g. session_fish.csv for old-run data)
            if (columns == null) return false;

            string updatedRow = CreateUpdatedCsvRow(columns);
            return FishLens_App.Services.CsvUtils.UpdateCsvRowForTrack(csvPath, videoFileName, startTimeSec, updatedRow, trackRun);
        }

        // **************************************************
        // Function: EnsureCsvHasRunColumn
        // Description: Upgrades older 10-column CSVs in place by appending a run column.
        //              Existing rows keep blank run values unless a default run is provided.
        // **************************************************
        private void EnsureCsvHasRunColumn(string csvPath, string defaultRun)
        {
            if (!File.Exists(csvPath)) return;

            var lines = File.ReadAllLines(csvPath).ToList();
            if (lines.Count == 0) return;

            var headerCols = lines[0].Split(',');
            if (headerCols.Length > 10 && string.Equals(headerCols[10].Trim(), "run", StringComparison.OrdinalIgnoreCase))
                return;

            if (headerCols.Length == 10)
                lines[0] = $"{lines[0]},run";

            bool backfillRun = !string.Equals(Path.GetFileName(csvPath), "all_history.csv", StringComparison.OrdinalIgnoreCase);

            for (int i = 1; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var cols = lines[i].Split(',');
                if (cols.Length >= 11) continue;
                lines[i] = $"{lines[i]},{(backfillRun ? defaultRun : string.Empty)}";
            }

            File.WriteAllLines(csvPath, lines);
        }

        // CSV removal moved to CsvUtils for reuse and testability

        // **************************************************
        // Function: CreateUpdatedCsvRow
        // Description: Creates updated CSV row from UI values
        // **************************************************
        private string CreateUpdatedCsvRow(string[] originalColumns)
        {
            // Get values from UI controls
            string likelyClass = GetFishPresentClass();
            string direction = GetTravelDirectionValue();
            string species = fishSpecies.Text.Trim();

            // Keep original values for fields not editable in UI
            string videoFile = originalColumns[0].Trim();
            string location = originalColumns.Length > 1 ? originalColumns[1].Trim() : string.Empty;
            string confidence = originalColumns.Length > 5 ? originalColumns[5].Trim() : string.Empty;
            string species_confidence = originalColumns.Length > 3 ? originalColumns[3].Trim() : string.Empty;
            string startTime = originalColumns.Length > 7 ? originalColumns[7].Trim() : string.Empty;
            string endTime = originalColumns.Length > 8 ? originalColumns[8].Trim() : string.Empty;
            string vidTimeStamp = originalColumns.Length > 9 ? originalColumns[9].Trim() : string.Empty;
            string run = originalColumns.Length > 10 ? originalColumns[10].Trim() : string.Empty;

            // Read and validate confidence values from UI TextBoxes
            // fishPresentConfidence is displayed as percentage (e.g., "88.00%")
            // confidence in CSV is stored as decimal (e.g., 0.88)
            string presentConfText = fishPresentConfidence.Text.Trim();
            if (!string.IsNullOrEmpty(presentConfText) && presentConfText != "--")
            {
                // Remove % sign and convert percentage back to decimal
                string cleanValue = presentConfText.Replace("%", "").Trim();
                if (double.TryParse(cleanValue, out double presentConfValue))
                {
                    // Convert from percentage (0-100) back to decimal (0-1)
                    confidence = (presentConfValue / 100).ToString("F4");
                }
            }

            // fishSpeciesConfidence is displayed as percentage (e.g., "92.45%")
            // species_confidence in CSV is stored as decimal (e.g., 0.9245)
            string speciesConfText = fishSpeciesConfidence.Text.Trim();
            if (!string.IsNullOrEmpty(speciesConfText) && speciesConfText != "--")
            {
                // Remove % sign and convert percentage back to decimal
                string cleanValue = speciesConfText.Replace("%", "").Trim();
                if (double.TryParse(cleanValue, out double speciesConfValue))
                {
                    // Convert from percentage (0-100) back to decimal (0-1)
                    species_confidence = (speciesConfValue / 100).ToString("F4");
                }
            }

            // Build the CSV row
            return $"{videoFile},{location},{species},{species_confidence},{likelyClass},{confidence},{direction},{startTime},{endTime},{vidTimeStamp},{run}";
        }

        // **************************************************
        // Function: GetFishPresentClass
        // Description: Converts UI fish present status to CSV class value
        // **************************************************
        private string GetFishPresentClass()
        {
            var selectedItem = fishPresentStatus.SelectedItem as ComboBoxItem;

            if (selectedItem == null)
                return "not_fish";

            string status = selectedItem.Content.ToString();
            return status == "Present" ? "fish" : "not_fish";
        }

        // **************************************************
        // Function: GetTravelDirectionValue
        // Description: Gets travel direction value from UI
        // **************************************************
        private string GetTravelDirectionValue()
        {
            var selectedItem = fishTravelDirection.SelectedItem as ComboBoxItem;

            if (selectedItem == null)
            {
                return "unknown";
            }

            return selectedItem.Content.ToString().ToLower();
        }

        #endregion

        #region UI Display

        private void ExpandSidebar()
        {
            _sidebarCollapsed = false;
            // Kill any in-progress animation first
            SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
            SideBar.RenderTransform = Transform.Identity;

            // Fade in videoLibraryTitle
            videoLibraryTitle.Opacity = 0;
            videoLibraryTitle.Visibility = Visibility.Visible;
            var titleFadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                BeginTime = TimeSpan.FromMilliseconds(150),
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            videoLibraryTitle.BeginAnimation(UIElement.OpacityProperty, titleFadeIn);

            SidebarDivider.Visibility = Visibility.Visible;
            videoList.Visibility = Visibility.Visible;
            deleteSelectedVideos.Visibility = Visibility.Visible;
            changeLocationForSelected.Visibility = Visibility.Visible;
            undoLastDelete.Visibility = Visibility.Visible;

            if (App.IsAnalyzing)
                analysisProgressArea.Visibility = Visibility.Visible;

            ButtonGrid.RowDefinitions.Clear();
            ButtonGrid.ColumnDefinitions.Clear();
            ButtonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ButtonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ButtonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ButtonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(Home, 0); Grid.SetColumn(Home, 0);
            Grid.SetRow(History, 0); Grid.SetColumn(History, 1);
            Grid.SetRow(Settings, 0); Grid.SetColumn(Settings, 2);
            Grid.SetRow(AccountSettingsButton, 0); Grid.SetColumn(AccountSettingsButton, 3);

            var expandAnim = new GridLengthAnimation
            {
                From = new GridLength(SidebarColumn.ActualWidth),
                To = new GridLength(320),
                Duration = TimeSpan.FromMilliseconds(280),
                EasingMode = EasingMode.EaseOut,
                FillBehavior = FillBehavior.Stop
            };
            expandAnim.Completed += (s, e) => SidebarColumn.Width = new GridLength(320);
            SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, expandAnim);
        }

        private void CollapseSidebar()
        {
            if (!_sidebarCollapsed)
            {
                _sidebarCollapsed = true;
                // Fade out videoLibraryTitle before/during collapse
                var titleFadeOut = new DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(100),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                titleFadeOut.Completed += (s, e) => videoLibraryTitle.Visibility = Visibility.Collapsed;
                videoLibraryTitle.BeginAnimation(UIElement.OpacityProperty, titleFadeOut);

                // Kill any in-progress animation first
                SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);

                var collapseAnim = new GridLengthAnimation
                {
                    From = new GridLength(SidebarColumn.ActualWidth),
                    To = new GridLength(106),
                    Duration = TimeSpan.FromMilliseconds(250),
                    EasingMode = EasingMode.EaseInOut,
                    FillBehavior = FillBehavior.HoldEnd   // hold until we set the hard value
                };

                collapseAnim.Completed += (s, e) =>
                {
                    // Remove animation and set hard value in one step
                    SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
                    SidebarColumn.Width = new GridLength(106);

                    videoList.Visibility = Visibility.Collapsed;
                    deleteSelectedVideos.Visibility = Visibility.Collapsed;
                    changeLocationForSelected.Visibility = Visibility.Collapsed;
                    undoLastDelete.Visibility = Visibility.Collapsed;
                    analysisProgressArea.Visibility = Visibility.Collapsed;
                    SidebarDivider.Visibility = Visibility.Collapsed;

                    ButtonGrid.HorizontalAlignment = HorizontalAlignment.Center;
                    ButtonGrid.Width = double.NaN;

                    ButtonGrid.RowDefinitions.Clear();
                    ButtonGrid.ColumnDefinitions.Clear();
                    ButtonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    ButtonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    ButtonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    ButtonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    Grid.SetRow(Home, 0); Grid.SetColumn(Home, 0);
                    Grid.SetRow(History, 1); Grid.SetColumn(History, 0);
                    Grid.SetRow(Settings, 2); Grid.SetColumn(Settings, 0);
                    Grid.SetRow(AccountSettingsButton, 3); Grid.SetColumn(AccountSettingsButton, 0);
                };

                SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, collapseAnim);
            }
        }

        /// <summary>
        /// Updates a ring arc Path to represent <paramref name="confidence"/> (0–100).
        /// The arc sits on a circle of radius 17 centred at (22,22).
        /// </summary>
        private static void SetRingArc(System.Windows.Shapes.Path arc, double confidence)
        {
            if (confidence <= 0) { arc.Data = Geometry.Empty; return; }

            double pct = Math.Min(confidence / 100.0, 0.9999); // avoid full-circle edge case
            double angle = pct * 360.0 - 90.0;                   // start from 12 o'clock
            double rad = angle * Math.PI / 180.0;
            double cx = 22, cy = 22, r = 17;
            double ex = cx + r * Math.Cos(rad);
            double ey = cy + r * Math.Sin(rad);
            int large = pct >= 0.5 ? 1 : 0;

            arc.Data = Geometry.Parse(
                $"M {cx},{cy - r} A {r},{r},0,{large},1,{ex:F2},{ey:F2}");
        }

        // Call this wherever you populate the analysis fields, e.g.:
        // SetRingArc(fishPresentRingArc,  fishPresentConfidenceValue);   // 0–100
        // SetRingArc(fishSpeciesRingArc,  fishSpeciesConfidenceValue);
        // fishPresentConfidence.Text  = $"{fishPresentConfidenceValue:0}%";
        // fishSpeciesConfidence.Text  = $"{fishSpeciesConfidenceValue:0}%";

        // **************************************************
        // Function: VideoButtonClick
        // Description: Displays selected video and its data
        // **************************************************
        private void VideoButtonClick(object sender, RoutedEventArgs e)
        {
            Button clickedButton = (Button)sender;
            string videoPath = clickedButton.Tag.ToString();
            var sourceVideo = clickedButton.DataContext as FishLens_App.Models.Video;

            // Load data first so fish markers are set before MediaOpened fires
            string videoFileName = Path.GetFileName(videoPath);
            string sourceRun = sourceVideo?.Run;
            var data = GetData(videoFileName, videoPath, sourceRun);
            if (data != null)
            {
                DisplayDataInUi(videoFileName, data.Run, videoPath);
            }

            LoadVideoInPlayer(videoPath);
        }


        // **************************************************
        // Function: LoadVideoInPlayer
        // Description: Loads video into media player with auto-play preference
        // **************************************************
        // **************************************************
        // Function: CleanupPlaybackTemp
        // Description: Deletes the temporary MP4 created for ASF scrubbing, if any
        // **************************************************
        private void CleanupPlaybackTemp()
        {
            if (!string.IsNullOrEmpty(_playbackTempPath) && File.Exists(_playbackTempPath))
            {
                try { File.Delete(_playbackTempPath); } catch { }
            }
            _playbackTempPath = null;
        }

        // **************************************************
        // Function: ConvertAsfToTempMp4
        // Description: Converts an ASF file to a temporary MP4 using ffmpeg for accurate scrubbing.
        //              Returns the temp path on success, or the original path if conversion fails.
        // **************************************************
        private string ConvertAsfToTempMp4(string asfPath)
        {
            string ffmpeg = null;
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                var candidate = System.IO.Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate)) { ffmpeg = candidate; break; }
            }
            if (ffmpeg == null) return asfPath; // ffmpeg not found - use original

            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"fishlens_play_{System.IO.Path.GetFileNameWithoutExtension(asfPath)}_{System.Guid.NewGuid():N}.mp4");
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = $"-hide_banner -loglevel error -y -i \"{asfPath}\" -c:v libx264 -preset ultrafast -crf 23 \"{tempPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                proc.WaitForExit(30_000); // 30s max
                if (proc.ExitCode == 0 && File.Exists(tempPath) && new FileInfo(tempPath).Length > 0)
                    return tempPath;
            }
            catch { }
            // Conversion failed - clean up and fall back to original
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            return asfPath;
        }

        private void LoadVideoInPlayer(string videoPath)
        {
            // Stop and reset timer before loading new video
            _videoTimer?.Stop();
            _isPlaying = false;

            // For ASF files, convert to a temp MP4 so that MediaElement seeking is accurate.
            // ASF (WMV) only supports keyframe-level seeks; the temp MP4 allows sample-accurate
            // scrubbing. The temp file is deleted when the next video loads or the app closes.
            CleanupPlaybackTemp();
            if (videoPath.EndsWith(".asf", StringComparison.OrdinalIgnoreCase) ||
                videoPath.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase))
            {
                string converted = ConvertAsfToTempMp4(videoPath);
                if (!string.Equals(converted, videoPath, StringComparison.OrdinalIgnoreCase))
                    _playbackTempPath = converted;
                videoPath = converted;
            }

            videoPlayer.Source = new Uri(videoPath);
            videoPlayer.Play();
            _isPlaying = true;
            playPauseButton.Content = "\u23F8";

            // Show controls, hide placeholder
            placeholderPanel.Visibility = Visibility.Collapsed;
            videoControls.Visibility = Visibility.Visible;

            // Reset scrubber and time only - fish markers are redrawn from _currentTracks by UpdateFishMarkers()
            videoScrubber.Value = 0;
            videoCurrentTimeText.Text = "0:00";
            videoTotalTimeText.Text = "0:00";
        }

        // **************************************************
        // Function: VideoPlayer_MediaOpened
        // Description: Called when MediaElement has opened and knows the duration
        // **************************************************
        private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            // Set total time label
            if (videoPlayer.NaturalDuration.HasTimeSpan)
                videoTotalTimeText.Text = FormatTime(videoPlayer.NaturalDuration.TimeSpan);

            // Start the position-update timer
            if (_videoTimer == null)
            {
                _videoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                _videoTimer.Tick += VideoTimer_Tick;
            }
            _videoTimer.Start();
            UpdateFishMarkers();
        }

        // **************************************************
        // Function: VideoPlayer_MediaEnded
        // Description: Resets controls when video finishes
        // **************************************************
        private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            _videoTimer?.Stop();
            _isPlaying = false;
            playPauseButton.Content = "\u25B6";
            videoPlayer.Stop();
            videoScrubber.Value = 0;
            videoCurrentTimeText.Text = "0:00";
        }

        // **************************************************
        // Function: VideoTimer_Tick
        // Description: Updates scrubber and time readout while playing
        // **************************************************
        private void VideoTimer_Tick(object sender, EventArgs e)
        {
            if (_isDraggingScrubber) return;
            // Suppress ticks briefly after a seek so the timer doesn't jump back to pre-seek position
            if (_suppressTimerTicks > 0) { _suppressTimerTicks--; return; }
            if (!videoPlayer.NaturalDuration.HasTimeSpan) return;

            double total = videoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            double pos   = videoPlayer.Position.TotalSeconds;
            if (total > 0)
                videoScrubber.Value = pos / total;

            videoCurrentTimeText.Text = FormatTime(videoPlayer.Position);
        }

        // **************************************************
        // Function: PlayPauseButton_Click
        // Description: Toggles play/pause on the video
        // **************************************************
        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlaying)
            {
                videoPlayer.Pause();
                _isPlaying = false;
                playPauseButton.Content = "\u25B6";
            }
            else
            {
                videoPlayer.Play();
                _isPlaying = true;
                playPauseButton.Content = "\u23F8";
                _videoTimer?.Start(); // restart timer if it was stopped by MediaEnded
            }
        }

        // **************************************************
        // Function: SkipBackButton_Click / SkipForwardButton_Click
        // Description: Skip video position by +/-1 second
        // **************************************************
        private void SkipBackButton_Click(object sender, RoutedEventArgs e) => SkipSeconds(-1.0);
        private void SkipForwardButton_Click(object sender, RoutedEventArgs e) => SkipSeconds(1.0);

        private void SkipSeconds(double seconds)
        {
            if (!videoPlayer.NaturalDuration.HasTimeSpan) return;
            double total = videoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            double newPos = Math.Max(0.0, Math.Min(total, videoPlayer.Position.TotalSeconds + seconds));
            videoPlayer.Position = TimeSpan.FromSeconds(newPos);
            videoScrubber.Value = total > 0 ? newPos / total : 0;
            videoCurrentTimeText.Text = FormatTime(TimeSpan.FromSeconds(newPos));
            _suppressTimerTicks = 3;
        }

        // **************************************************
        // Function: Window_PreviewKeyDown
        // Description: Space = play/pause, Left/Right arrow = skip +/-1s.
        //              Only active when video controls are visible.
        // **************************************************
        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (videoControls.Visibility != Visibility.Visible) return;

            switch (e.Key)
            {
                case System.Windows.Input.Key.Space:
                    PlayPauseButton_Click(sender, e);
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.Left:
                    SkipSeconds(-1.0);
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.Right:
                    SkipSeconds(1.0);
                    e.Handled = true;
                    break;
            }
        }

        // **************************************************
        // Function: VideoScrubber_PreviewMouseDown/Up
        // Description: Pauses timer updates while user drags the scrubber
        // **************************************************
        private void VideoScrubber_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isDraggingScrubber = true;
            if (_isPlaying) videoPlayer.Pause();
            SeekToMousePosition(e);
            e.Handled = true;
        }

        private void VideoScrubber_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDraggingScrubber || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
            SeekToMousePosition(e);
        }

        private void VideoScrubber_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_isDraggingScrubber) return;
            SeekToMousePosition(e);
            _isDraggingScrubber = false;
            _suppressTimerTicks = 3;
            if (_isPlaying) videoPlayer.Play();
            e.Handled = true;
        }

        private void SeekToMousePosition(System.Windows.Input.MouseEventArgs e)
        {
            if (!videoPlayer.NaturalDuration.HasTimeSpan) return;
            double x = e.GetPosition(videoScrubber).X;
            double w = videoScrubber.ActualWidth;
            const double thumbHalf = 7.0; // custom thumb is 14px wide
            double trackW = Math.Max(1.0, w - 2 * thumbHalf);
            double ratio = Math.Max(0.0, Math.Min(1.0, (x - thumbHalf) / trackW));
            videoScrubber.Value = ratio;
            double total = videoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            videoPlayer.Position = TimeSpan.FromSeconds(ratio * total);
            videoCurrentTimeText.Text = FormatTime(videoPlayer.Position);
        }

        // **************************************************
        // Function: VideoScrubber_ValueChanged
        // Description: Seeks when user clicks a new position on the scrubber
        // **************************************************
        private void VideoScrubber_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isDraggingScrubber) return;
            if (!videoPlayer.NaturalDuration.HasTimeSpan) return;

            double total = videoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            videoPlayer.Position = TimeSpan.FromSeconds(videoScrubber.Value * total);
            videoCurrentTimeText.Text = FormatTime(videoPlayer.Position);
        }

        // **************************************************
        // Function: FormatTime
        // Description: Formats a TimeSpan as m:ss
        // **************************************************
        private static string FormatTime(TimeSpan t)
        {
            return $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
        }

        // **************************************************
        // Function: UpdateFishMarkers
        // Description: Draws one colored range bar per fish track onto the scrubber canvas,
        //              and one directional emoji per track above it.
        //              The active track is full opacity; inactive tracks are 50% opacity.
        //              Colors: upstream=#2AB5B5 teal, downstream=#E05C5C coral, indecisive=#E8A038 amber.
        //              The displayed markers use the escaped emoji strings defined below.
        // **************************************************
        private void UpdateFishMarkers()
        {
            if (_currentTracks.Count == 0) return;
            if (!videoPlayer.NaturalDuration.HasTimeSpan) return;
            double total = videoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            if (total <= 0) return;

            var canvas = videoScrubber.Template?.FindName("fishMarkersCanvas", videoScrubber) as Canvas;
            if (canvas == null)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateFishMarkers));
                return;
            }

            double w = canvas.ActualWidth;
            if (w < 10)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateFishMarkers));
                return;
            }

            // Clear previous bars and emojis
            canvas.Children.Clear();
            fishEmojiCanvas.Children.Clear();

            const double thumbHalf = 7.0;
            double trackW = Math.Max(1.0, w - 2 * thumbHalf);

            for (int i = 0; i < _currentTracks.Count; i++)
            {
                var track = _currentTracks[i];
                bool isActive = (i == _currentTrackIndex);
                double opacity = isActive ? 1.0 : 0.5;

                if (!double.TryParse(track.StartTime, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double fStart)) continue;
                if (!double.TryParse(track.EndTime, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double fEnd)) continue;

                string dir = (track.Direction ?? string.Empty).ToLowerInvariant();
                var color = dir switch
                {
                    "upstream"   => System.Windows.Media.Color.FromRgb(0x2A, 0xB5, 0xB5), // teal
                    "downstream" => System.Windows.Media.Color.FromRgb(0xE0, 0x5C, 0x5C), // coral
                    _            => System.Windows.Media.Color.FromRgb(0xE8, 0xA0, 0x38), // amber
                };

                double startX = thumbHalf + (fStart / total) * trackW;
                double endX   = thumbHalf + (fEnd   / total) * trackW;
                double barW   = Math.Max(4.0, endX - startX);

                // Range bar
                var bar = new System.Windows.Shapes.Rectangle
                {
                    Height  = 4,
                    Width   = barW,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill    = new System.Windows.Media.SolidColorBrush(color),
                    Opacity = opacity,
                };
                Canvas.SetLeft(bar, startX);
                canvas.Children.Add(bar);

                // Directional emoji above the bar
                if (barW >= 14)
                {
                    string emoji = dir switch
                    {
                        "upstream"   => "\u25C0\U0001F41F",
                        "downstream" => "\U0001F41F\u25B6",
                        _            => "\u2194\U0001F41F",
                    };
                    double midX      = startX + barW / 2.0;
                    double emojiLeft = Math.Max(thumbHalf, Math.Min(w - thumbHalf - 22, midX - 11));
                    var tb = new TextBlock
                    {
                        Text     = emoji,
                        FontSize = 11,
                        Opacity  = opacity,
                        Margin   = new Thickness(0),
                    };
                    Canvas.SetLeft(tb, emojiLeft);
                    Canvas.SetTop(tb, 0);
                    fishEmojiCanvas.Children.Add(tb);
                }
            }
        }

        // **************************************************
        // Function: DisplayDataInUi
        // Description: Loads all tracks for a video and displays the first one
        // **************************************************
        private void DisplayDataInUi(string videoFileName, string sourceRun = null, string videoFilePath = null)
        {
            _currentTracks = GetAllTracks(videoFileName, sourceRun, videoFilePath);
            _currentTrackIndex = 0;
            DisplayTrackInUi(_currentTracks[0]);
            UpdateTrackNavigator();
        }

        // **************************************************
        // Function: DisplayTrackInUi
        // Description: Populates the analysis panel from a single Video/track object.
        //              Called by DisplayDataInUi (initial load) and by the track navigator (7.2).
        // **************************************************
        private void DisplayTrackInUi(FishLens_App.Models.Video vid)
        {
            // Location fallback for no-fish rows (no-fish CSVs don't appear in run_master)
            string location = vid.Location;
            if (string.IsNullOrWhiteSpace(location))
            {
                string sourceRun = string.IsNullOrWhiteSpace(vid.Run)
                    ? (Application.Current as App)?.ActiveRun ?? string.Empty
                    : vid.Run;
                string noFishCsvPath = _pathResolver.ResolveSessionNoFishCsvPath(sourceRun);
                location = FishLens_App.Services.CsvUtils.ReadLocationFromNoFishCsv(noFishCsvPath, vid.Name);
            }

            videoName.Text = vid.Name;
            videoLocation.Text = string.IsNullOrWhiteSpace(location) ? "--" : location;
            videoDateTime.Text = $"Duration: {vid.StartTime}s - {vid.EndTime}s";
            // likely_class can be "fish", a species name ("chinook"), "not_fish", or "no_fish".
            // Anything other than not_fish/no_fish means a fish was detected.
            bool fishPresent = vid.LikelyClass != "not_fish" && vid.LikelyClass != "no_fish";
            fishPresentStatus.SelectedIndex = fishPresent ? 0 : 1;
            fishPresentConfidence.Text = $"{Math.Round(vid.AvgConfidence * 100)}%";
            string dirLower = (vid.Direction ?? string.Empty).ToLower().Trim();
            fishTravelDirection.SelectedIndex = dirLower == "upstream" ? 0 : dirLower == "downstream" ? 1 : 2;
            fishSpecies.Text = CapitalizeFirstLetter(vid.Species);
            fishSpeciesConfidence.Text = vid.SpeciesConfidence > 0 ? $"{vid.SpeciesConfidence * 100:F2}%" : "--";

            // Refresh all track markers on the scrubber (opacity highlights the active one)
            UpdateFishMarkers();
        }

        // **************************************************
        // Function: UpdateTrackNavigator
        // Description: Refreshes the track navigator label and shows/hides it
        // **************************************************
        private void UpdateTrackNavigator()
        {
            int total = _currentTracks.Count;
            bool multi = total > 1;

            trackNavigator.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
            prevFishButton.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
            nextFishButton.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;

            if (!multi) return;

            trackLabel.Text = $"Fish {_currentTrackIndex + 1} / {total}";
            trackVideoLabel.Text = _currentTracks[_currentTrackIndex].Name;
            trackPrevButton.IsEnabled = _currentTrackIndex > 0;
            trackNextButton.IsEnabled = _currentTrackIndex < total - 1;
            prevFishButton.IsEnabled  = _currentTrackIndex > 0;
            nextFishButton.IsEnabled  = _currentTrackIndex < total - 1;
        }

        // **************************************************
        // Function: TrackPrevClick / TrackNextClick
        // Description: Navigate backwards/forwards through fish tracks for the current video.
        //              Also seeks the video to that track's start time (7.4).
        // **************************************************
        private void TrackPrevClick(object sender, RoutedEventArgs e)
        {
            if (_currentTrackIndex > 0)
            {
                _currentTrackIndex--;
                DisplayTrackInUi(_currentTracks[_currentTrackIndex]);
                UpdateTrackNavigator();
                SeekToCurrentTrack();
            }
        }

        private void TrackNextClick(object sender, RoutedEventArgs e)
        {
            if (_currentTrackIndex < _currentTracks.Count - 1)
            {
                _currentTrackIndex++;
                DisplayTrackInUi(_currentTracks[_currentTrackIndex]);
                UpdateTrackNavigator();
                SeekToCurrentTrack();
            }
        }

        // Transport-bar fish jump buttons - same logic as track navigator arrows
        private void PrevFishButton_Click(object sender, RoutedEventArgs e) => TrackPrevClick(sender, e);
        private void NextFishButton_Click(object sender, RoutedEventArgs e) => TrackNextClick(sender, e);

        // **************************************************
        // Function: SeekToCurrentTrack
        // Description: Seeks the video player to the start time of the currently selected track
        // **************************************************
        private void SeekToCurrentTrack()
        {
            if (_currentTracks.Count == 0) return;
            var track = _currentTracks[_currentTrackIndex];
            if (double.TryParse(track.StartTime, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double startSec))
            {
                videoPlayer.Position = TimeSpan.FromSeconds(startSec);
                _suppressTimerTicks = 2;
            }
        }

        // **************************************************
        // Function: CapitalizeFirstLetter
        // Description: Capitalizes the first letter of a string
        // **************************************************
        private string CapitalizeFirstLetter(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return char.ToUpper(text[0]) + text.Substring(1);
        }

        // **************************************************
        // Function: CreateVideoButtonsList
        // Description: Creates and adds buttons for all videos to sidebar
        // **************************************************
        private void CreateVideoButtonsList(List<(FileInfo videoFile, FishLens_App.Models.Video videoData)> videoDataList)
        {
            if (videoDataList.Count == 0) return;

            string sectionRun = ResolveVideoRun(videoDataList[0].videoData);
            var sectionContext = CreateLibrarySectionContext(videoDataList[0].videoFile.FullName, sectionRun);
            CreateFolderHeader(sectionContext, GetSectionDisplayText(sectionContext, videoDataList.Select(x => x.videoData)));
            CreateVideoButtons(videoDataList, sectionContext);
        }

        // **************************************************
        // Function: CreateFolderHeader
        // Description: Creates folder name display with checkbox and separator
        // **************************************************
        private void CreateFolderHeader(LibrarySectionContext sectionContext, string displayText = null)
        {
            Grid folderNameGrid = new Grid();
            folderNameGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
            folderNameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            folderNameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            folderNameGrid.DataContext = sectionContext;

            // Folder name (includes run and location context)
            TextBox textBox = new TextBox();
            textBox.Text = displayText ?? GetSectionDisplayText(sectionContext);
            textBox.Foreground = new SolidColorBrush(Colors.White);
            textBox.Background = Brushes.Transparent;
            textBox.BorderThickness = new Thickness(0);
            textBox.IsReadOnly = true;

            // Folder deletion checkbox - this one should select all the checkboxes in the folder
            CheckBox folderCheckBox = new CheckBox();
            folderCheckBox.Padding = new Thickness(5);
            folderCheckBox.VerticalAlignment = VerticalAlignment.Center;

            string thisSectionKey = sectionContext.SectionKey;
            folderNameGrid.Tag = GetHeaderTag(thisSectionKey);

            // When folder checkbox toggled, check/uncheck all video checkboxes belonging to this folder
            folderCheckBox.Checked += (s, e) =>
            {
                foreach (var child in videoList.Children)
                {
                    if (child is Grid g && g.Tag is string t && t == thisSectionKey)
                    {
                        foreach (var elem in g.Children)
                        {
                            if (elem is CheckBox cb)
                            {
                                cb.IsChecked = true;
                            }
                        }
                    }
                }
                UpdateActionButtonState();
            };

            folderCheckBox.Unchecked += (s, e) =>
            {
                foreach (var child in videoList.Children)
                {
                    if (child is Grid g && g.Tag is string t && t == thisSectionKey)
                    {
                        foreach (var elem in g.Children)
                        {
                            if (elem is CheckBox cb)
                            {
                                cb.IsChecked = false;
                            }
                        }
                    }
                }
                UpdateActionButtonState();
            };

            // Add elements
            Grid.SetColumn(folderCheckBox, 1);
            folderNameGrid.Children.Add(textBox);
            folderNameGrid.Children.Add(folderCheckBox);
            videoList.Children.Add(folderNameGrid);

            // Horizontal line separator
            Separator separator = new Separator();
            separator.Margin = new Thickness(0, 5, 0, 5);
            videoList.Children.Add(separator);
        }

        private void CreateVideoButtons(List<(FileInfo videoFile, FishLens_App.Models.Video videoData)> videoDataList, LibrarySectionContext sectionContext)
        {
            foreach (var (videoFile, videoData) in videoDataList)
            {
                Grid grid = new Grid();
                grid.Tag = sectionContext.SectionKey;
                grid.DataContext = sectionContext;
                grid.HorizontalAlignment = HorizontalAlignment.Stretch;
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Button button = CreateSingleVideoButton(videoFile, videoData);
                button.Click += VideoButtonClick;

                var internalCheckBox = button.Content is Grid g
                    ? g.Children.OfType<CheckBox>().FirstOrDefault(c => c.Tag as string == "selectionCheck")
                    : null;

                if (internalCheckBox != null)
                {
                    button.MouseEnter += (s, e) => internalCheckBox.Opacity = 1;
                    button.MouseLeave += (s, e) => { if (internalCheckBox.IsChecked != true) internalCheckBox.Opacity = 0; };
                    internalCheckBox.Checked += (s, e) => { internalCheckBox.Opacity = 1; UpdateActionButtonState(); };
                    internalCheckBox.Unchecked += (s, e) => { internalCheckBox.Opacity = 0; UpdateActionButtonState(); };
                }

                Grid.SetColumn(button, 0);
                grid.Children.Add(button);
                videoList.Children.Add(grid);
            }
        }

        private Button CreateSingleVideoButton(FileInfo videoFile, FishLens_App.Models.Video videoData)
        {
            if (string.IsNullOrWhiteSpace(videoData.VideoFilePath)) videoData.VideoFilePath = videoFile.FullName;
            if (string.IsNullOrWhiteSpace(videoData.Run)) videoData.Run = (Application.Current as App)?.ActiveRun ?? string.Empty;
            if (string.IsNullOrWhiteSpace(videoData.Name)) videoData.Name = videoFile.Name;

            var tierColor = GetTierColor(videoData.AvgConfidence);
            bool isLow = tierColor == COLOR_LOW;
            var tierBrush = new SolidColorBrush(tierColor);

            // ── Column layout: stripe(3) | gap(8) | text(*) | pct(Auto) | checkbox(Auto) | gap(6) ──
            var grid = new Grid { Margin = new Thickness(0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });

            // ── Col 0: left stripe ──
            var stripe = new Border
            {
                Tag = "stripe",
                Background = tierBrush,
                CornerRadius = new CornerRadius(0),
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            Grid.SetColumn(stripe, 0);
            grid.Children.Add(stripe);

            // ── Col 2: filename + meta ──
            string dirText = (videoData.Direction ?? string.Empty).ToLower() switch
            {
                "upstream" => "Upstream",
                "downstream" => "Downstream",
                _ => "Indecisive",
            };
            bool fishPresent = videoData.LikelyClass != "not_fish" && videoData.LikelyClass != "no_fish";
            string metaText = fishPresent
                ? $"{dirText} · {CapitalizeFirstLetter(videoData.Species ?? string.Empty)}"
                : "Not Present";

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = videoFile.Name,
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)Application.Current.Resources["OnAccentForeground"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            textStack.Children.Add(new TextBlock
            {
                Text = metaText,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                Margin = new Thickness(0, 1, 0, 0),
            });
            Grid.SetColumn(textStack, 2);
            grid.Children.Add(textStack);

            // ── Col 3: confidence % ──
            var pctBlock = new TextBlock
            {
                Tag = "pct",
                Text = $"{videoData.AvgConfidence * 100:0}%",
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = tierBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
            };
            Grid.SetColumn(pctBlock, 3);
            grid.Children.Add(pctBlock);

            // ── Col 4: selection checkbox (hidden until hover or checked) ──
            var checkBox = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Opacity = 0,
                Tag = "selectionCheck",
            };
            Grid.SetColumn(checkBox, 4);
            grid.Children.Add(checkBox);

            // ── Button shell ──
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "bd";
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(BUTTON_CORNER_RADIUS));
            borderFactory.SetValue(Border.ClipToBoundsProperty, true);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Stretch);
            borderFactory.AppendChild(cp);

            var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Button.BackgroundProperty,
                new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), "bd"));

            var template = new ControlTemplate(typeof(Button));
            template.VisualTree = borderFactory;
            template.Triggers.Add(hoverTrigger);

            return new Button
            {
                Content = grid,
                Height = BUTTON_HEIGHT,
                Tag = videoFile.FullName,
                DataContext = videoData,
                Background = isLow
                    ? new SolidColorBrush(Color.FromArgb(30, 0xE2, 0x4B, 0x4A))
                    : Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(BUTTON_MARGIN),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Template = template,
            };
        }

        // **************************************************
        // Function: IsLowConfidence
        // Description: Determines if confidence value is below threshold
        // **************************************************
        private bool IsLowConfidence(double confidence)
        {
            double threshold = (Application.Current as App)?.Configuration?.ConfidenceThreshold
                ?? _config?.ConfidenceThreshold
                ?? DEFAULT_CONFIDENCE_THRESHOLD;
            return confidence < threshold;
        }

        public void RefreshLibraryConfidenceStyles()
        {
            foreach (var child in videoList.Children)
            {
                if (child is not Grid rowGrid) continue;
                foreach (var button in rowGrid.Children.OfType<Button>())
                {
                    if (button.DataContext is not FishLens_App.Models.Video video) continue;
                    var tierColor = GetTierColor(video.AvgConfidence);
                    var tierBrush = new SolidColorBrush(tierColor);
                    bool isLow = tierColor == COLOR_LOW;

                    if (button.Content is Grid g)
                    {
                        foreach (UIElement el in g.Children)
                        {
                            if (el is Border b && "stripe".Equals(b.Tag))
                                b.Background = tierBrush;
                            if (el is System.Windows.Shapes.Ellipse e && "dot".Equals(e.Tag))
                                e.Fill = tierBrush;
                            if (el is TextBlock tb && "pct".Equals(tb.Tag))
                                tb.Foreground = tierBrush;
                        }
                    }
                    button.Background = isLow
                        ? new SolidColorBrush(Color.FromArgb(30, 0xE2, 0x4B, 0x4A))
                        : Brushes.Transparent;
                }
            }
        }

        #endregion

        #region Button Styling

        private Color GetTierColor(double confidence)
        {
            double threshold = (Application.Current as App)?.Configuration?.ConfidenceThreshold
                ?? _config?.ConfidenceThreshold
                ?? DEFAULT_CONFIDENCE_THRESHOLD;
            if (confidence >= threshold) return COLOR_HIGH;
            if (confidence >= threshold * 0.6) return COLOR_MID;
            return COLOR_LOW;
        }

        private Style CreateButtonStyle(bool isLowConfidence)
        {
            // No longer used — styling is built directly into CreateSingleVideoButton
            return new Style(typeof(Button));
        }

        #endregion

        private void MainFrame_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {

        }
    }
}
