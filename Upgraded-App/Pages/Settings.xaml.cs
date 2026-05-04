using FishLens_App.Interfaces;
using FishLens_App.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FishLens_App
{
    public partial class Settings : System.Windows.Controls.Page
    {
        private readonly IProjectPathResolver _pathResolver;
        private readonly IFileSystemManager _fileSystemManager;
        private readonly ILogger<MainWindow> _logger;
        private CheckBoxToggle _checkBoxes;
        private AppConfiguration _config;
        private bool _loadingSettings;
        private bool _hasUnsavedSettingsChanges;
        private bool _isUpdatingConfidenceControls;

        public Settings(IProjectPathResolver pathresolver, IFileSystemManager fileSystemManager, ILogger<MainWindow> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pathResolver = pathresolver ?? throw new ArgumentNullException(nameof(pathresolver));
            _fileSystemManager = fileSystemManager ?? throw new ArgumentNullException(nameof(fileSystemManager));
            InitializeComponent();

            Loaded += (_, _) =>
            {
                App.AnalysisStateChanged += OnAnalysisStateChanged;
                ApplyAnalysisLock(App.IsAnalyzing);
            };
            Unloaded += (_, _) => App.AnalysisStateChanged -= OnAnalysisStateChanged;

            LoadAll();
        }

        private void OnAnalysisStateChanged(bool isAnalyzing) =>
            Dispatcher.Invoke(() => ApplyAnalysisLock(isAnalyzing));

        private void ApplyAnalysisLock(bool isAnalyzing)
        {
            if (RunSetupCard != null)
                RunSetupCard.IsEnabled = !isAnalyzing;
            if (LocationsCard != null)
                LocationsCard.IsEnabled = !isAnalyzing;
            if (saveSettingsButton != null)
                saveSettingsButton.IsEnabled = !isAnalyzing;
            if (analysisWarningBanner != null)
                analysisWarningBanner.Visibility = isAnalyzing ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - (e.Delta * 0.25));
                e.Handled = true;
            }
        }

        private void ConfidenceThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (confidenceValueBox == null || _isUpdatingConfidenceControls)
                return;

            _isUpdatingConfidenceControls = true;
            confidenceValueBox.Text = $"{Math.Round(e.NewValue):F0}";
            _isUpdatingConfidenceControls = false;
            MarkSettingsDirty();
        }

        private void ConfidenceValueBox_LostFocus(object sender, RoutedEventArgs e) =>
            ApplyConfidenceThresholdFromTextBox();

        private void ConfidenceValueBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            ApplyConfidenceThresholdFromTextBox();
            e.Handled = true;
        }

        private void ApplyConfidenceThresholdFromTextBox()
        {
            if (confidenceThreshold == null || confidenceValueBox == null)
                return;

            string raw = (confidenceValueBox.Text ?? string.Empty).Trim().TrimEnd('%');
            if (!double.TryParse(raw, out double value))
                value = confidenceThreshold.Value;

            value = Math.Max(0, Math.Min(100, Math.Round(value)));

            _isUpdatingConfidenceControls = true;
            confidenceThreshold.Value = value;
            confidenceValueBox.Text = $"{value:F0}";
            _isUpdatingConfidenceControls = false;
            MarkSettingsDirty();
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                double previousConfidenceThreshold = _config?.ConfidenceThreshold ?? 0.0;
                bool previousFastMode = _checkBoxes.FastMode;

                _config.ConfidenceThreshold = (confidenceThreshold?.Value ?? 0) / 100.0;
                _config.HighContrastMode = highContrastMode.IsChecked ?? false;
                _config.LargeText = largeText.IsChecked ?? false;
                _checkBoxes.FastMode = enableFastMode.IsChecked ?? false;

                PersistSettingsToDatabase();
                PersistSettingsToJson();

                var appInst = Application.Current as App;
                if (appInst != null)
                {
                    appInst.ActiveLocation = _config.ActiveLocation;
                    appInst.ActiveRun = _config.ActiveRun;
                    appInst.ApplyCurrentSettings();
                }

                ApplySettingsToMainWindow();

                if (_checkBoxes.FastMode != previousFastMode)
                    App.RaiseFastModeChanged();
                if (Math.Abs(_config.ConfidenceThreshold - previousConfidenceThreshold) > 0.0001)
                    App.RaiseConfidenceThresholdChanged();

                App.RaiseLocationChanged();
                SetSaveStatusSaved();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings");
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ToggleFastMode(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings)
                return;

            _checkBoxes.FastMode = enableFastMode.IsChecked ?? false;
            MarkSettingsDirty();
        }

        private void SettingsControl_Changed(object sender, RoutedEventArgs e) =>
            MarkSettingsDirty();

        private void AddLocation_Click(object sender, RoutedEventArgs e)
        {
            string name = newLocationName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Please enter a location name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_config.Locations.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("A location with that name already exists.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string direction = newLocationDirection.SelectedIndex == 1 ? "right" : "left";
            _config.Locations.Add(new LocationEntry { Name = name, UpstreamDirection = direction });
            newLocationName.Text = string.Empty;
            newLocationDirection.SelectedIndex = 0;
            RefreshLocationsPanel();
            MarkSettingsDirty();
        }

        private void DeleteLocation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string locName)
                return;

            var entry = _config.Locations.FirstOrDefault(l => l.Name == locName);
            if (entry == null)
                return;

            _config.Locations.Remove(entry);

            if (string.Equals(_config.ActiveLocation, locName, StringComparison.OrdinalIgnoreCase))
            {
                string newActive = _config.Locations.FirstOrDefault()?.Name ?? "Unknown";
                _config.ActiveLocation = newActive;

                if (Application.Current is App app)
                    app.ActiveLocation = newActive;
            }

            if (_config.Locations.Count == 0)
                _config.Locations.Add(new LocationEntry { Name = "Unknown", UpstreamDirection = "left" });

            RefreshLocationsPanel();
            MarkSettingsDirty();
        }

        private void CreateRun_Click(object sender, RoutedEventArgs e)
        {
            string name = newRunNameBox?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Please enter a run name (e.g. Spring 2026).", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.Equals(name, "debug", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("\"debug\" is a reserved run name. Use the Debug run from the dropdown.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_config.Runs.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("A run with that name already exists.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Directory.CreateDirectory(_pathResolver.ResolveRunFolder(name));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not create run folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _config.Runs.Add(new RunEntry { Name = name, Locked = false });
            _config.ActiveRun = name;

            if (Application.Current is App app)
                app.ActiveRun = name;

            if (newRunNameBox != null)
                newRunNameBox.Text = string.Empty;

            PersistRuns();
            if (Application.Current is App createRunApp)
                createRunApp.EnsureRunStorageInitialized();
            LoadRunsDropdown();
            UpdateRunStatusText();
            App.RaiseRunChanged();
            UpdateSaveStatusAfterImmediateRunPersist();
        }

        private void SetActiveRun_Click(object sender, RoutedEventArgs e)
        {
            if (activeRunDropdown?.SelectedItem is not string selectedDisplay || string.IsNullOrWhiteSpace(selectedDisplay))
            {
                MessageBox.Show("Please select a run from the dropdown first.", "No Run Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedRun = selectedDisplay == "[Debug]" ? "debug" : selectedDisplay;
            var runEntry = _config.Runs.FirstOrDefault(r => r.Name == selectedRun);
            if (runEntry?.Locked == true)
            {
                MessageBox.Show($"'{selectedRun}' is locked and cannot be set as active. Reopen it first.", "Run Locked", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _config.ActiveRun = selectedRun;
            if (Application.Current is App app)
                app.ActiveRun = selectedRun;

            PersistRuns();
            if (Application.Current is App activeRunApp)
                activeRunApp.EnsureRunStorageInitialized();
            UpdateRunStatusText();
            App.RaiseRunChanged();
            UpdateSaveStatusAfterImmediateRunPersist();
        }

        private void EndRun_Click(object sender, RoutedEventArgs e)
        {
            if (activeRunDropdown?.SelectedItem is not string selectedDisplay || string.IsNullOrWhiteSpace(selectedDisplay))
            {
                MessageBox.Show("Please select a run from the dropdown first.", "No Run Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedRun = selectedDisplay == "[Debug]" ? "debug" : selectedDisplay;
            if (selectedRun == "debug")
            {
                MessageBox.Show("The Debug run cannot be locked.", "Debug Run", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var runEntry = _config.Runs.FirstOrDefault(r => r.Name == selectedRun);
            if (runEntry == null)
                return;

            if (runEntry.Locked)
            {
                if (MessageBox.Show($"'{selectedRun}' is already locked. Reopen it?", "Reopen Run?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    runEntry.Locked = false;
                    PersistRuns();
                    if (Application.Current is App reopenRunApp)
                        reopenRunApp.EnsureRunStorageInitialized();
                    UpdateRunStatusText();
                    UpdateSaveStatusAfterImmediateRunPersist();
                }
                return;
            }

            var confirm = MessageBox.Show(
                $"End run '{selectedRun}'? It will be locked for new entries. Reports can still be generated.\n\nYou can reopen it later from this panel.",
                "End Run",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            runEntry.Locked = true;

            if (string.Equals(_config.ActiveRun, selectedRun, StringComparison.OrdinalIgnoreCase))
            {
                _config.ActiveRun = string.Empty;
                if (Application.Current is App app)
                    app.ActiveRun = string.Empty;
                App.RaiseRunChanged();
            }

            PersistRuns();
            if (Application.Current is App endRunApp)
                endRunApp.EnsureRunStorageInitialized();
            UpdateRunStatusText();
            UpdateSaveStatusAfterImmediateRunPersist();
        }

        private void ActiveRunDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            UpdateRunStatusText();

        private void LoadAll()
        {
            try
            {
                DataContext = this;
                _checkBoxes = (Application.Current as App)?.CheckBoxes ?? new CheckBoxToggle();
                _config = (Application.Current as App)?.Configuration ?? new AppConfiguration();
                LoadPageVisibility();
                LoadSettings();
                ClearSaveStatus();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings");
            }
        }

        private void LoadPageVisibility()
        {
            var app = Application.Current as App;
            if (app == null || app.IsAdmin)
                return;

            AnalysisHeader.Visibility = Visibility.Collapsed;
            AnalysisCard.Visibility = Visibility.Collapsed;
            RunSetupCard.Visibility = Visibility.Collapsed;
            RunSetupText.Visibility = Visibility.Collapsed;
            LocationsCard.Visibility = Visibility.Collapsed;
            LocationsText.Visibility = Visibility.Collapsed;
        }

        private void LoadSettings()
        {
            _loadingSettings = true;

            try
            {
                LoadSettingsFromDatabase();
                LoadSettingsFromJson();

                _isUpdatingConfidenceControls = true;
                double confPercent = Math.Round((_config?.ConfidenceThreshold ?? 0.7) * 100);
                confidenceThreshold.Value = confPercent;
                confidenceValueBox.Text = $"{confPercent:F0}";
                _isUpdatingConfidenceControls = false;

                highContrastMode.IsChecked = _config.HighContrastMode;
                largeText.IsChecked = _config.LargeText;
                enableFastMode.IsChecked = _checkBoxes.FastMode;

                if (string.IsNullOrWhiteSpace(_config.ActiveLocation))
                    _config.ActiveLocation = _config.Locations.FirstOrDefault()?.Name ?? "Unknown";

                if (Application.Current is App app)
                {
                    app.ActiveLocation = _config.ActiveLocation;
                    app.ActiveRun = _config.ActiveRun;
                    app.EnsureRunStorageInitialized();
                }

                LoadRunsDropdown();
                RefreshLocationsPanel();
            }
            finally
            {
                _loadingSettings = false;
                _hasUnsavedSettingsChanges = false;
            }
        }

        private void LoadSettingsFromDatabase()
        {
            var app = Application.Current as App;
            if (app == null || app.CurrentUserId <= 0)
                return;

            try
            {
                using var conn = new SqlConnection(app.connectionString);
                conn.Open();

                using (var cmd = new SqlCommand("kaharra.GetUserSettings", conn))
                {
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@pUserId", app.CurrentUserId);

                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        // Legacy output/error columns are ignored now, but high-contrast and text size remain useful.
                        _config.HighContrastMode = reader.GetBoolean(2);
                        _config.LargeText = reader.GetBoolean(3);
                    }
                }

                if (!app.IsAdmin)
                    return;

                using var orgCmd = new SqlCommand("kaharra.GetOrganizationSettings", conn);
                orgCmd.CommandType = System.Data.CommandType.StoredProcedure;
                orgCmd.Parameters.AddWithValue("@pOrgId", app.CurrentOrganizationId);

                using var orgReader = orgCmd.ExecuteReader();
                if (orgReader.Read())
                    _config.ConfidenceThreshold = orgReader.GetDouble(0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load settings from database; using in-memory/default values");
            }
        }

        private void LoadSettingsFromJson()
        {
            try
            {
                string configPath = Path.Combine(_pathResolver.ResolveProjectRoot() ?? string.Empty, "appsettings.json");
                if (!File.Exists(configPath))
                    return;

                using var stream = File.OpenRead(configPath);
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;

                if (root.TryGetProperty("FastMode", out var fmEl) &&
                    (fmEl.ValueKind == JsonValueKind.True || fmEl.ValueKind == JsonValueKind.False))
                {
                    _checkBoxes.FastMode = fmEl.GetBoolean();
                }

                if (root.TryGetProperty("ActiveLocation", out var alEl) && alEl.ValueKind == JsonValueKind.String)
                {
                    _config.ActiveLocation = alEl.GetString() ?? "Unknown";
                }

                if (root.TryGetProperty("ActiveRun", out var arEl) && arEl.ValueKind == JsonValueKind.String)
                {
                    _config.ActiveRun = arEl.GetString() ?? string.Empty;
                }

                if (root.TryGetProperty("Runs", out var runsEl) && runsEl.ValueKind == JsonValueKind.Array)
                {
                    var runs = new List<RunEntry>();
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

                    _config.Runs = runs;
                }

                if (root.TryGetProperty("Locations", out var locsEl) && locsEl.ValueKind == JsonValueKind.Array)
                {
                    var locations = new List<LocationEntry>();
                    foreach (var locEl in locsEl.EnumerateArray())
                    {
                        string locName = locEl.TryGetProperty("Name", out var nameEl)
                            ? nameEl.GetString() ?? "Unknown"
                            : "Unknown";
                        string locDir = locEl.TryGetProperty("UpstreamDirection", out var dirEl)
                            ? dirEl.GetString() ?? "left"
                            : "left";

                        locations.Add(new LocationEntry { Name = locName, UpstreamDirection = locDir });
                    }

                    if (locations.Count > 0)
                        _config.Locations = locations;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not parse appsettings.json; keeping current settings");
            }
        }

        private void PersistSettingsToDatabase()
        {
            var app = Application.Current as App;
            if (app == null || app.CurrentUserId <= 0)
                return;

            using var conn = new SqlConnection(app.connectionString);
            conn.Open();

            using (var cmd = new SqlCommand("kaharra.SaveUserSettings", conn))
            {
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@pUserId", app.CurrentUserId);
                cmd.Parameters.AddWithValue("@pOutputBox", false);
                cmd.Parameters.AddWithValue("@pErrorBox", false);
                cmd.Parameters.AddWithValue("@pHighContrastMode", _config.HighContrastMode);
                cmd.Parameters.AddWithValue("@pLargeText", _config.LargeText);
                cmd.ExecuteNonQuery();
            }

            if (!app.IsAdmin)
                return;

            using var orgCmd = new SqlCommand("kaharra.SaveOrganizationSettings", conn);
            orgCmd.CommandType = System.Data.CommandType.StoredProcedure;
            orgCmd.Parameters.AddWithValue("@pOrgId", app.CurrentOrganizationId);
            orgCmd.Parameters.AddWithValue("@pConfidenceThreshold", _config.ConfidenceThreshold);
            orgCmd.Parameters.AddWithValue("@pUpdatedByUserId", app.CurrentUserId);
            orgCmd.ExecuteNonQuery();
        }

        private void PersistSettingsToJson()
        {
            string configPath = Path.Combine(_pathResolver.ResolveProjectRoot() ?? string.Empty, "appsettings.json");
            var settingsObj = new
            {
                ConfidenceThreshold = _config.ConfidenceThreshold,
                FastMode = _checkBoxes.FastMode,
                HighContrastMode = _config.HighContrastMode,
                LargeText = _config.LargeText,
                ActiveLocation = _config.ActiveLocation,
                ActiveRun = _config.ActiveRun,
                Runs = _config.Runs,
                Locations = _config.Locations
            };

            SaveSettingsFile(configPath, settingsObj);
            _logger.LogInformation("Settings saved to {path}", configPath);
        }

        private void RefreshLocationsPanel()
        {
            if (locationsListPanel == null)
                return;

            locationsListPanel.Children.Clear();

            foreach (var loc in _config.Locations)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameBlock = new TextBlock
                {
                    Text = loc.Name,
                    FontSize = (double)Application.Current.Resources["BaseFontSize"],
                    Foreground = (Brush)Application.Current.Resources["PrimaryText"],
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(nameBlock, 0);

                var dirBlock = new TextBlock
                {
                    Text = loc.UpstreamDirection == "right" ? "Upstream: Right" : "Upstream: Left",
                    FontSize = (double)Application.Current.Resources["BaseFontSize"],
                    Foreground = (Brush)Application.Current.Resources["SecondaryText"],
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 12, 0)
                };
                Grid.SetColumn(dirBlock, 1);

                var deleteBtn = new Button
                {
                    Content = "Remove",
                    Tag = loc.Name,
                    Height = 28,
                    Padding = new Thickness(10, 0, 10, 0),
                    FontSize = (double)Application.Current.Resources["BaseFontSize"],
                    Background = Brushes.IndianRed,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                deleteBtn.Click += DeleteLocation_Click;
                Grid.SetColumn(deleteBtn, 2);

                row.Children.Add(nameBlock);
                row.Children.Add(dirBlock);
                row.Children.Add(deleteBtn);
                locationsListPanel.Children.Add(row);
            }
        }

        private void LoadRunsDropdown()
        {
            if (activeRunDropdown == null)
                return;

            var names = new List<string> { "[Debug]" };
            names.AddRange(_config.Runs.Select(r => r.Name));
            activeRunDropdown.ItemsSource = names;

            string activeDisplay = _config.ActiveRun == "debug" ? "[Debug]" : _config.ActiveRun;
            if (!string.IsNullOrWhiteSpace(activeDisplay) && names.Contains(activeDisplay))
                activeRunDropdown.SelectedItem = activeDisplay;
            else if (names.Count > 0)
                activeRunDropdown.SelectedIndex = 0;

            UpdateRunStatusText();
        }

        private void UpdateRunStatusText()
        {
            if (runStatusText == null)
                return;

            string selectedDisplay = activeRunDropdown?.SelectedItem as string ?? string.Empty;
            if (string.IsNullOrWhiteSpace(selectedDisplay))
            {
                runStatusText.Text = string.Empty;
                return;
            }

            string selected = selectedDisplay == "[Debug]" ? "debug" : selectedDisplay;
            if (selected == "debug")
            {
                bool isActive = string.Equals("debug", _config.ActiveRun, StringComparison.OrdinalIgnoreCase);
                runStatusText.Text = $"[Debug] - {(isActive ? "Active" : "Inactive")} (testing mode, not saved to history)";
                return;
            }

            var entry = _config.Runs.FirstOrDefault(r => r.Name == selected);
            bool isActiveSeason = string.Equals(selected, _config.ActiveRun, StringComparison.OrdinalIgnoreCase);
            bool isLocked = entry?.Locked ?? false;
            string status = isLocked ? "Locked (read-only)" : (isActiveSeason ? "Active" : "Inactive");
            runStatusText.Text = $"{selected} - {status}";
        }

        private void PersistRuns()
        {
            try
            {
                string configPath = Path.Combine(_pathResolver.ResolveProjectRoot(), "appsettings.json");
                string existing = File.Exists(configPath) ? File.ReadAllText(configPath) : "{}";
                using var doc = JsonDocument.Parse(existing);
                var root = doc.RootElement;

                var dict = new Dictionary<string, object>();
                foreach (var prop in root.EnumerateObject())
                    dict[prop.Name] = prop.Value.Clone();

                dict["ActiveRun"] = _config.ActiveRun;
                dict["Runs"] = _config.Runs.Select(r => new { r.Name, r.Locked }).ToList();
                SaveSettingsFile(configPath, dict);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist runs");
            }
        }

        private static void SaveSettingsFile(string configPath, object settingsObj)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(configPath, JsonSerializer.Serialize(settingsObj, options));
        }

        private void ApplySettingsToMainWindow()
        {
            try
            {
                if (Application.Current.MainWindow is MainWindow main)
                {
                    main.Dispatcher.Invoke(() =>
                    {
                        ThemeHelper.ThemeSwap(_config.HighContrastMode);
                        main.RefreshLibraryConfidenceStyles();
                    });
                }
            }
            catch
            {
                // Best effort only.
            }
        }

        private void MarkSettingsDirty()
        {
            if (_loadingSettings)
                return;

            _hasUnsavedSettingsChanges = true;
            UpdateSaveStatus("Unsaved Changes", Brushes.DarkOrange);
        }

        private void SetSaveStatusSaved()
        {
            _hasUnsavedSettingsChanges = false;
            UpdateSaveStatus("Saved Changes", Brushes.ForestGreen);
        }

        private void UpdateSaveStatusAfterImmediateRunPersist()
        {
            if (_hasUnsavedSettingsChanges)
            {
                UpdateSaveStatus("Unsaved Changes", Brushes.DarkOrange);
                return;
            }

            SetSaveStatusSaved();
        }

        private void ClearSaveStatus()
        {
            _hasUnsavedSettingsChanges = false;
            if (saveStatusText != null)
            {
                saveStatusText.Text = string.Empty;
                saveStatusText.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateSaveStatus(string text, Brush brush)
        {
            if (saveStatusText == null)
                return;

            saveStatusText.Text = text;
            saveStatusText.Foreground = brush;
            saveStatusText.Visibility = Visibility.Visible;
        }
    }
}
