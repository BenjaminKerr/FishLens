using FishLens_App.Models;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace FishLens_App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // Private sets so they aren't accidentally replaced with new instances
        public CheckBoxToggle CheckBoxes { get; private set; }

        public string connectionString =
        "server=aura.cset.oit.edu,5433; " +
        "database=kaharra; " +
        "UID=kaharra; " +
        "password=kaharra";
        public AppConfiguration Configuration { get; private set; }
        public int CurrentUserId { get; set; }
        public string CurrentUsername { get; set; }
        public int CurrentRoleId { get; set; }
        public int CurrentOrganizationId { get; set; }

        public bool IsAdmin => CurrentRoleId == 1;
        public bool IsUser => CurrentRoleId == 2;

        // Active location used when launching Python processes
        public string ActiveLocation { get; set; } = "Unknown";

        // Active run name (e.g. "Spring 2026"); empty string means no run selected
        public string ActiveRun { get; set; } = string.Empty;

        // Raised when the user saves a Fast Mode change in Settings so MainWindow can restart Python
        public static event Action FastModeChanged;
        public static void RaiseFastModeChanged() => FastModeChanged?.Invoke();

        // Raised when the active location changes so MainWindow can refresh its dropdown
        public static event Action LocationChanged;
        public static void RaiseLocationChanged() => LocationChanged?.Invoke();

        // Raised when the active run changes so MainWindow and History can refresh
        public static event Action RunChanged;
        public static void RaiseRunChanged() => RunChanged?.Invoke();

        // Raised when the saved confidence threshold changes so existing library items can retint
        public static event Action ConfidenceThresholdChanged;
        public static void RaiseConfidenceThresholdChanged() => ConfidenceThresholdChanged?.Invoke();

        // Raised when YOLO analysis starts or stops so other pages can lock/unlock their controls
        public static bool IsAnalyzing { get; private set; }
        public static event Action<bool> AnalysisStateChanged;
        public static void RaiseAnalysisStateChanged(bool isAnalyzing)
        {
            IsAnalyzing = isAnalyzing;
            AnalysisStateChanged?.Invoke(isAnalyzing);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            CheckBoxes = new CheckBoxToggle();
            Configuration = new AppConfiguration();

            // Startup only pulls JSON-backed runtime settings. User/org visual settings
            // like High Contrast and Large Text are populated from the database at sign-in.
            NormalizeConfigurationState();
            ActiveLocation = Configuration.ActiveLocation;
            ActiveRun = Configuration.ActiveRun;

            // Ensure the All History folder exists for seasonal run archiving.
            try
            {
                string projectRoot = Path.GetFullPath(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
                string allHistoryDir = Path.Combine(projectRoot, "All History");
                Directory.CreateDirectory(allHistoryDir);
            }
            catch { /* non-critical */ }

            EnsureRunStorageInitialized();
        }


        // ****************************************************************
        // Function: ResetSettingsToDefaults
        // Description: Clears the current user session and resets all settings to their
        //     default values, then applies them to the UI. Called on sign-out.
        public void ResetSettingsToDefaults()
        {
            CurrentUserId = 0;
            CurrentUsername = null;
            CurrentRoleId = 0;
            CurrentOrganizationId = 0;

            Configuration.ConfidenceThreshold = 0.7;
            Configuration.HighContrastMode = false;
            Configuration.LargeText = false;
            CheckBoxes.FastMode = false;
            NormalizeConfigurationState();
            ActiveLocation = Configuration.ActiveLocation;
            ActiveRun = Configuration.ActiveRun;

            ApplyCurrentSettings();
        }

        // ****************************************************************
        // Function: ApplyCurrentSettings
        // Description: Applies in-memory configuration (theme, font sizes) to the running
        //     application. Called after sign-in, settings save, and sign-out reset.
        public void ApplyCurrentSettings()
        {
            ThemeHelper.ApplyHighContrastMode(Configuration.HighContrastMode);

            if (Configuration.LargeText)
            {
                Resources["BaseFontSize"] = 18.0;
                Resources["HeaderFontSize"] = 32.0;
                Resources["LargeHeaderFontSize"] = 42.0;
                Resources["TitleFontSize"] = 72.0;
            }
            else
            {
                Resources["BaseFontSize"] = 14.0;
                Resources["HeaderFontSize"] = 24.0;
                Resources["LargeHeaderFontSize"] = 32.0;
                Resources["TitleFontSize"] = 56.0;
            }
        }

        // ****************************************************************
        // Function: EnsureRunStorageInitialized
        // Description: Creates All History folder structure and CSV files for all configured
        //     runs. Called after successful sign-in so run storage is ready before analysis.
        public void EnsureRunStorageInitialized()
        {
            try
            {
                NormalizeConfigurationState();
                string allHistoryDir = Path.Combine(GetProjectRoot(), "All History");
                Directory.CreateDirectory(allHistoryDir);
                CleanupFishImages();
                EnsureCsvFile(Path.Combine(allHistoryDir, "all_history.csv"), FishCsvHeader);

                EnsureRunStorageExists("debug", isDebugRun: true);
                foreach (var run in Configuration.Runs.Where(r => !string.IsNullOrWhiteSpace(r.Name)))
                    EnsureRunStorageExists(run.Name, isDebugRun: false);
            }
            catch { /* non-critical */ }
        }

        private void EnsureRunStorageExists(string runName, bool isDebugRun)
        {
            string runFolder = Path.Combine(GetProjectRoot(), "All History", runName);
            Directory.CreateDirectory(runFolder);

            if (isDebugRun)
            {
                EnsureCsvFile(Path.Combine(runFolder, "debug.csv"), FishCsvHeader);
                return;
            }

            EnsureCsvFile(Path.Combine(runFolder, "session_fish.csv"), FishCsvHeader);
            EnsureCsvFile(Path.Combine(runFolder, "session_no_fish.csv"), NoFishCsvHeader);
            EnsureCsvFile(Path.Combine(runFolder, "run_master.csv"), FishCsvHeader);
            EnsureCsvFile(Path.Combine(GetProjectRoot(), "All History", "all_history.csv"), FishCsvHeader);
        }

        private static void EnsureCsvFile(string path, string header)
        {
            if (File.Exists(path)) return;
            File.WriteAllText(path, header + Environment.NewLine);
        }

        private void NormalizeConfigurationState()
        {
            Configuration ??= new AppConfiguration();

            if (Configuration.Locations == null || Configuration.Locations.Count == 0)
            {
                Configuration.Locations = new System.Collections.Generic.List<LocationEntry>
                {
                    new LocationEntry { Name = "Unknown", UpstreamDirection = "left" }
                };
            }

            if (string.IsNullOrWhiteSpace(Configuration.ActiveLocation) ||
                !Configuration.Locations.Any(l => string.Equals(l.Name, Configuration.ActiveLocation, StringComparison.OrdinalIgnoreCase)))
            {
                Configuration.ActiveLocation = Configuration.Locations.First().Name;
            }

            Configuration.Runs ??= new System.Collections.Generic.List<RunEntry>();
            Configuration.Runs = Configuration.Runs
                .Where(r => !string.IsNullOrWhiteSpace(r?.Name))
                .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (!string.IsNullOrWhiteSpace(Configuration.ActiveRun) &&
                !string.Equals(Configuration.ActiveRun, "debug", StringComparison.OrdinalIgnoreCase) &&
                !Configuration.Runs.Any(r => string.Equals(r.Name, Configuration.ActiveRun, StringComparison.OrdinalIgnoreCase)))
            {
                Configuration.ActiveRun = string.Empty;
            }
        }

        private static void CleanupFishImages()
        {
            try
            {
                string fishImagesDir = Path.Combine(GetProjectRoot(), "fish_images");
                if (!Directory.Exists(fishImagesDir))
                    return;

                DateTime cutoffUtc = DateTime.UtcNow.AddDays(-3);
                var imageFiles = Directory
                    .EnumerateFiles(fishImagesDir, "*.jpg", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path))
                    .Where(file => file.Exists)
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ToList();

                foreach (var file in imageFiles.Where(file => file.LastWriteTimeUtc < cutoffUtc))
                    TryDeleteFile(file);

                imageFiles = Directory
                    .EnumerateFiles(fishImagesDir, "*.jpg", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path))
                    .Where(file => file.Exists)
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ToList();

                const int maxImages = 50;
                foreach (var file in imageFiles.Skip(maxImages))
                    TryDeleteFile(file);

                RemoveEmptyDirectories(fishImagesDir);
            }
            catch { /* non-critical */ }
        }

        private static void TryDeleteFile(FileInfo file)
        {
            try { if (file.Exists) file.Delete(); }
            catch { /* best effort only */ }
        }

        private static void RemoveEmptyDirectories(string rootPath)
        {
            try
            {
                foreach (string directory in Directory.GetDirectories(rootPath))
                {
                    RemoveEmptyDirectories(directory);
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                        Directory.Delete(directory, recursive: false);
                }
            }
            catch { /* non-critical */ }
        }

        private static string GetProjectRoot()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            return Directory.GetParent(appDir)?.Parent?.Parent?.Parent?.Parent?.FullName ?? appDir;
        }

        private const string FishCsvHeader =
            "video_file,location,species,species_confidence,likely_class,confidence,direction,start_time_sec,end_time_sec,video_timestamp,run";

        private const string NoFishCsvHeader =
            "video_file,location,video_timestamp";
    }
}
