using FishLens_App.Models;
using System;
using System.IO;
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
                    if (root.TryGetProperty("FastMode", out var fmEl) &&
                        (fmEl.ValueKind == JsonValueKind.True || fmEl.ValueKind == JsonValueKind.False))
                        CheckBoxes.FastMode = fmEl.GetBoolean();
                    if (root.TryGetProperty("ActiveLocation", out var alEl) &&
                        alEl.ValueKind == JsonValueKind.String)
                        ActiveLocation = alEl.GetString() ?? "Unknown";
                    if (root.TryGetProperty("ActiveRun", out var arEl) &&
                        arEl.ValueKind == JsonValueKind.String)
                        ActiveRun = arEl.GetString() ?? string.Empty;
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
    }
}
