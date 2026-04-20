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

            // Read persisted settings now so FastMode is correct before Python starts.
            // Settings.LoadSettings does the same read later, but by then Python may already
            // have launched with the wrong FISHLENS_FAST_MODE value.
            try
            {
                // Resolve project root the same way DefaultProjectPathResolver does
                // (4 levels up from BaseDirectory: bin/Debug/net*/):
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string projectRoot = System.IO.Directory.GetParent(appDir)?.Parent?.Parent?.Parent?.Parent?.FullName ?? appDir;
                string configPath = Path.Combine(projectRoot, "appsettings.json");
                if (File.Exists(configPath))
                {
                    using var stream = File.OpenRead(configPath);
                    using var doc = JsonDocument.Parse(stream);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("ConfidenceThreshold", out var ctEl) &&
                        ctEl.ValueKind == JsonValueKind.Number)
                        Configuration.ConfidenceThreshold = ctEl.GetDouble();
                    if (root.TryGetProperty("FastMode", out var fmEl) &&
                        (fmEl.ValueKind == JsonValueKind.True || fmEl.ValueKind == JsonValueKind.False))
                        CheckBoxes.FastMode = fmEl.GetBoolean();
                    if (root.TryGetProperty("ActiveLocation", out var alEl) &&
                        alEl.ValueKind == JsonValueKind.String)
                        ActiveLocation = alEl.GetString() ?? "Unknown";
                    if (root.TryGetProperty("ActiveRun", out var arEl) &&
                        arEl.ValueKind == JsonValueKind.String)
                        ActiveRun = arEl.GetString() ?? string.Empty;
                    if (root.TryGetProperty("Runs", out var runsEl) &&
                        runsEl.ValueKind == JsonValueKind.Array)
                    {
                        var runs = new System.Collections.Generic.List<RunEntry>();
                        foreach (var runEl in runsEl.EnumerateArray())
                        {
                            string runName = runEl.TryGetProperty("Name", out var nameEl)
                                ? nameEl.GetString() ?? string.Empty
                                : string.Empty;
                            bool locked = runEl.TryGetProperty("Locked", out var lockedEl) &&
                                lockedEl.ValueKind == JsonValueKind.True;
                            if (!string.IsNullOrWhiteSpace(runName))
                                runs.Add(new RunEntry { Name = runName, Locked = locked });
                        }
                        Configuration.Runs = runs;
                    }
                }
            }
            catch { /* non-critical; defaults will be used */ }

            // Ensure the All History folder exists for seasonal run archiving.
            try
            {
                string projectRoot = Path.GetFullPath(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
                string allHistoryDir = Path.Combine(projectRoot, "All History");
                Directory.CreateDirectory(allHistoryDir);
            }
            catch { /* non-critical */ }
        }

        public void EnsureRunStorageInitialized()
        {
            try
            {
                string allHistoryDir = Path.Combine(GetProjectRoot(), "All History");
                Directory.CreateDirectory(allHistoryDir);
                CleanupFishImages();

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
            try
            {
                if (file.Exists)
                    file.Delete();
            }
            catch
            {
                // Best effort only. If a file is locked or in use, leave it alone.
            }
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
            catch
            {
                // Non-critical cleanup only.
            }
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
