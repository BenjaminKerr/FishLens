// **************************************************
// ***********************************
// File: MainWindow.xaml.cs
// Description: Handles the analysis page's functionality
// Author: Benjamin Kerr
// 2025 - 2026
// ***********************************
// **************************************************

using FishLens_App.Interfaces;
using FishLens_App.Models;
using FishLens_App.Services;
using FishLens_App.Helper_Classes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using System.Windows.Media.Animation;

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
        private const string FishExportHeader = "video_file,location,species,species_confidence,likely_class,confidence,direction,start_time_sec,end_time_sec,video_timestamp,run";
        private const string NoFishExportHeader = "video_file,location,video_timestamp";

        // State
        private string _currentFolderName = string.Empty;

        #endregion

        #region Fields

        private readonly IProjectPathResolver _pathResolver;
        private readonly IFileSystemManager _fileSystemManager;
        private readonly ILogger<MainWindow> _logger;
        private readonly AppConfiguration _config;
        private readonly CheckBoxToggle _checkBoxes;
        private readonly PythonWorkerPoolService _workerPool;
        // Stack of deletion batches for undo support
        private readonly Stack<DeletionBatch> _deletionHistory = new Stack<DeletionBatch>();
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
        private ProgressBarBuilder _builder = new ProgressBarBuilder();
        private bool _isDraggingScrubber = false;
        private bool _isPlaying = false;
        private bool _videoEnded = false;
        private int _suppressTimerTicks = 0;
        private string _playbackTempPath = null; // temp MP4 created from ASF for accurate scrubbing
        private bool _processingComplete = false;


        // Sidebar state
        private bool _sidebarCollapsed = false;

        // Multi-track state - all tracks for the currently displayed video
        private List<FishLens_App.Models.Video> _currentTracks = new List<FishLens_App.Models.Video>();
        private List<FishLens_App.Models.Video> _savedCurrentTracks = new List<FishLens_App.Models.Video>();
        private int _currentTrackIndex;

        // Guards that prevent UI event handlers from firing during programmatic control updates.
        private bool _suppressStatusHandler = false;
        private bool _suppressTrackFieldHandlers = false;
        private bool _updatingConfidenceText = false;
        private bool _hasUnsavedAnalysisChanges = false;
        int completedVideos = 0;

        public ObservableCollection<VideoProgressStatus> Bars { get; } = new ObservableCollection<VideoProgressStatus>();
        public ObservableCollection<VideoProgressStatus> ThreadStatuses { get; } = new ObservableCollection<VideoProgressStatus>();



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

        private class ExportDataSet
        {
            public string[] FishLines { get; set; } = Array.Empty<string>();
            public string[] NoFishLines { get; set; } = Array.Empty<string>();

            public bool HasData =>
                (FishLines?.Skip(1).Any(line => !string.IsNullOrWhiteSpace(line)) ?? false) ||
                (NoFishLines?.Skip(1).Any(line => !string.IsNullOrWhiteSpace(line)) ?? false);
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
            ClearAnalysisPanel();
            app.ApplyCurrentSettings();
            _checkBoxes = GetCheckBoxToggleFromApplication();
            _config = GetConfigurationFromApplication();
            _workerPool = new PythonWorkerPoolService(_pathResolver, _logger);
            _workerPool.ProgressChanged += WorkerPool_ProgressChanged;
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
            DataContext = this;

            AccountSettingsButton.Visibility = app.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
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
            SetTransportButtonIcons();
            fishSpecies.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler(FishSpeciesTextChanged));
            PopulateLocationDropdown();
            UpdateRunDisplay();
            App.LocationChanged += OnLocationChanged;
            App.RunChanged += OnRunChanged;
            _ = _workerPool.StartBaselineAsync();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            App.LocationChanged -= OnLocationChanged;
            App.RunChanged -= OnRunChanged;
            _workerPool.ProgressChanged -= WorkerPool_ProgressChanged;
            _workerPool.Dispose();
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
            StopVideoForNavigation();

            if (IsCurrentPageSettings())
            {
                //if (CheckForUnsavedChanges())
                //{
                ExpandSidebar();
                MainFrame.Visibility = Visibility.Collapsed;
                RefreshAnalysisThemeStyles();
                //}
            }
            else
            {
                ExpandSidebar();
                MainFrame.Visibility = Visibility.Collapsed;
                RefreshAnalysisThemeStyles();
            }
        }

        // **************************************************
        // Function: SignOutButtonClick
        // Description: Navigates back to the signin page
        // **************************************************
        private void SignOutButtonClick(object sender, RoutedEventArgs e)
        {
            StopVideoForNavigation();
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
            CollapseSidebar();
            NavigateToPage(new History(_pathResolver, _fileSystemManager, _logger), "History");
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
            CollapseSidebar();
            NavigateToPage(new AccountSettings(), "AccountSettings");
        }

        // **************************************************
        // Function: IsCurrentPageSettings
        // Description: Returns true if the current frame content is the Settings page
        // **************************************************
        public bool IsCurrentPageSettings() => MainFrame.Content is Settings;

        // **************************************************
        // Function: NavigateToPage
        // Description: Handles logic common to both navigation functions
        // **************************************************
        private void NavigateToPage(object page, string pageName)
        {
            StopVideoForNavigation();
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
            _logger.LogInformation("Starting worker-pool analysis with videoFolder: {VideoFolder}", videoFolder);
            Dispatcher.Invoke(ShowAnalysisProgress);
            _processingTcs = new TaskCompletionSource<bool>();
            try
            {
                var context = CreateAnalysisBatchContext(videoFolder);
                var summary = await _workerPool.AnalyzeFolderAsync(context, System.Threading.CancellationToken.None);
                if (summary.Cancelled)
                    _processingTcs.TrySetCanceled();
                else
                    _processingTcs.TrySetResult(true);

                Dispatcher.Invoke(() => CompleteAnalysisProgress(summary));

                var _syncApp = Application.Current as App;
                string _syncActiveRun = _syncApp?.ActiveRun ?? string.Empty;
                string _syncCsvPath = _pathResolver.ResolveRunCsvPath(_syncActiveRun);
                string _syncNoFishPath = _pathResolver.ResolveSessionNoFishCsvPath(_syncActiveRun);
                int _syncOrgId = _syncApp?.CurrentOrganizationId ?? 0;
                int _syncUserId = _syncApp?.CurrentUserId ?? 0;
                string _syncConn = _syncApp?.connectionString;
                _ = System.Threading.Tasks.Task.Run(() =>
                    FishLens_App.Services.DbSyncService.SyncRunToDb(_syncCsvPath, _syncOrgId, _syncUserId, _syncConn));
                _ = System.Threading.Tasks.Task.Run(() =>
                    FishLens_App.Services.DbSyncService.SyncNoFishRunToDb(_syncNoFishPath, _syncActiveRun, _syncOrgId, _syncUserId, _syncConn));
            }
            catch (OperationCanceledException)
            {
                _processingTcs.TrySetCanceled();
                Dispatcher.Invoke(HideAnalysisProgress);
            }
            catch (Exception ex)
            {
                _processingTcs.TrySetException(ex);
                Dispatcher.Invoke(HideAnalysisProgress);
                MessageBox.Show(ex.Message, "Could not process videos.", MessageBoxButton.OK);
            }
        }

        private AnalysisBatchContext CreateAnalysisBatchContext(string videoFolder)
        {
            string activeRun = (Application.Current as App)?.ActiveRun ?? string.Empty;
            string activeLocation = (Application.Current as App)?.ActiveLocation ?? "Unknown";
            string runFolder = string.IsNullOrWhiteSpace(activeRun) ? string.Empty : _pathResolver.ResolveRunFolder(activeRun);
            var videoFiles = Directory.GetFiles(videoFolder)
                .Where(f => VideoExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            return new AnalysisBatchContext
            {
                VideoFolder = videoFolder,
                VideoFiles = videoFiles,
                RunName = activeRun,
                RunFolder = runFolder,
                Location = activeLocation,
                UpstreamDirection = GetUpstreamDirectionForLocation(activeLocation),
                FastMode = _checkBoxes?.FastMode ?? false,
                RunCsvPath = string.Equals(activeRun, "debug", StringComparison.OrdinalIgnoreCase)
                    ? _pathResolver.ResolveCsvScriptPath()
                    : _pathResolver.ResolveRunCsvPath(activeRun),
                SessionCsvPath = string.IsNullOrWhiteSpace(activeRun) ? string.Empty : _pathResolver.ResolveSessionCsvPath(activeRun),
                SessionNoFishCsvPath = string.IsNullOrWhiteSpace(activeRun) ? string.Empty : _pathResolver.ResolveSessionNoFishCsvPath(activeRun),
                AllHistoryCsvPath = _pathResolver.ResolveAllTimeMasterFishCsvPath()
            };
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
        // Description: Tracks the legacy FastMode setting, which now represents Slow Mode.
        // **************************************************
        private void OnFastModeChanged()
        {
            bool fastMode = _checkBoxes?.FastMode ?? false;
            if (_yoloFastModeAtStart == fastMode) return; // no actual change, ignore

            _yoloFastModeAtStart = fastMode;
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
            string activeRun = (Application.Current as App)?.ActiveRun ?? string.Empty;
            if (runDisplayLabel != null)
                runDisplayLabel.Text = string.IsNullOrWhiteSpace(activeRun) ? "No run selected" : activeRun;
        }

        // **************************************************
        // Function: GetUpstreamDirectionForActiveLocation
        // Description: Returns "left" or "right" based on the UpstreamDirection of the
        //              currently active location in the live app configuration.
        // **************************************************
        private string GetUpstreamDirectionForActiveLocation()
        {
            string activeLocation = (Application.Current as App)?.ActiveLocation ?? "Unknown";
            return GetUpstreamDirectionForLocation(activeLocation);
        }

        private string GetUpstreamDirectionForLocation(string locationName)
        {
            var match = (Application.Current as App)?.Configuration?.Locations?
                .FirstOrDefault(l => string.Equals(l.Name, locationName, StringComparison.OrdinalIgnoreCase));

            string direction = match?.UpstreamDirection?.Trim().ToLowerInvariant();
            return direction == "right" ? "right" : "left";
        }

        // **************************************************
        // Function: LoadUpstreamDirectionMap
        // Description: Returns a dictionary of location name -> upstream direction ("left"/"right")
        //              loaded from the live app configuration. Used for direction-flip logic on location change.
        // **************************************************
        private Dictionary<string, string> LoadUpstreamDirectionMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var loc in (Application.Current as App)?.Configuration?.Locations ?? Enumerable.Empty<LocationEntry>())
                map[loc.Name ?? string.Empty] = loc.UpstreamDirection == "right" ? "right" : "left";
            return map;
        }

        // **************************************************
        // Function: PopulateLocationDropdown
        // Description: Loads named locations from the live app configuration into the header ComboBox
        // **************************************************
        private void PopulateLocationDropdown()
        {
            try
            {
                var app = Application.Current as App;
                var config = app?.Configuration;
                var locationNames = config?.Locations?
                    .Where(l => !string.IsNullOrWhiteSpace(l.Name))
                    .Select(l => l.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? new List<string>();

                if (locationNames.Count == 0)
                    locationNames.Add("Unknown");

                string activeLocation = app?.ActiveLocation ?? config?.ActiveLocation ?? "Unknown";
                if (!locationNames.Contains(activeLocation))
                    activeLocation = locationNames.Contains(config?.ActiveLocation) ? config.ActiveLocation : locationNames[0];

                // Suppress SelectionChanged while populating
                locationDropdown.SelectionChanged -= LocationDropdown_SelectionChanged;
                locationDropdown.ItemsSource = locationNames;
                locationDropdown.SelectedItem = activeLocation;
                locationDropdown.SelectionChanged += LocationDropdown_SelectionChanged;

                // Keep App and its configuration in sync with the visible dropdown value.
                if (app != null)
                {
                    app.ActiveLocation = activeLocation;
                    if (app.Configuration != null)
                        app.Configuration.ActiveLocation = activeLocation;
                }
            }
            catch { /* non-critical */ }
        }

        // **************************************************
        // Function: LocationDropdown_SelectionChanged
        // Description: Persists the newly selected location to appsettings.json and updates App
        // **************************************************
        private void LocationDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string selected = locationDropdown.SelectedItem as string;
            if (string.IsNullOrEmpty(selected)) return;

            // Block location changes while a video batch is processing to avoid
            // mismatching the location/direction env vars already baked into the running process.
            bool isProcessing = _processingTcs != null && !_processingTcs.Task.IsCompleted;
            if (isProcessing)
            {
                // Silently revert to whatever was previously committed
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

            // Persist to JSON so next startup remembers the choice
            try
            {
                string configPath = Path.Combine(_pathResolver.ResolveProjectRoot(), "appsettings.json");
                if (!File.Exists(configPath)) return;

                string json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Rebuild the JSON with the updated ActiveLocation
                var dict = new System.Collections.Generic.Dictionary<string, object>();
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name == "ActiveLocation")
                        dict[prop.Name] = selected;
                    else
                        dict[prop.Name] = prop.Value.Clone();
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(configPath, JsonSerializer.Serialize(dict, options));
            }
            catch { /* non-critical */ }
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

            // Sync the location change to the database for every detection row that belongs to
            // these videos.  We re-read the already-updated run CSV so every track carries the
            // new location without requiring the user to hit Save Changes separately.
            var dbApp = Application.Current as App;
            if (dbApp != null && dbApp.CurrentOrganizationId > 0 && !string.IsNullOrWhiteSpace(dbApp.connectionString))
            {
                int dbOrgId = dbApp.CurrentOrganizationId;
                int dbUserId = dbApp.CurrentUserId;
                string dbConn = dbApp.connectionString;

                var seenVideoKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var video in selectedVideos)
                {
                    string videoFileName = !string.IsNullOrWhiteSpace(video.Name)
                        ? video.Name
                        : Path.GetFileName(video.VideoFilePath ?? string.Empty);
                    string videoRun = ResolveVideoRun(video);
                    string videoKey = $"{videoRun}|{videoFileName}";

                    if (!string.IsNullOrWhiteSpace(videoFileName) && !seenVideoKeys.Contains(videoKey))
                    {
                        seenVideoKeys.Add(videoKey);

                        var allTracks = GetAllTracks(videoFileName, videoRun, video.VideoFilePath);
                        foreach (var track in allTracks)
                        {
                            if (track != null
                                && !string.IsNullOrWhiteSpace(track.LikelyClass)
                                && !track.LikelyClass.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                            {
                                var capturedTrack = track;
                                int capturedOrgId = dbOrgId;
                                int capturedUserId = dbUserId;
                                string capturedConn = dbConn;
                                _ = System.Threading.Tasks.Task.Run(() =>
                                    FishLens_App.Services.DbSyncService.UpsertTrackToDb(
                                        capturedTrack, capturedOrgId, capturedUserId, capturedConn));
                            }
                        }
                    }
                }
            }
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

        }

        // **************************************************
        // Function: WorkerPool_ProgressChanged
        // Description: Handler for visual progress updates when processign progress is made
        // **************************************************
        private void WorkerPool_ProgressChanged(object sender, AnalysisProgressEventArgs e)
        {
            // Snapshot the process, ready-TCS, and kill-count for this Python instance at startup.
            // If the process is killed and restarted (e.g. run change), the NEW instance
            // sets _yoloProcess and _yoloReadyTcs to fresh objects.  Without snapshots here,
            // this dying loop would TrySetException on the NEW TCS and crash the next run.
            // _yoloKillCount is snapshotted so we can tell whether the exit was intentional:
            // if it was incremented since we started, the kill was a deliberate restart and we
            // must NOT poison _processingTcs (the new analysis is already underway).


            Dispatcher.Invoke(() =>
            {
                if (e.TotalVideos > 0)
                    _totalVideos = e.TotalVideos;

                if (e.EventType == "total")
                {
                    completedVideos = e.CompletedVideos;
                    Bars.Clear();
                    foreach (var b in _builder.InitialBuild(_totalVideos))
                        Bars.Add(b);
                    SetAnalysisStatus("Processed " + completedVideos + "/" + _totalVideos + " videos...");
                    return;
                }

                if (e.EventType == "video_started" && !string.IsNullOrWhiteSpace(e.Message))
                {
                    var sections = e.Message.Split("|");
                    int pid = int.Parse(sections[0]);
                    string filename = sections[1];
                    string vidName = sections[2];
                    var existing = ThreadStatuses.FirstOrDefault(t => t.Pid == pid);

                    if (existing == null)
                    {
                        VideoProgressStatus status = new VideoProgressStatus()
                        {
                            Message = vidName,
                            Pid = pid,
                            Filename = filename
                        };
                        ThreadStatuses.Add(status);
                        var bar = Bars.FirstOrDefault(b => b.State == VideoProgressState.Empty);
                        if (bar != null)
                        {
                            status.VideoIndex = Bars.IndexOf(bar);
                            bar.SetInProgress();
                        }
                    }
                    else
                    {
                        if (existing.Filename != filename)
                        {
                            var bar = Bars.FirstOrDefault(b => b.State == VideoProgressState.Empty);
                            if (bar != null)
                            {
                                existing.VideoIndex = Bars.IndexOf(bar);
                                bar.SetInProgress();
                            }
                        }
                        existing.Message = vidName;
                    }
                    return;
                }

                if (e.EventType == "video_finished")
                {
                    completedVideos = e.CompletedVideos;
                    var sections = e.Message.Split("|");
                    int pid = int.Parse(sections[0]);
                    string filename = sections[1];

                    var threadStatus = ThreadStatuses.FirstOrDefault(x => x.Pid == pid);

                    if (threadStatus != null &&
                        threadStatus.VideoIndex >= 0 &&
                        threadStatus.VideoIndex < Bars.Count)
                    {
                        Bars[threadStatus.VideoIndex]?.SetComplete();
                    }
                    threadStatus.Message = ("");

                    if (completedVideos == _totalVideos)
                    {
                        SetAnalysisStatus("Finishing Up...");
                    }
                    else
                    {
                        SetAnalysisStatus("Processed " + completedVideos + "/" + _totalVideos + " videos...");
                    }
                }
            });
        }

        // **************************************************
        // Function: ShowAnalysisProgress / HideAnalysisProgress / SetAnalysisStatus
        // Description: Helpers to show/hide/update the inline progress area
        // **************************************************
        private void ShowAnalysisProgress()
        {
            analysisProgressArea.Visibility = Visibility.Visible;
            analysisStatusText.Text = "Starting up, please wait...";
            Bars.Clear();
            App.RaiseAnalysisStateChanged(true);
        }

        private void HideAnalysisProgress()
        {
            analysisProgressArea.Visibility = Visibility.Collapsed;
            App.RaiseAnalysisStateChanged(false);
        }

        private void CompleteAnalysisProgress(AnalysisRunSummary summary)
        {
            int total = summary?.PendingVideos ?? _totalVideos;
            int completed = summary == null
                ? _totalVideos
                : Math.Max(0, Math.Min(total, summary.AnalyzedVideos + summary.FailedVideos));

            if (total > 0)
            {
                Bars.Clear();
                int index = 0;
                foreach (var b in _builder.InitialBuild(total))
                {
                    b.VideoIndex = index++;
                    if (b.VideoIndex < completed)
                        b.SetComplete();
                    Bars.Add(b);
                }
            }

            SetAnalysisStatus(summary?.Cancelled == true ? "Analysis cancelled" : "Analysis complete");
            HideAnalysisProgress();
        }

        private void SetAnalysisStatus(string status)
        {
            analysisStatusText.Text = status;
        }

        private void MarkAnalysisDirty()
        {
            if (_suppressTrackFieldHandlers || _suppressStatusHandler)
                return;
            if (_currentTrackIndex < 0 || _currentTrackIndex >= _currentTracks.Count)
                return;

            _hasUnsavedAnalysisChanges = true;
            UpdateAnalysisSaveStatus("Unsaved Changes", "WarningBrush");
        }

        private void SetAnalysisStatusSaved()
        {
            _hasUnsavedAnalysisChanges = false;
            UpdateAnalysisSaveStatus("Saved Changes", "SuccessBrush");

            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            timer.Tick += (s, e) =>
            {
                if (!_hasUnsavedAnalysisChanges)
                    ClearAnalysisSaveStatus();
                timer.Stop();
            };
            timer.Start();
        }

        private void ClearAnalysisSaveStatus()
        {
            _hasUnsavedAnalysisChanges = false;
            if (analysisSaveStatusText == null)
                return;

            analysisSaveStatusText.Text = string.Empty;
            analysisSaveStatusText.Visibility = Visibility.Collapsed;
        }

        private void UpdateAnalysisSaveStatus(string text, string brushKey)
        {
            if (analysisSaveStatusText == null)
                return;

            analysisSaveStatusText.Text = text;
            analysisSaveStatusText.Foreground = (Brush)Application.Current.Resources[brushKey];
            analysisSaveStatusText.Visibility = Visibility.Visible;
        }

        private static FishLens_App.Models.Video CloneVideo(FishLens_App.Models.Video video)
        {
            if (video == null) return null;

            return new FishLens_App.Models.Video
            {
                Name = video.Name,
                TrackId = video.TrackId,
                LikelyClass = video.LikelyClass,
                Confidence = video.Confidence,
                StartTime = video.StartTime,
                EndTime = video.EndTime,
                AvgConfidence = video.AvgConfidence,
                Direction = video.Direction,
                Species = video.Species,
                SpeciesConfidence = video.SpeciesConfidence,
                Date = video.Date,
                Time = video.Time,
                DetectionTimestamp = video.DetectionTimestamp,
                VideoFilePath = video.VideoFilePath,
                Location = video.Location,
                Run = video.Run
            };
        }

        private static List<FishLens_App.Models.Video> CloneVideoTracks(IEnumerable<FishLens_App.Models.Video> tracks)
        {
            return tracks?.Select(CloneVideo).Where(track => track != null).ToList()
                ?? new List<FishLens_App.Models.Video>();
        }

        private void RefreshAnalysisBaseline()
        {
            _savedCurrentTracks = CloneVideoTracks(_currentTracks);
        }

        private void RestoreAnalysisBaseline()
        {
            _currentTracks = CloneVideoTracks(_savedCurrentTracks);
            if (_currentTracks.Count == 0)
            {
                ClearAnalysisSaveStatus();
                UpdateFishMarkers();
                return;
            }

            _currentTrackIndex = Math.Max(0, Math.Min(_currentTrackIndex, _currentTracks.Count - 1));
            DisplayTrackInUi(_currentTracks[_currentTrackIndex]);
            UpdateTrackNavigator();
        }

        private bool ConfirmDiscardUnsavedAnalysisChanges()
        {
            if (!_hasUnsavedAnalysisChanges)
                return true;

            var result = MessageBox.Show(
                "You have unsaved analysis changes. Continue without saving them?",
                "Unsaved Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return false;

            RestoreAnalysisBaseline();
            return true;
        }


        // **************************************************
        // Function: UpdateActionButtonState
        // Description: Enables/disables the Delete, Change Location, and Undo buttons based on
        //              whether any video checkboxes are checked and whether there is undo history.
        // **************************************************
        private void UpdateActionButtonState()
        {
            bool anyChecked = GetSelectedVideoGrids().Count > 0;
            bool hasDisplayedTrack = _processingComplete
                && _currentTracks.Count > 0
                && _currentTrackIndex >= 0
                && _currentTrackIndex < _currentTracks.Count;
            deleteSelectedVideos.IsEnabled = anyChecked;
            changeLocationForSelected.IsEnabled = anyChecked;
            undoLastDelete.IsEnabled = _deletionHistory.Count > 0;
            fishPresentStatus.IsEnabled = hasDisplayedTrack;
            fishTravelDirection.IsEnabled = hasDisplayedTrack;
            fishSpecies.IsEnabled = hasDisplayedTrack;
            saveButton.IsEnabled = hasDisplayedTrack;
            fishPresentConfidence.IsEnabled = hasDisplayedTrack;
            fishSpeciesConfidence.IsEnabled = hasDisplayedTrack;
            analysisProgressArea.Visibility = App.IsAnalyzing ? Visibility.Visible : Visibility.Collapsed;
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
            _workerPool.CancelActiveRun();
            HideAnalysisProgress();
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
                MessageBox.Show("No videos selected to remove.", "Remove from Library", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!ConfirmDelete(selected.Count)) return;

            bool removedCurrentVideo = selected.Any(x => IsCurrentDisplayedVideo(x.path, x.video));

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

            if (removedCurrentVideo)
            {
                ResetVideoPlayer(showPlaceholder: true);
                ClearAnalysisPanel();
            }

            // If the list is now empty, hide the video controls entirely.
            bool anyVideosLeft = videoList.Children.OfType<Grid>().Any(g => g.Tag is string t && !t.StartsWith("header:"));
            if (!anyVideosLeft)
            {
                ResetVideoPlayer(showPlaceholder: true);
                ClearAnalysisPanel();
            }

            RefreshSessionOverview();
            UpdateActionButtonState();
            MessageBox.Show($"Removed {selected.Count} video(s) from view.", "Removed", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // **************************************************
        // Function: ChangeLocationForSelectedClick
        // Description: Updates the location column in all CSVs for the checked videos only.
        // **************************************************
        public void ChangeLocationForSelectedClick(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedVideoGrids();
            if (selected.Count == 0)
            {
                MessageBox.Show("No videos selected. Use the checkboxes to select one or more videos first.",
                    "Change Location", MessageBoxButton.OK);
                return;
            }

            string newLocation = ShowLocationPickerDialog();
            if (newLocation == null) return;

            UpdateLocationForVideosInCsvs(selected.Select(x => x.video), newLocation);
            RefreshSessionOverview();
            UpdateActionButtonState();

            MessageBox.Show(
                $"Location updated to \"{newLocation}\" for {selected.Count} video(s).",
                "Location Updated", MessageBoxButton.OK);
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
                Height = 180,
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
                    Button btn = g.Children.OfType<Button>().FirstOrDefault();
                    if (btn == null) continue;

                    var innerGrid = btn.Content as Grid;
                    var cb = innerGrid?.Children.OfType<CheckBox>()
                                                .FirstOrDefault(c => c.Tag as string == "selectionCheck");

                    if (cb != null && cb.IsChecked == true && btn.Tag is string path)
                    {
                        result.Add((g, path, btn.DataContext as FishLens_App.Models.Video));
                    }
                }
            }

            return result;
        }

        private bool IsCurrentDisplayedVideo(string path, FishLens_App.Models.Video video)
        {
            string displayedName = videoName.Text;
            if (string.IsNullOrWhiteSpace(displayedName) || displayedName == "--") return false;

            string selectedName = !string.IsNullOrWhiteSpace(video?.Name)
                ? video.Name
                : Path.GetFileName(path);
            bool nameMatch = string.Equals(displayedName, selectedName, StringComparison.OrdinalIgnoreCase);

            bool trackPathMatch = _currentTracks.Any(track =>
                !string.IsNullOrWhiteSpace(track.VideoFilePath)
                && string.Equals(track.VideoFilePath, path, StringComparison.OrdinalIgnoreCase));

            bool playerPathMatch = videoPlayer.Source != null
                && string.Equals(videoPlayer.Source.LocalPath, path, StringComparison.OrdinalIgnoreCase);

            return nameMatch || trackPathMatch || playerPathMatch;
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
        // Description: Puts the saved Grid elements from a DeletionBatch back into the video list.
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

                if (item.grid.Parent == null)
                {
                    foreach (var elem in item.grid.Children)
                        if (elem is CheckBox cb) cb.IsChecked = false;

                    double restoredConf = item.video.AvgConfidence;
                    int insertIndex = -1;
                    bool inTargetSection = false;
                    int idx = 0;
                    while (idx < videoList.Children.Count && insertIndex < 0)
                    {
                        if (videoList.Children[idx] is Grid g && g.Tag is string t)
                        {
                            if (t.Equals(GetHeaderTag(item.folder), StringComparison.OrdinalIgnoreCase))
                            {
                                inTargetSection = true;
                            }
                            else if (inTargetSection && t.StartsWith("header:", StringComparison.OrdinalIgnoreCase))
                            {
                                insertIndex = idx;
                            }
                            else if (inTargetSection
                                     && !t.StartsWith("header:", StringComparison.OrdinalIgnoreCase)
                                     && t.Equals(item.folder, StringComparison.OrdinalIgnoreCase))
                            {
                                double existingConf = 0;
                                bool confFound = false;
                                foreach (var elem in g.Children)
                                {
                                    if (!confFound && elem is Button b && b.DataContext is FishLens_App.Models.Video v)
                                    {
                                        existingConf = v.AvgConfidence;
                                        confFound = true;
                                    }
                                }
                                if (restoredConf <= existingConf)
                                    insertIndex = idx;
                            }
                        }
                        idx++;
                    }

                    if (insertIndex >= 0)
                        videoList.Children.Insert(insertIndex, item.grid);
                    else
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
                var tracks = FishLens_App.Services.CsvUtils.ReadAllTracksFromCsv(csvPath, videoFileName, effectiveRun, videoFilePath);

                if (tracks.Count == 1 && tracks[0].LikelyClass == "N/A")
                {
                    string noFishPath = _pathResolver.ResolveSessionNoFishCsvPath(effectiveRun);
                    string noFishLocation = FishLens_App.Services.CsvUtils.ReadLocationFromNoFishCsv(noFishPath, videoFileName);
                    if (noFishLocation != null)
                    {
                        tracks[0].LikelyClass = "no_fish";
                        tracks[0].Location = noFishLocation;
                        tracks[0].StartTime = "0";
                        tracks[0].EndTime = "0";
                    }
                }

                return tracks;
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
                string scope = ShowExportScopeDialog();
                if (scope == null) return;

                ExportDataSet exportSet = BuildExportDataSet(scope);

                if (!exportSet.HasData)
                {
                    MessageBox.Show("No analysis data found for the selected scope.", "Export Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SaveFileDialog saveFileDialog = CreateExportSaveDialog();
                if (saveFileDialog.ShowDialog() == true)
                {
                    MakeExcelSheetAndInsertData(saveFileDialog, exportSet.FishLines, exportSet.NoFishLines);
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
            bool hasSessionData = HasCurrentSessionData();
            panel.Children.Add(new TextBlock
            {
                Text = "Which data should the export include?",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["PrimaryText"],
                Margin = new Thickness(0, 0, 0, 14),
                TextWrapping = TextWrapping.Wrap
            });

            var rbSession = new RadioButton
            {
                Content = "Current Session",
                IsChecked = hasSessionData,
                IsEnabled = hasSessionData,
                Margin = new Thickness(0, 0, 0, 6),
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["PrimaryText"]
            };
            var rbRun = new RadioButton
            {
                Content = "Current Run",
                IsChecked = !hasSessionData,
                Margin = new Thickness(0, 0, 0, 6),
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["PrimaryText"]
            };
            var rbAll = new RadioButton
            {
                Content = "All History",
                Margin = new Thickness(0, 0, 0, 14),
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["PrimaryText"]
            };

            panel.Children.Add(rbSession);
            panel.Children.Add(rbRun);
            panel.Children.Add(rbAll);

            string chosen = null;
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okBtn = new Button
            {
                Content = "Export",
                Width = 80,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                Background = (System.Windows.Media.Brush)Application.Current.Resources["AccentBrush"],
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["OnAccentForeground"],
                BorderThickness = new Thickness(0)
            };
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

        private string ResolveExportCsvPath(string scope)
        {
            string activeRun = (Application.Current as App)?.ActiveRun ?? string.Empty;
            return scope switch
            {
                "all" => _pathResolver.ResolveAllTimeMasterFishCsvPath(),
                "run" => _pathResolver.ResolveRunCsvPath(activeRun),
                _ => _pathResolver.ResolveSessionCsvPath(activeRun)
            };
        }

        private string ResolveExportNoFishCsvPath(string scope)
        {
            string activeRun = (Application.Current as App)?.ActiveRun ?? string.Empty;
            return scope == "session" ? _pathResolver.ResolveSessionNoFishCsvPath(activeRun) : null;
        }

        private ExportDataSet BuildExportDataSet(string scope)
        {
            if (scope == "session")
            {
                return BuildExportDataSetFromCsv(
                    ResolveExportCsvPath(scope),
                    ResolveExportNoFishCsvPath(scope));
            }

            var app = Application.Current as App;
            if (app != null && app.CurrentOrganizationId > 0 && !string.IsNullOrWhiteSpace(app.connectionString))
            {
                string activeRun = app.ActiveRun ?? string.Empty;
                string runFilter = scope == "run" ? activeRun : null;
                string[] dbLines = ReadExportDataLinesFromDb(app.CurrentOrganizationId, app.connectionString, runFilter);
                if (dbLines != null)
                    return SplitFullSchemaRowsForExport(dbLines);
            }

            return BuildExportDataSetFromCsv(
                ResolveExportCsvPath(scope),
                ResolveExportNoFishCsvPath(scope));
        }

        private ExportDataSet BuildExportDataSetFromCsv(string fishCsvPath, string noFishCsvPath)
        {
            return new ExportDataSet
            {
                FishLines = File.Exists(fishCsvPath)
                    ? File.ReadAllLines(fishCsvPath)
                    : new[] { FishExportHeader },
                NoFishLines = File.Exists(noFishCsvPath)
                    ? File.ReadAllLines(noFishCsvPath)
                    : new[] { NoFishExportHeader }
            };
        }

        private ExportDataSet SplitFullSchemaRowsForExport(IEnumerable<string> fullSchemaRows)
        {
            var fishLines = new List<string> { FishExportHeader };
            var noFishLines = new List<string> { NoFishExportHeader };

            foreach (string row in fullSchemaRows ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(row))
                    continue;

                string[] cols = FishLens_App.Services.CsvUtils.ParseCsvLine(row);
                string likelyClass = cols.Length > 4 ? cols[4].Trim() : string.Empty;
                bool noFish = likelyClass.Equals("no_fish", StringComparison.OrdinalIgnoreCase)
                    || likelyClass.Equals("not_fish", StringComparison.OrdinalIgnoreCase);

                if (noFish)
                {
                    string videoFile = cols.Length > 0 ? cols[0].Trim() : string.Empty;
                    string location = cols.Length > 1 ? cols[1].Trim() : string.Empty;
                    string timestamp = cols.Length > 9 ? cols[9].Trim() : string.Empty;
                    noFishLines.Add($"{videoFile},{location},{timestamp}");
                }
                else
                {
                    fishLines.Add(row);
                }
            }

            return new ExportDataSet
            {
                FishLines = fishLines.ToArray(),
                NoFishLines = noFishLines.ToArray()
            };
        }

        private string[] ReadExportDataLinesFromDb(int orgId, string connectionString, string runName = null)
        {
            var lines = new List<string>();
            try
            {
                using var conn = new SqlConnection(connectionString);
                conn.Open();
                using var cmd = new SqlCommand("kaharra.GetFishDetectionsByOrg", conn);
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@pOrgId", orgId);
                cmd.Parameters.AddWithValue("@pRunName", runName != null ? (object)runName : DBNull.Value);
                cmd.Parameters.AddWithValue("@pLocationName", DBNull.Value);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string videoFile = reader["VideoFile"]?.ToString() ?? string.Empty;
                    string location = reader["LocationName"]?.ToString() ?? string.Empty;
                    string species = reader["Species"]?.ToString() ?? string.Empty;
                    string speciesConf = reader["SpeciesConfidence"] == DBNull.Value
                        ? "0" : ((double)reader["SpeciesConfidence"]).ToString("F4");
                    string likelyClass = reader["LikelyClass"]?.ToString() ?? string.Empty;
                    string confidence = reader["Confidence"] == DBNull.Value
                        ? "0" : ((double)reader["Confidence"]).ToString("F4");
                    string direction = reader["Direction"]?.ToString() ?? string.Empty;
                    string startTime = reader["StartTimeSec"]?.ToString() ?? "0";
                    string endTime = reader["EndTimeSec"]?.ToString() ?? "0";
                    string timestamp = reader["DetectionTimestamp"] == DBNull.Value
                        ? string.Empty
                        : ((DateTime)reader["DetectionTimestamp"]).ToString("yyyy/MM/dd HH:mm:ss");
                    string run = reader["RunName"]?.ToString() ?? string.Empty;

                    lines.Add($"{videoFile},{location},{species},{speciesConf},{likelyClass},{confidence},{direction},{startTime},{endTime},{timestamp},{run}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading export detections from DB for org {OrgId}", orgId);
                return null;
            }

            return lines.ToArray();
        }

        private bool HasCurrentSessionData()
        {
            string activeRun = (Application.Current as App)?.ActiveRun ?? string.Empty;
            if (string.IsNullOrWhiteSpace(activeRun)) return false;

            return HasCsvData(_pathResolver.ResolveSessionCsvPath(activeRun))
                || HasCsvData(_pathResolver.ResolveSessionNoFishCsvPath(activeRun));
        }

        private static bool HasCsvData(string csvPath)
        {
            if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath)) return false;

            try
            {
                return File.ReadLines(csvPath)
                    .Skip(1)
                    .Any(line => !string.IsNullOrWhiteSpace(line));
            }
            catch
            {
                return false;
            }
        }

        private SaveFileDialog CreateExportSaveDialog()
        {
            return new SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                FileName = $"FishLens_Analysis_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}"
            };
        }

        private void MakeExcelSheetAndInsertData(SaveFileDialog saveFileDialog, string[] fishLines, string[] noFishLines)
        {
            string excelPath = saveFileDialog.FileName;
            fishLines ??= new[] { FishExportHeader };
            noFishLines ??= new[] { NoFishExportHeader };

            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var fishSheet = workbook.Worksheets.Add("Fish Detected");
                WriteDataToWorksheet(fishSheet, fishLines);
                FormatWorksheet(fishSheet, fishLines);

                var noFishSheet = workbook.Worksheets.Add("No Fish Detected");
                WriteDataToWorksheet(noFishSheet, noFishLines);
                FormatWorksheet(noFishSheet, noFishLines);

                var summarySheet = workbook.Worksheets.Add("Run Summary");
                BuildRunSummarySheet(summarySheet, fishLines, noFishLines);

                workbook.SaveAs(excelPath);
            }

            ShowExportSuccessMessage(excelPath);
            PromptToOpenExportedFile(excelPath);
        }

        private void BuildRunSummarySheet(ClosedXML.Excel.IXLWorksheet sheet, string[] fishLines, string[] noFishLines)
        {
            int upstream = 0, downstream = 0, indecisive = 0;
            int chinookUp = 0, chinookDown = 0;
            int omykissUp = 0, omykissDown = 0;

            for (int i = 1; i < fishLines.Length; i++)
            {
                var cols = fishLines[i].Split(',');
                string dir = cols.Length > 6 ? cols[6].Trim().ToLower() : string.Empty;
                string species = cols.Length > 2 ? cols[2].Trim().ToLower() : string.Empty;

                if (dir == "upstream") upstream++;
                else if (dir == "downstream") downstream++;
                else indecisive++;

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

            int totalFish = fishLines.Length > 1 ? fishLines.Length - 1 : 0;
            int totalNoFish = noFishLines.Length > 1 ? noFishLines.Length - 1 : 0;
            int net = upstream - downstream;
            int chinookNet = chinookUp - chinookDown;
            int omykissNet = omykissUp - omykissDown;

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

            sheet.Row(1).Style.Font.Bold = true;
            sheet.Row(1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightBlue;
            sheet.Row(7).Style.Font.Bold = true;
            sheet.Row(8).Style.Font.Bold = true;
            sheet.Row(9).Style.Font.Bold = true;
            sheet.Columns().AdjustToContents();
        }

        private void WriteDataToWorksheet(ClosedXML.Excel.IXLWorksheet worksheet, string[] allLines)
        {
            var confColumns = new HashSet<int> { 3, 5 };

            for (int line = 0; line < allLines.Length; line++)
            {
                string[] columns = allLines[line].Split(',');
                for (int column = 0; column < columns.Length; column++)
                {
                    string raw = columns[column].Trim();
                    if (line == 0 || !confColumns.Contains(column))
                    {
                        worksheet.Cell(line + 1, column + 1).Value = raw;
                        continue;
                    }
                    string clean = raw.TrimEnd('%');
                    if (double.TryParse(clean, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double val))
                    {
                        if (val <= 1.0) val *= 100.0;
                        worksheet.Cell(line + 1, column + 1).Value = $"{val:F2}%";
                    }
                    else
                    {
                        worksheet.Cell(line + 1, column + 1).Value = raw;
                    }
                }
            }
        }

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

        private void ShowExportSuccessMessage(string excelPath)
        {
            MessageBox.Show($"Data exported successfully to:\n{excelPath}", "Export Successful",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

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
            sessionRunText.Text = $"{(string.IsNullOrWhiteSpace(sourceRun) ? "--" : sourceRun)}";
            sessionLocationText.Text = $"{(string.IsNullOrWhiteSpace(location) ? "--" : location)}";

            string csvPath = string.IsNullOrWhiteSpace(sourceRun)
                ? _pathResolver.ResolveCsvScriptPath()
                : _pathResolver.ResolveRunCsvPath(sourceRun);
            if (!File.Exists(csvPath))
            {
                sessionNetUpstreamText.Text = "--";
                return;
            }

            int upstreamCount = 0;
            int downstreamCount = 0;
            var lines = File.ReadAllLines(csvPath);
            for (int i = 1; i < lines.Length; i++)
            {
                var cols = lines[i].Split(',');
                if (cols.Length <= 6) continue;
                if (cols.Length > 1 && !string.Equals(cols[1].Trim(), location, StringComparison.OrdinalIgnoreCase)) continue;
                string likelyClass = cols[4].Trim().ToLower();
                if (likelyClass == "bird" || likelyClass == "no_fish" || likelyClass == "n/a") continue;
                string direction = cols[6].Trim().ToLower();
                if (direction == "upstream") upstreamCount++;
                else if (direction == "downstream") downstreamCount++;
            }

            int net = upstreamCount - downstreamCount;
            sessionNetUpstreamText.Text = $"{net}";
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

                var currentTrack = (_currentTracks.Count > _currentTrackIndex)
                    ? _currentTracks[_currentTrackIndex]
                    : null;
                string startTimeSec = currentTrack?.StartTime ?? string.Empty;
                string sourceRun = currentTrack?.Run;
                if (string.IsNullOrWhiteSpace(sourceRun))
                    sourceRun = (Application.Current as App)?.ActiveRun ?? string.Empty;

                string runMasterPath = _pathResolver.ResolveRunCsvPath(sourceRun);
                bool fishRowExists = File.Exists(runMasterPath) && UpdateCsvFile(runMasterPath, currentTrack, currentVideoName, startTimeSec);
                bool trackIsNoFish = IsNoFishLikelyClass(currentTrack?.LikelyClass);

                if (!fishRowExists && !trackIsNoFish)
                {
                    MessageBox.Show(
                        "This track was not found in the run master CSV. No changes were saved.",
                        "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!fishRowExists && trackIsNoFish)
                    HandleNoFishCsvUpdate(currentTrack, currentVideoName, sourceRun);

                string allHistoryPath = _pathResolver.ResolveAllTimeMasterFishCsvPath();
                if (File.Exists(allHistoryPath))
                    UpdateCsvFile(allHistoryPath, currentTrack, currentVideoName, startTimeSec);

                string sessionPath = _pathResolver.ResolveSessionCsvPath(sourceRun);
                if (File.Exists(sessionPath))
                    UpdateCsvFile(sessionPath, currentTrack, currentVideoName, startTimeSec);

                if (currentTrack != null)
                    currentTrack.Run = sourceRun;

                var _saveApp = Application.Current as App;
                if (_saveApp != null && currentTrack != null)
                {
                    var dbTrack = new FishLens_App.Models.Video
                    {
                        VideoFilePath = currentTrack.VideoFilePath,
                        Name = currentVideoName,
                        Run = sourceRun,
                        Location = currentTrack.Location,
                        LikelyClass = GetFishPresentClass(),
                        Direction = GetTravelDirectionValue(),
                        Species = fishSpecies.Text.Trim(),
                        StartTime = startTimeSec,
                        EndTime = currentTrack.EndTime,
                        DetectionTimestamp = currentTrack.DetectionTimestamp,
                        AvgConfidence = ParseConfidenceText(fishPresentConfidence.Text),
                        SpeciesConfidence = ParseConfidenceText(fishSpeciesConfidence.Text),
                    };
                    _ = System.Threading.Tasks.Task.Run(() =>
                        FishLens_App.Services.DbSyncService.UpsertTrackToDb(
                            dbTrack, _saveApp.CurrentOrganizationId, _saveApp.CurrentUserId, _saveApp.connectionString));
                }

                RefreshSessionOverview();

                double savedPresConf = ParseConfidenceText(fishPresentConfidence.Text);
                if (_currentTracks.Count > _currentTrackIndex && _currentTracks[_currentTrackIndex] != null)
                    _currentTracks[_currentTrackIndex].AvgConfidence = savedPresConf;

                double newLibConf = _currentTracks.Count > 0
                    ? _currentTracks.Min(t => t.AvgConfidence)
                    : savedPresConf;
                string savedVideoName = currentVideoName;
                string savedRun = sourceRun;
                string libSectionKey = null;

                foreach (var child in videoList.Children)
                {
                    if (child is Grid rowGrid
                        && rowGrid.Tag is string rowTag
                        && !rowTag.StartsWith("header:", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var elem in rowGrid.Children)
                        {
                            if (elem is Button btn
                                && btn.DataContext is FishLens_App.Models.Video libVid
                                && string.Equals(libVid.Name, savedVideoName, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(ResolveVideoRun(libVid), savedRun, StringComparison.OrdinalIgnoreCase))
                            {
                                libVid.AvgConfidence = newLibConf;
                                btn.Style = CreateButtonStyle(IsLowConfidence(newLibConf));
                                libSectionKey = rowTag;
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(libSectionKey))
                    ResortLibrarySection(libSectionKey);

                if (_currentTracks.Count > _currentTrackIndex && _currentTracks[_currentTrackIndex] != null)
                {
                    var savedTrack = _currentTracks[_currentTrackIndex];
                    savedTrack.LikelyClass = GetFishPresentClass();
                    savedTrack.Direction = GetTravelDirectionValue();
                    savedTrack.Species = fishSpecies.Text.Trim();
                    savedTrack.SpeciesConfidence = ParseConfidenceText(fishSpeciesConfidence.Text);
                    DisplayTrackInUi(savedTrack);
                }
                RefreshAnalysisBaseline();

                MessageBox.Show("Changes saved successfully!", "Save Successful",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                SetAnalysisStatusSaved();
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
        //              Returns true if the row was found and updated.
        // **************************************************
        private bool UpdateCsvFile(string csvPath, FishLens_App.Models.Video track, string videoFileName, string startTimeSec)
        {
            EnsureCsvHasRunColumn(csvPath, track?.Run ?? string.Empty);
            string[] lines = File.ReadAllLines(csvPath);
            string[] columns = null;
            int rowIndex = -1;
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
                    rowIndex = i;
                    break;
                }
            }

            if (columns == null || rowIndex < 0) return false;

            string updatedRow = CreateUpdatedCsvRow(columns);
            lines[rowIndex] = updatedRow;
            File.WriteAllLines(csvPath, lines);
            return true;
        }

        // **************************************************
        // Function: EnsureCsvHasRunColumn
        // Description: Upgrades older 10-column CSVs in place by appending a run column.
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

        // **************************************************
        // Function: CreateUpdatedCsvRow
        // Description: Creates updated CSV row from UI values
        // **************************************************
        private string CreateUpdatedCsvRow(string[] originalColumns)
        {
            string likelyClass = GetFishPresentClass();
            string direction = GetTravelDirectionValue();
            string species = fishSpecies.Text.Trim();

            string videoFile = originalColumns[0].Trim();
            string location = originalColumns.Length > 1 ? originalColumns[1].Trim() : string.Empty;
            string confidence = originalColumns.Length > 5 ? originalColumns[5].Trim() : string.Empty;
            string species_confidence = originalColumns.Length > 3 ? originalColumns[3].Trim() : string.Empty;
            string startTime = originalColumns.Length > 7 ? originalColumns[7].Trim() : string.Empty;
            string endTime = originalColumns.Length > 8 ? originalColumns[8].Trim() : string.Empty;
            string vidTimeStamp = originalColumns.Length > 9 ? originalColumns[9].Trim() : string.Empty;
            string run = originalColumns.Length > 10 ? originalColumns[10].Trim() : string.Empty;

            string presentConfText = fishPresentConfidence.Text.Trim();
            if (!string.IsNullOrEmpty(presentConfText) && presentConfText != "--")
            {
                string cleanValue = presentConfText.Replace("%", "").Trim();
                if (double.TryParse(cleanValue, out double presentConfValue))
                    confidence = (presentConfValue / 100).ToString("F4");
            }

            string speciesConfText = fishSpeciesConfidence.Text.Trim();
            if (!string.IsNullOrEmpty(speciesConfText) && speciesConfText != "--")
            {
                string cleanValue = speciesConfText.Replace("%", "").Trim();
                if (double.TryParse(cleanValue, out double speciesConfValue))
                    species_confidence = (speciesConfValue / 100).ToString("F4");
            }

            if (IsNoFishLikelyClass(likelyClass))
            {
                species = string.Empty;
                species_confidence = "0";
                confidence = "0";
                direction = string.Empty;
            }

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
                return string.Empty;

            return selectedItem.Content.ToString().ToLower();
        }

        // **************************************************
        // Function: IsNoFishLikelyClass
        // Description: Returns true when the likely class indicates no fish was detected
        // **************************************************
        private bool IsNoFishLikelyClass(string likelyClass)
        {
            return string.IsNullOrWhiteSpace(likelyClass)
                || likelyClass.Equals("not_fish", StringComparison.OrdinalIgnoreCase)
                || likelyClass.Equals("no_fish", StringComparison.OrdinalIgnoreCase)
                || likelyClass.Equals("N/A", StringComparison.OrdinalIgnoreCase);
        }

        // **************************************************
        // Function: HandleNoFishCsvUpdate
        // Description: Saves changes for a video that originated from the no-fish CSV.
        // **************************************************
        private void HandleNoFishCsvUpdate(FishLens_App.Models.Video track, string videoName, string sourceRun)
        {
            string newLikelyClass = GetFishPresentClass();
            bool convertingToFish = newLikelyClass.Equals("fish", StringComparison.OrdinalIgnoreCase);

            string videoFile = track?.VideoFilePath ?? videoName;

            string location = track?.Location ?? string.Empty;
            if (string.IsNullOrWhiteSpace(location) || location.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                string activeLocation = (Application.Current as App)?.ActiveLocation ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(activeLocation)
                    && !activeLocation.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    location = activeLocation;
                }
            }

            string timestamp = track?.DetectionTimestamp.HasValue == true
                ? track.DetectionTimestamp.Value.ToString("yyyy/MM/dd HH:mm:ss")
                : string.Empty;

            string[] syntheticCols = { videoFile, location, "", "0", newLikelyClass, "0", "", "0", "0", timestamp, sourceRun };
            string newRow = CreateUpdatedCsvRow(syntheticCols);

            if (convertingToFish)
            {
                AppendRowToCsv(_pathResolver.ResolveRunCsvPath(sourceRun), newRow);
                AppendRowToCsv(_pathResolver.ResolveAllTimeMasterFishCsvPath(), newRow);

                string sessionFishPath = _pathResolver.ResolveSessionCsvPath(sourceRun);
                if (File.Exists(sessionFishPath))
                    AppendRowToCsv(sessionFishPath, newRow);

                string noFishPath = _pathResolver.ResolveSessionNoFishCsvPath(sourceRun);
                FishLens_App.Services.CsvUtils.RemoveVideoFromCsv(noFishPath, videoName);

                if (track != null)
                {
                    track.LikelyClass = newLikelyClass;
                    track.Location = location;
                }
            }
        }

        // **************************************************
        // Function: AppendRowToCsv
        // Description: Appends a single data row to an existing CSV file.
        // **************************************************
        private void AppendRowToCsv(string csvPath, string row)
        {
            if (File.Exists(csvPath))
            {
                EnsureCsvHasRunColumn(csvPath, string.Empty);
                File.AppendAllText(csvPath, row + Environment.NewLine);
            }
        }

        // **************************************************
        // Function: ConfidenceTextBox_TextChanged
        // Description: Keeps the "%" suffix permanently visible in both confidence TextBoxes.
        // **************************************************
        private void ConfidenceTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingConfidenceText) return;
            if (sender is TextBox tb)
            {
                string text = tb.Text;
                if (!string.IsNullOrEmpty(text) && text != "--")
                {
                    string stripped = text.Replace("%", "");
                    string desired = stripped + "%";
                    if (text != desired)
                    {
                        _updatingConfidenceText = true;
                        int caretPos = Math.Min(tb.CaretIndex, stripped.Length);
                        tb.Text = desired;
                        tb.CaretIndex = caretPos;
                        _updatingConfidenceText = false;
                    }
                }
            }
        }

        // **************************************************
        // Function: ConfidenceTextBox_PreviewKeyDown
        // Description: Prevents accidental edits to the trailing "%" in a confidence TextBox.
        // **************************************************
        private void ConfidenceTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is TextBox tb && tb.Text.EndsWith("%"))
            {
                int lastEditPos = tb.Text.Length - 1;
                if (e.Key == System.Windows.Input.Key.End)
                {
                    tb.CaretIndex = lastEditPos;
                    e.Handled = true;
                }
                if (e.Key == System.Windows.Input.Key.Delete
                    && tb.SelectionLength == 0
                    && tb.CaretIndex >= lastEditPos)
                {
                    e.Handled = true;
                }
            }
        }

        // **************************************************
        // Function: FishPresentStatus_SelectionChanged
        // Description: Reacts when the user changes the fish-present dropdown.
        // **************************************************
        private void FishPresentStatus_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressStatusHandler) return;
            if (fishPresentStatus.SelectedIndex == 0)   // "Present"
            {
                string current = fishPresentConfidence.Text.Trim();
                if (string.IsNullOrEmpty(current) || current == "--")
                    fishPresentConfidence.Text = "100%";
                SetRingArc(fishPresentRingArc, fishPresentConfidence, ParseConfidenceText(fishPresentConfidence.Text) * 100);
            }
            else                                        // "Not Present"
            {
                fishPresentConfidence.Text = "--";
                fishTravelDirection.SelectedIndex = -1;
                fishSpecies.SelectedIndex = -1;
                fishSpecies.Text = string.Empty;
                fishSpeciesConfidence.Text = "--";
                ClearRing(fishPresentRingArc, fishPresentConfidence);
                ClearRing(fishSpeciesRingArc, fishSpeciesConfidence);
            }

            if (_suppressTrackFieldHandlers) return;
            if (_currentTrackIndex >= 0 && _currentTrackIndex < _currentTracks.Count)
            {
                _currentTracks[_currentTrackIndex].LikelyClass = GetFishPresentClass();
                _currentTracks[_currentTrackIndex].Direction = GetTravelDirectionValue();
                _currentTracks[_currentTrackIndex].Species = fishSpecies.Text.Trim();
                _currentTracks[_currentTrackIndex].AvgConfidence = ParseConfidenceText(fishPresentConfidence.Text);
                _currentTracks[_currentTrackIndex].SpeciesConfidence = ParseConfidenceText(fishSpeciesConfidence.Text);
                UpdateFishMarkers();
                MarkAnalysisDirty();
            }
        }

        private void FishTravelDirection_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressTrackFieldHandlers) return;
            if (_currentTrackIndex < 0 || _currentTrackIndex >= _currentTracks.Count) return;

            _currentTracks[_currentTrackIndex].Direction = GetTravelDirectionValue();
            UpdateFishMarkers();
            MarkAnalysisDirty();
        }

        private void FishSpecies_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MarkAnalysisDirty();
        }

        private void FishSpeciesTextChanged(object sender, TextChangedEventArgs e)
        {
            MarkAnalysisDirty();
        }

        private double ParseConfidenceText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text == "--") return 0.0;
            if (double.TryParse(text.Replace("%", "").Trim(), out double val))
                return val > 1 ? val / 100.0 : val;
            return 0.0;
        }

        #endregion

        #region UI Display

        private void ExpandSidebar()
        {
            _sidebarCollapsed = false;
            SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);

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
                    FillBehavior = FillBehavior.HoldEnd
                };

                collapseAnim.Completed += (s, e) =>
                {
                    SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
                    SidebarColumn.Width = new GridLength(106);

                    videoList.Visibility = Visibility.Collapsed;
                    deleteSelectedVideos.Visibility = Visibility.Collapsed;
                    changeLocationForSelected.Visibility = Visibility.Collapsed;
                    undoLastDelete.Visibility = Visibility.Collapsed;
                    analysisProgressArea.Visibility = Visibility.Collapsed;
                    SidebarDivider.Visibility = Visibility.Collapsed;
                };

                SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, collapseAnim);
            }
        }

        private static void SetRingArc(System.Windows.Shapes.Path arc, TextBlock label, double confidence)
        {
            var color = confidence >= 75 ? GetThemeColor("ConfidenceRingHighBrush", Color.FromRgb(0x0F, 0x6E, 0x56))
                      : confidence >= 45 ? GetThemeColor("ConfidenceRingMidBrush", Color.FromRgb(0xBA, 0x75, 0x17))
                                         : GetThemeColor("ConfidenceRingLowBrush", Color.FromRgb(0xC0, 0x39, 0x2B));
            var brush = new SolidColorBrush(color);
            arc.Stroke = brush;
            label.Foreground = brush;

            if (confidence <= 0) { arc.Data = Geometry.Empty; return; }

            double pct = Math.Min(confidence / 100.0, 0.9999);
            double angle = pct * 360.0 - 90.0;
            double rad = angle * Math.PI / 180.0;
            double cx = 22, cy = 22, r = 17;
            double ex = cx + r * Math.Cos(rad);
            double ey = cy + r * Math.Sin(rad);
            int large = pct >= 0.5 ? 1 : 0;

            arc.Data = Geometry.Parse(
                $"M {cx},{cy - r} A {r},{r},0,{large},1,{ex:F2},{ey:F2}");
        }

        private static void ClearRing(System.Windows.Shapes.Path arc, TextBlock label)
        {
            arc.Data = Geometry.Empty;
            label.Foreground = (Brush)Application.Current.Resources["AccentBrush"];
        }

        private static Brush GetThemeBrush(string resourceKey, Brush fallback = null)
        {
            return Application.Current?.TryFindResource(resourceKey) as Brush
                ?? fallback
                ?? Brushes.Transparent;
        }

        private static Color GetThemeColor(string resourceKey, Color fallback)
        {
            return Application.Current?.TryFindResource(resourceKey) is SolidColorBrush brush
                ? brush.Color
                : fallback;
        }

        private static string FormatPercentValue(double percent)
        {
            double clamped = Math.Max(0, Math.Min(100, percent));
            return $"{Math.Round(clamped, MidpointRounding.AwayFromZero):0}%";
        }

        private static string FormatConfidencePercent(double normalizedConfidence)
        {
            return FormatPercentValue(normalizedConfidence * 100);
        }

        private void ClearAnalysisPanel()
        {
            _suppressStatusHandler = true;
            _suppressTrackFieldHandlers = true;

            _currentTracks.Clear();
            _savedCurrentTracks.Clear();
            _currentTrackIndex = 0;
            videoName.Text = "--";
            videoLocation.Text = "--";
            videoDateTime.Text = "--";
            videoDuration.Text = "--";
            fishPresentStatus.SelectedIndex = -1;
            fishTravelDirection.SelectedIndex = -1;
            fishSpecies.SelectedIndex = -1;
            fishSpecies.Text = string.Empty;
            fishPresentConfidence.Text = "--";
            fishSpeciesConfidence.Text = "--";
            ClearRing(fishPresentRingArc, fishPresentConfidence);
            ClearRing(fishSpeciesRingArc, fishSpeciesConfidence);
            trackNavigator.Visibility = Visibility.Collapsed;
            prevFishButton.Visibility = Visibility.Collapsed;
            nextFishButton.Visibility = Visibility.Collapsed;
            trackLabel.Text = "Fish 0 / 0";
            fishEmojiCanvas.Children.Clear();

            var markerCanvas = videoScrubber.Template?.FindName("fishMarkersCanvas", videoScrubber) as Canvas;
            markerCanvas?.Children.Clear();

            _suppressTrackFieldHandlers = false;
            _suppressStatusHandler = false;
            ClearAnalysisSaveStatus();
            UpdateActionButtonState();
        }

        // **************************************************
        // Function: VideoButtonClick
        // Description: Displays selected video and its data
        // **************************************************
        private void VideoButtonClick(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscardUnsavedAnalysisChanges())
                return;

            Button clickedButton = (Button)sender;
            string videoPath = clickedButton.Tag.ToString();
            var sourceVideo = clickedButton.DataContext as FishLens_App.Models.Video;

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
        // **************************************************
        private string ConvertAsfToTempMp4(string asfPath)
        {
            string ffmpeg = null;
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                var candidate = System.IO.Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate)) { ffmpeg = candidate; break; }
            }
            if (ffmpeg == null) return asfPath;

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
                proc.WaitForExit(30_000);
                if (proc.ExitCode == 0 && File.Exists(tempPath) && new FileInfo(tempPath).Length > 0)
                    return tempPath;
            }
            catch { }
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            return asfPath;
        }

        private void LoadVideoInPlayer(string videoPath)
        {
            ResetVideoPlayer(showPlaceholder: false);

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
            _videoEnded = false;
            SetPlayPauseButtonIcon(isPlaying: true);

            placeholderPanel.Visibility = Visibility.Collapsed;
            videoControls.Visibility = Visibility.Visible;

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
            if (videoPlayer.NaturalDuration.HasTimeSpan)
                videoTotalTimeText.Text = FormatTime(videoPlayer.NaturalDuration.TimeSpan);

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
            _videoEnded = true;
            SetPlayPauseButtonIcon(isPlaying: false);
            videoPlayer.Stop();
            videoPlayer.Position = TimeSpan.Zero;
            videoScrubber.Value = 0;
            videoCurrentTimeText.Text = "0:00";
            if (videoPlayer.NaturalDuration.HasTimeSpan)
                videoTotalTimeText.Text = FormatTime(videoPlayer.NaturalDuration.TimeSpan);
        }

        private void ResetVideoPlayer(bool showPlaceholder)
        {
            _videoTimer?.Stop();
            _isPlaying = false;
            _videoEnded = false;
            _isDraggingScrubber = false;
            _suppressTimerTicks = 0;

            try { videoPlayer.Stop(); } catch { }
            videoPlayer.Source = null;
            CleanupPlaybackTemp();

            videoScrubber.Value = 0;
            videoCurrentTimeText.Text = "0:00";
            videoTotalTimeText.Text = "0:00";
            SetPlayPauseButtonIcon(isPlaying: false);

            if (showPlaceholder)
            {
                placeholderPanel.Visibility = Visibility.Visible;
                videoControls.Visibility = Visibility.Collapsed;
            }
        }

        private void StopVideoForNavigation()
        {
            if (videoPlayer == null) return;

            _videoTimer?.Stop();
            _isPlaying = false;
            _videoEnded = false;
            _isDraggingScrubber = false;
            _suppressTimerTicks = 0;

            try
            {
                videoPlayer.Stop();
                videoPlayer.Position = TimeSpan.Zero;
            }
            catch { }

            if (videoScrubber != null)
                videoScrubber.Value = 0;
            if (videoCurrentTimeText != null)
                videoCurrentTimeText.Text = "0:00";
            if (videoPlayer?.NaturalDuration.HasTimeSpan == true && videoTotalTimeText != null)
                videoTotalTimeText.Text = FormatTime(videoPlayer.NaturalDuration.TimeSpan);
            if (playPauseButton != null)
                SetPlayPauseButtonIcon(isPlaying: false);
        }

        private void SetTransportButtonIcons()
        {
            prevFishButton.Content = CreateTransportIcon("track-previous");
            skipBackButton.Content = CreateTransportIcon("skip-back");
            SetPlayPauseButtonIcon(isPlaying: _isPlaying);
            skipForwardButton.Content = CreateTransportIcon("skip-forward");
            nextFishButton.Content = CreateTransportIcon("track-next");
            trackPrevButton.Content = CreateNavigatorIcon(leftFacing: true);
            trackNextButton.Content = CreateNavigatorIcon(leftFacing: false);
        }

        public void RefreshTransportButtonIcons()
        {
            SetTransportButtonIcons();
            UpdateFishMarkers();
        }

        public void RefreshAnalysisThemeStyles()
        {
            if (_currentTrackIndex >= 0 && _currentTrackIndex < _currentTracks.Count)
            {
                RefreshAnalysisConfidenceRings();
            }
            else
            {
                ClearRing(fishPresentRingArc, fishPresentConfidence);
                ClearRing(fishSpeciesRingArc, fishSpeciesConfidence);
            }
        }

        private void RefreshAnalysisConfidenceRings()
        {
            bool fishPresent = fishPresentStatus.SelectedIndex == 0;

            if (fishPresent)
                SetRingArc(fishPresentRingArc, fishPresentConfidence, ParseConfidenceText(fishPresentConfidence.Text) * 100);
            else
                ClearRing(fishPresentRingArc, fishPresentConfidence);

            if (fishPresent && ParseConfidenceText(fishSpeciesConfidence.Text) > 0)
                SetRingArc(fishSpeciesRingArc, fishSpeciesConfidence, ParseConfidenceText(fishSpeciesConfidence.Text) * 100);
            else
                ClearRing(fishSpeciesRingArc, fishSpeciesConfidence);
        }

        private void SetPlayPauseButtonIcon(bool isPlaying)
        {
            if (playPauseButton == null) return;
            playPauseButton.Content = CreateTransportIcon(isPlaying ? "pause" : "play");
        }

        private FrameworkElement CreateTransportIcon(string iconName)
        {
            var canvas = new Canvas
            {
                Width = 24,
                Height = 24,
                IsHitTestVisible = false
            };

            switch (iconName)
            {
                case "pause":
                    canvas.Children.Add(CreateIconRect(8, 6, 3, 12));
                    canvas.Children.Add(CreateIconRect(13, 6, 3, 12));
                    break;
                case "skip-back":
                    canvas.Children.Add(CreateIconPath("M13,6 L7,12 L13,18 Z"));
                    canvas.Children.Add(CreateIconPath("M18,6 L12,12 L18,18 Z"));
                    break;
                case "skip-forward":
                    canvas.Children.Add(CreateIconPath("M6,6 L12,12 L6,18 Z"));
                    canvas.Children.Add(CreateIconPath("M11,6 L17,12 L11,18 Z"));
                    break;
                case "track-previous":
                    canvas.Children.Add(CreateIconRect(7, 6, 2, 12));
                    canvas.Children.Add(CreateIconPath("M17,6 L9,12 L17,18 Z"));
                    break;
                case "track-next":
                    canvas.Children.Add(CreateIconPath("M7,6 L15,12 L7,18 Z"));
                    canvas.Children.Add(CreateIconRect(15, 6, 2, 12));
                    break;
                default:
                    canvas.Children.Add(CreateIconPath("M8,6 L17,12 L8,18 Z"));
                    break;
            }

            return new Viewbox
            {
                Width = 18,
                Height = 18,
                Stretch = Stretch.Uniform,
                Child = canvas
            };
        }

        private static FrameworkElement CreateNavigatorIcon(bool leftFacing)
        {
            var canvas = new Canvas
            {
                Width = 12,
                Height = 12,
                IsHitTestVisible = false
            };
            canvas.Children.Add(CreateIconPath(leftFacing ? "M8,2 L3,6 L8,10 Z" : "M4,2 L9,6 L4,10 Z"));

            return new Viewbox
            {
                Width = 12,
                Height = 12,
                Stretch = Stretch.Uniform,
                Child = canvas
            };
        }

        private static System.Windows.Shapes.Rectangle CreateIconRect(double left, double top, double width, double height)
        {
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = height,
                RadiusX = 0.8,
                RadiusY = 0.8
            };
            BindShapeFillToButtonForeground(rect);
            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, top);
            return rect;
        }

        private static System.Windows.Shapes.Path CreateIconPath(string data)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(data)
            };
            BindShapeFillToButtonForeground(path);
            return path;
        }

        private static void BindShapeFillToButtonForeground(System.Windows.Shapes.Shape shape)
        {
            BindingOperations.SetBinding(shape, System.Windows.Shapes.Shape.FillProperty, new Binding("Foreground")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1)
            });
        }

        // **************************************************
        // Function: VideoTimer_Tick
        // Description: Updates scrubber and time readout while playing
        // **************************************************
        private void VideoTimer_Tick(object sender, EventArgs e)
        {
            if (_isDraggingScrubber) return;
            if (_suppressTimerTicks > 0) { _suppressTimerTicks--; return; }
            if (!videoPlayer.NaturalDuration.HasTimeSpan) return;

            double total = videoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            double pos = videoPlayer.Position.TotalSeconds;
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
                SetPlayPauseButtonIcon(isPlaying: false);
            }
            else
            {
                if (_videoEnded || (videoPlayer.NaturalDuration.HasTimeSpan &&
                    videoPlayer.Position >= videoPlayer.NaturalDuration.TimeSpan.Subtract(TimeSpan.FromMilliseconds(100))))
                {
                    videoPlayer.Stop();
                    videoPlayer.Position = TimeSpan.Zero;
                    videoScrubber.Value = 0;
                    videoCurrentTimeText.Text = "0:00";
                    _suppressTimerTicks = 0;
                    _videoEnded = false;
                }

                videoPlayer.Play();
                _isPlaying = true;
                SetPlayPauseButtonIcon(isPlaying: true);
                _videoTimer?.Start();
            }
        }

        private void SkipBackButton_Click(object sender, RoutedEventArgs e) => SkipSeconds(-1.0);
        private void SkipForwardButton_Click(object sender, RoutedEventArgs e) => SkipSeconds(1.0);

        private void SkipSeconds(double seconds)
        {
            if (!videoPlayer.NaturalDuration.HasTimeSpan) return;
            double total = videoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            double newPos = Math.Max(0.0, Math.Min(total, videoPlayer.Position.TotalSeconds + seconds));
            _videoEnded = false;
            videoPlayer.Position = TimeSpan.FromSeconds(newPos);
            videoScrubber.Value = total > 0 ? newPos / total : 0;
            videoCurrentTimeText.Text = FormatTime(TimeSpan.FromSeconds(newPos));
            _suppressTimerTicks = 3;
        }

        // **************************************************
        // Function: Window_PreviewKeyDown
        // Description: Space = play/pause, Left/Right arrow = skip +/-1s.
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
            const double thumbHalf = 7.0;
            double trackW = Math.Max(1.0, w - 2 * thumbHalf);
            double ratio = Math.Max(0.0, Math.Min(1.0, (x - thumbHalf) / trackW));
            videoScrubber.Value = ratio;
            double total = videoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            videoPlayer.Position = TimeSpan.FromSeconds(ratio * total);
            videoCurrentTimeText.Text = FormatTime(videoPlayer.Position);
            _videoEnded = false;
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

        private static string FormatTime(TimeSpan t)
        {
            return $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
        }

        private static Brush GetDirectionBrush(string direction)
        {
            string key = direction switch
            {
                "upstream" => "DirectionUpstreamBrush",
                "downstream" => "DirectionDownstreamBrush",
                _ => "DirectionUnknownBrush"
            };

            return (Brush)Application.Current.Resources[key];
        }

        private static FrameworkElement CreateFishTrackMarker(string direction, string upstreamScreenDirection, Brush brush, double opacity)
        {
            var canvas = new Canvas
            {
                Width = 26,
                Height = 12,
                Opacity = opacity,
                IsHitTestVisible = false
            };

            bool? facesLeft = direction switch
            {
                "upstream" => upstreamScreenDirection != "right",
                "downstream" => upstreamScreenDirection == "right",
                _ => null
            };

            if (facesLeft == true)
            {
                canvas.Children.Add(CreateMarkerArrow(brush, facesLeft: true, left: 0));
                canvas.Children.Add(CreateMarkerFish(brush, facesLeft: true, left: 8));
            }
            else if (facesLeft == false)
            {
                canvas.Children.Add(CreateMarkerFish(brush, facesLeft: false, left: 0));
                canvas.Children.Add(CreateMarkerArrow(brush, facesLeft: false, left: 18));
            }
            else
            {
                canvas.Children.Add(CreateMarkerFish(brush, facesLeft: false, left: 4));
            }

            return canvas;
        }

        private static UIElement CreateMarkerFish(Brush brush, bool facesLeft, double left)
        {
            var outline = GetThemeBrush("ScrubMarkerOutlineBrush", Brushes.White);
            var group = new Canvas
            {
                Width = 18,
                Height = 12,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = facesLeft ? new ScaleTransform(-1, 1) : Transform.Identity
            };

            var body = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M3,6 C5,2.8 10.8,2.8 14,6 C10.8,9.2 5,9.2 3,6 Z"),
                Fill = brush,
                Stroke = outline,
                StrokeThickness = 0.7
            };
            group.Children.Add(body);

            var tail = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M3,6 L0,3 L0,9 Z"),
                Fill = brush,
                Stroke = outline,
                StrokeThickness = 0.7
            };
            group.Children.Add(tail);

            var eye = new System.Windows.Shapes.Ellipse
            {
                Width = 1.4,
                Height = 1.4,
                Fill = (Brush)Application.Current.Resources["OnAccentForeground"],
                Opacity = 0.75
            };
            Canvas.SetLeft(eye, 11.2);
            Canvas.SetTop(eye, 5.1);
            group.Children.Add(eye);

            Canvas.SetLeft(group, left);
            return group;
        }

        private static UIElement CreateMarkerArrow(Brush brush, bool facesLeft, double left)
        {
            var outline = GetThemeBrush("ScrubMarkerOutlineBrush", Brushes.White);
            var path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(facesLeft ? "M6,2 L0,6 L6,10 Z" : "M0,2 L6,6 L0,10 Z"),
                Fill = brush,
                Stroke = outline,
                StrokeThickness = 0.7
            };
            Canvas.SetLeft(path, left);
            Canvas.SetTop(path, 1);
            return path;
        }

        // **************************************************
        // Function: UpdateFishMarkers
        // Description: Draws one colored range bar per fish track onto the scrubber canvas.
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

            canvas.Children.Clear();
            fishEmojiCanvas.Children.Clear();

            const double thumbHalf = 7.0;
            double trackW = Math.Max(1.0, w - 2 * thumbHalf);
            string upstreamScreenDirection = GetUpstreamDirectionForActiveLocation();

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
                var markerBrush = GetDirectionBrush(dir);

                double startX = thumbHalf + (fStart / total) * trackW;
                double endX = thumbHalf + (fEnd / total) * trackW;
                double barW = Math.Max(4.0, endX - startX);

                var bar = new System.Windows.Shapes.Rectangle
                {
                    Height = 4,
                    Width = barW,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = markerBrush,
                    Stroke = GetThemeBrush("ScrubMarkerOutlineBrush", Brushes.White),
                    StrokeThickness = 0.5,
                    Opacity = opacity,
                };
                Canvas.SetLeft(bar, startX);
                canvas.Children.Add(bar);

                if (barW >= 14)
                {
                    double midX = startX + barW / 2.0;
                    var marker = CreateFishTrackMarker(dir, upstreamScreenDirection, markerBrush, opacity);
                    double markerWidth = marker.Width;
                    double markerLeft = Math.Max(thumbHalf, Math.Min(w - thumbHalf - markerWidth, midX - markerWidth / 2.0));
                    Canvas.SetLeft(marker, markerLeft);
                    Canvas.SetTop(marker, 0);
                    fishEmojiCanvas.Children.Add(marker);
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
            RefreshAnalysisBaseline();
            _currentTrackIndex = 0;
            DisplayTrackInUi(_currentTracks[0]);
            UpdateTrackNavigator();
        }

        // **************************************************
        // Function: DisplayTrackInUi
        // Description: Populates the analysis panel from a single Video/track object.
        // **************************************************
        private void DisplayTrackInUi(FishLens_App.Models.Video vid)
        {
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
            videoDateTime.Text = vid.DetectionTimestamp.HasValue
                ? vid.DetectionTimestamp.Value.ToString("yyyy/MM/dd HH:mm:ss")
                : string.IsNullOrWhiteSpace(vid.Date) ? "--" : vid.Date;
            videoDuration.Text = $"{vid.StartTime}s - {vid.EndTime}s";

            bool fishPresent = !string.IsNullOrWhiteSpace(vid.LikelyClass)
                && !vid.LikelyClass.Equals("not_fish", StringComparison.OrdinalIgnoreCase)
                && !vid.LikelyClass.Equals("no_fish", StringComparison.OrdinalIgnoreCase)
                && !vid.LikelyClass.Equals("N/A", StringComparison.OrdinalIgnoreCase);
            _suppressStatusHandler = true;
            _suppressTrackFieldHandlers = true;
            fishPresentStatus.SelectedIndex = fishPresent ? 0 : 1;
            _suppressStatusHandler = false;
            fishPresentConfidence.Text = fishPresent ? FormatConfidencePercent(vid.AvgConfidence) : "--";
            if (fishPresent)
                SetRingArc(fishPresentRingArc, fishPresentConfidence, vid.AvgConfidence * 100);
            else
                ClearRing(fishPresentRingArc, fishPresentConfidence);

            string dirLower = (vid.Direction ?? string.Empty).ToLower().Trim();
            fishTravelDirection.SelectedIndex = fishPresent
                ? (dirLower == "upstream" ? 0 : dirLower == "downstream" ? 1 : 2)
                : -1;

            if (fishPresent)
            {
                string speciesDisplay = string.IsNullOrWhiteSpace(vid.Species)
                    || vid.Species.Equals("No data", StringComparison.OrdinalIgnoreCase)
                    ? "No data"
                    : CapitalizeFirstLetter(vid.Species);
                fishSpecies.Text = speciesDisplay;
                double speciesPct = vid.SpeciesConfidence * 100;
                fishSpeciesConfidence.Text = vid.SpeciesConfidence > 0 ? FormatConfidencePercent(vid.SpeciesConfidence) : "--";
                if (vid.SpeciesConfidence > 0)
                    SetRingArc(fishSpeciesRingArc, fishSpeciesConfidence, speciesPct);
                else
                    ClearRing(fishSpeciesRingArc, fishSpeciesConfidence);
            }
            else
            {
                fishSpecies.SelectedIndex = -1;
                fishSpecies.Text = string.Empty;
                fishSpeciesConfidence.Text = "--";
                ClearRing(fishSpeciesRingArc, fishSpeciesConfidence);
            }
            _suppressTrackFieldHandlers = false;

            UpdateFishMarkers();
            ClearAnalysisSaveStatus();
            UpdateActionButtonState();
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
            trackPrevButton.IsEnabled = _currentTrackIndex > 0;
            trackNextButton.IsEnabled = _currentTrackIndex < total - 1;
            prevFishButton.IsEnabled = _currentTrackIndex > 0;
            nextFishButton.IsEnabled = _currentTrackIndex < total - 1;
        }

        private void TrackPrevClick(object sender, RoutedEventArgs e)
        {
            if (_currentTrackIndex > 0)
            {
                if (!ConfirmDiscardUnsavedAnalysisChanges())
                    return;

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
                if (!ConfirmDiscardUnsavedAnalysisChanges())
                    return;

                _currentTrackIndex++;
                DisplayTrackInUi(_currentTracks[_currentTrackIndex]);
                UpdateTrackNavigator();
                SeekToCurrentTrack();
            }
        }

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

            TextBox textBox = new TextBox();
            textBox.Text = displayText ?? GetSectionDisplayText(sectionContext);
            textBox.Foreground = GetThemeBrush("SidebarTextBrush", Brushes.White);
            textBox.Background = Brushes.Transparent;
            textBox.BorderThickness = new Thickness(0);
            textBox.IsReadOnly = true;

            CheckBox folderCheckBox = new CheckBox();
            folderCheckBox.Padding = new Thickness(5);
            folderCheckBox.VerticalAlignment = VerticalAlignment.Center;

            string thisSectionKey = sectionContext.SectionKey;
            folderNameGrid.Tag = GetHeaderTag(thisSectionKey);

            folderCheckBox.Checked += (s, e) =>
            {
                foreach (var child in videoList.Children)
                {
                    if (child is Grid g && g.Tag is string t && t == thisSectionKey)
                    {
                        var btn = g.Children.OfType<Button>().FirstOrDefault();
                        var innerGrid = btn?.Content as Grid;
                        var cb = innerGrid?.Children.OfType<CheckBox>()
                                                    .FirstOrDefault(c => c.Tag as string == "selectionCheck");
                        if (cb != null) cb.IsChecked = true;
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
                        var btn = g.Children.OfType<Button>().FirstOrDefault();
                        var innerGrid = btn?.Content as Grid;
                        var cb = innerGrid?.Children.OfType<CheckBox>()
                                                    .FirstOrDefault(c => c.Tag as string == "selectionCheck");
                        if (cb != null) cb.IsChecked = false;
                    }
                }
                UpdateActionButtonState();
            };

            Grid.SetColumn(folderCheckBox, 1);
            folderNameGrid.Children.Add(textBox);
            folderNameGrid.Children.Add(folderCheckBox);
            videoList.Children.Add(folderNameGrid);

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
            bool isLow = IsLowConfidence(videoData.AvgConfidence);
            var tierBrush = new SolidColorBrush(tierColor);

            var grid = new Grid { Margin = new Thickness(0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });

            var stripe = new Border
            {
                Tag = "stripe",
                Background = tierBrush,
                CornerRadius = new CornerRadius(0),
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            Grid.SetColumn(stripe, 0);
            grid.Children.Add(stripe);

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
                Foreground = GetThemeBrush("SidebarTextBrush", Brushes.White),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            textStack.Children.Add(new TextBlock
            {
                Text = metaText,
                FontSize = 11,
                Foreground = GetThemeBrush("SidebarMutedTextBrush", Brushes.White),
                Margin = new Thickness(0, 1, 0, 0),
            });
            Grid.SetColumn(textStack, 2);
            grid.Children.Add(textStack);

            var pctBlock = new TextBlock
            {
                Tag = "pct",
                Text = FormatConfidencePercent(videoData.AvgConfidence),
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = tierBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
            };
            Grid.SetColumn(pctBlock, 3);
            grid.Children.Add(pctBlock);

            var checkBox = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Opacity = 0,
                Tag = "selectionCheck",
            };
            Grid.SetColumn(checkBox, 4);
            grid.Children.Add(checkBox);

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
                GetThemeBrush("SidebarHoverBackgroundBrush", new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))), "bd"));

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
                    ? GetThemeBrush("LibraryLowConfidenceBackground", new SolidColorBrush(Color.FromArgb(30, 0xE2, 0x4B, 0x4A)))
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
                foreach (var elem in rowGrid.Children)
                {
                    if (elem is Button button && button.DataContext is FishLens_App.Models.Video video)
                    {
                        var tierColor = GetTierColor(video.AvgConfidence);
                        var tierBrush = new SolidColorBrush(tierColor);
                        bool isLow = IsLowConfidence(video.AvgConfidence);

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
                            ? GetThemeBrush("LibraryLowConfidenceBackground", new SolidColorBrush(Color.FromArgb(30, 0xE2, 0x4B, 0x4A)))
                            : Brushes.Transparent;
                    }
                }
            }
        }

        // **************************************************
        // Function: ResortLibrarySection
        // Description: Re-orders the video item rows within a single library section
        //              so they stay sorted ascending by AvgConfidence after a save.
        // **************************************************
        private void ResortLibrarySection(string sectionKey)
        {
            var sectionItems = new List<Grid>();
            foreach (var child in videoList.Children)
            {
                if (child is Grid g
                    && g.Tag is string t
                    && !t.StartsWith("header:", StringComparison.OrdinalIgnoreCase)
                    && t.Equals(sectionKey, StringComparison.OrdinalIgnoreCase))
                {
                    sectionItems.Add(g);
                }
            }

            if (sectionItems.Count <= 1) return;

            var sorted = sectionItems
                .OrderBy(g =>
                {
                    double conf = 0;
                    bool confFound = false;
                    foreach (var elem in g.Children)
                    {
                        if (!confFound && elem is Button b && b.DataContext is FishLens_App.Models.Video v)
                        {
                            conf = v.AvgConfidence;
                            confFound = true;
                        }
                    }
                    return conf;
                })
                .ToList();

            bool alreadySorted = true;
            int checkIdx = 0;
            while (checkIdx < sectionItems.Count && alreadySorted)
            {
                if (!ReferenceEquals(sectionItems[checkIdx], sorted[checkIdx]))
                    alreadySorted = false;
                checkIdx++;
            }

            if (alreadySorted) return;

            foreach (var g in sectionItems)
                videoList.Children.Remove(g);

            int insertAt = -1;
            int headerSearch = 0;
            while (headerSearch < videoList.Children.Count && insertAt < 0)
            {
                if (videoList.Children[headerSearch] is Grid hg
                    && hg.Tag is string ht
                    && ht.Equals(GetHeaderTag(sectionKey), StringComparison.OrdinalIgnoreCase))
                {
                    insertAt = headerSearch + 2;
                }
                headerSearch++;
            }

            if (insertAt >= 0)
            {
                for (int i = 0; i < sorted.Count; i++)
                    videoList.Children.Insert(insertAt + i, sorted[i]);
            }
            else
            {
                foreach (var g in sorted)
                    videoList.Children.Add(g);
            }
        }

        #endregion

        #region Button Styling

        private Color GetTierColor(double confidence)
        {
            double threshold = (Application.Current as App)?.Configuration?.ConfidenceThreshold
                ?? _config?.ConfidenceThreshold
                ?? DEFAULT_CONFIDENCE_THRESHOLD;
            if (confidence >= threshold) return GetThemeColor("LibraryConfidenceHighBrush", Color.FromRgb(0x5D, 0xCA, 0xA5));
            if (confidence >= threshold * 0.6) return GetThemeColor("LibraryConfidenceMidBrush", Color.FromRgb(0xEF, 0x9F, 0x27));
            return GetThemeColor("LibraryConfidenceLowBrush", Color.FromRgb(0xE2, 0x4B, 0x4A));
        }

        private Style CreateButtonStyle(bool isLowConfidence)
        {
            // Styling is built directly into CreateSingleVideoButton; this is a no-op stub kept for call-site compatibility.
            return new Style(typeof(Button));
        }

        #endregion

        private void MainFrame_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {

        }


    }
}
