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
            DbAddLocation(name, direction);
            newLocationName.Text = string.Empty;
            newLocationDirection.SelectedIndex = 0;
            RefreshLocationsPanel();
            ShowInlineActionStatus("Location added.");
            App.RaiseLocationChanged();
        }

        private void DeleteLocation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string locName)
                return;

            var entry = _config.Locations.FirstOrDefault(l => l.Name == locName);
            if (entry == null)
                return;

            _config.Locations.Remove(entry);
            DbDeleteLocation(locName);

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
            ShowInlineActionStatus("Location removed.");
            App.RaiseLocationChanged();
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
            DbAddRun(name, false);  
            if (Application.Current is App createRunApp)
                createRunApp.EnsureRunStorageInitialized();
            LoadRunsDropdown();
            UpdateRunStatusText();
            App.RaiseRunChanged();
            ShowInlineActionStatus("Run created and set as active.");
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
            PersistSettingsToDatabase();
            if (Application.Current is App activeRunApp)
                activeRunApp.EnsureRunStorageInitialized();
            UpdateRunStatusText();
            App.RaiseRunChanged();
            ShowInlineActionStatus($"Active run set to '{selectedRun}'.");

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
                    DbUpdateRunLocked(selectedRun, false);
                    if (Application.Current is App reopenRunApp)
                        reopenRunApp.EnsureRunStorageInitialized();
                    UpdateRunStatusText();
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

            DbUpdateRunLocked(selectedRun, true);
            if (Application.Current is App endRunApp)
                endRunApp.EnsureRunStorageInitialized();
            UpdateRunStatusText();
            ShowInlineActionStatus($"Run '{selectedRun}' ended");
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

                // User settings: FastMode, HighContrast, LargeText, ActiveRunOverride
                string userActiveRunOverride = null;
                using (var cmd = new SqlCommand("kaharra.GetUserSettings", conn))
                {
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@pUserId", app.CurrentUserId);

                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        _checkBoxes.FastMode = reader.GetBoolean(0);
                        _config.HighContrastMode = reader.GetBoolean(1);
                        _config.LargeText = reader.GetBoolean(2);
                        userActiveRunOverride = reader.IsDBNull(3) ? null : reader.GetString(3);
                    }
                }

                // Org settings: ConfidenceThreshold, ActiveRun
                string orgActiveRun = null;
                using (var orgCmd = new SqlCommand("kaharra.GetOrganizationSettings", conn))
                {
                    orgCmd.CommandType = System.Data.CommandType.StoredProcedure;
                    orgCmd.Parameters.AddWithValue("@pOrgId", app.CurrentOrganizationId);

                    using var orgReader = orgCmd.ExecuteReader();
                    if (orgReader.Read())
                    {
                        _config.ConfidenceThreshold = orgReader.GetDouble(0);
                        orgActiveRun = orgReader.IsDBNull(1) ? null : orgReader.GetString(1);
                    }
                }

                // ActiveRun = user override if set, else org default
                _config.ActiveRun = !string.IsNullOrWhiteSpace(userActiveRunOverride)
                    ? userActiveRunOverride
                    : (orgActiveRun ?? string.Empty);

                // Org Runs list
                var runs = new List<RunEntry>();
                using (var runsCmd = new SqlCommand("kaharra.GetOrganizationRuns", conn))
                {
                    runsCmd.CommandType = System.Data.CommandType.StoredProcedure;
                    runsCmd.Parameters.AddWithValue("@pOrgId", app.CurrentOrganizationId);

                    using var runsReader = runsCmd.ExecuteReader();
                    while (runsReader.Read())
                    {
                        runs.Add(new RunEntry
                        {
                            Name = runsReader.GetString(0),
                            Locked = runsReader.GetBoolean(1)
                        });
                    }
                }
                _config.Runs = runs;

                // Org Locations list
                var locations = new List<LocationEntry>();
                using (var locsCmd = new SqlCommand("kaharra.GetOrganizationLocations", conn))
                {
                    locsCmd.CommandType = System.Data.CommandType.StoredProcedure;
                    locsCmd.Parameters.AddWithValue("@pOrgId", app.CurrentOrganizationId);

                    using var locsReader = locsCmd.ExecuteReader();
                    while (locsReader.Read())
                    {
                        locations.Add(new LocationEntry
                        {
                            Name = locsReader.GetString(0),
                            UpstreamDirection = locsReader.GetString(1)
                        });
                    }
                }
                if (locations.Count > 0)
                    _config.Locations = locations;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load settings from database; using in-memory/default values");
            }
        }

        private void PersistSettingsToDatabase()
        {
            var app = Application.Current as App;
            if (app == null || app.CurrentUserId <= 0)
                return;

            using var conn = new SqlConnection(app.connectionString);
            conn.Open();

            string orgActiveRun = null;
            if (app.IsAdmin)
            {
                orgActiveRun = _config.ActiveRun;
            }
            else
            {
                using var orgCmd = new SqlCommand("kaharra.GetOrganizationSettings", conn);
                orgCmd.CommandType = System.Data.CommandType.StoredProcedure;
                orgCmd.Parameters.AddWithValue("@pOrgId", app.CurrentOrganizationId);
                using var orgReader = orgCmd.ExecuteReader();
                if (orgReader.Read())
                    orgActiveRun = orgReader.IsDBNull(1) ? null : orgReader.GetString(1);
            }

            string userOverride = string.Equals(_config.ActiveRun, orgActiveRun, StringComparison.OrdinalIgnoreCase)
                ? null 
                : _config.ActiveRun;

            using (var cmd = new SqlCommand("kaharra.SaveUserSettings", conn))
            {
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@pUserId", app.CurrentUserId);
                cmd.Parameters.AddWithValue("@pFastMode", _checkBoxes.FastMode);
                cmd.Parameters.AddWithValue("@pHighContrastMode", _config.HighContrastMode);
                cmd.Parameters.AddWithValue("@pLargeText", _config.LargeText);
                cmd.Parameters.AddWithValue("@pActiveRunOverride", (object)userOverride ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            if (app.IsAdmin)
            {
                using var saveOrgCmd = new SqlCommand("kaharra.SaveOrganizationSettings", conn);
                saveOrgCmd.CommandType = System.Data.CommandType.StoredProcedure;
                saveOrgCmd.Parameters.AddWithValue("@pOrgId", app.CurrentOrganizationId);
                saveOrgCmd.Parameters.AddWithValue("@pConfidenceThreshold", _config.ConfidenceThreshold);
                saveOrgCmd.Parameters.AddWithValue("@pActiveRun", (object)_config.ActiveRun ?? DBNull.Value);
                saveOrgCmd.Parameters.AddWithValue("@pUpdatedByUserId", app.CurrentUserId);
                saveOrgCmd.ExecuteNonQuery();
            }
        }

        private void DbAddRun(string name, bool locked)
        {
            var app = Application.Current as App;
            if (app == null) return;

            try
            {
                using var conn = new SqlConnection(app.connectionString);
                conn.Open();
                using var cmd = new SqlCommand("kaharra.AddOrganizationRun", conn);
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@pOrgId", app.CurrentOrganizationId);
                cmd.Parameters.AddWithValue("@pName", name);
                cmd.Parameters.AddWithValue("@pLocked", locked);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add run {name} to DB", name);
            }
        }

        private void DbUpdateRunLocked(string name, bool locked)
        {
            var app = Application.Current as App;
            if (app == null) return;

            try
            {
                using var conn = new SqlConnection(app.connectionString);
                conn.Open();
                using var cmd = new SqlCommand("kaharra.UpdateOrganizationRun", conn);
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@pOrgId", app.CurrentOrganizationId);
                cmd.Parameters.AddWithValue("@pName", name);
                cmd.Parameters.AddWithValue("@pLocked", locked);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update run {name} in DB", name);
            }
        }

        private void DbAddLocation(string name, string upstreamDirection)
        {
            var app = Application.Current as App;
            if (app == null) return;

            try
            {
                using var conn = new SqlConnection(app.connectionString);
                conn.Open();
                using var cmd = new SqlCommand("kaharra.AddOrganizationLocation", conn);
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@pOrgId", app.CurrentOrganizationId);
                cmd.Parameters.AddWithValue("@pName", name);
                cmd.Parameters.AddWithValue("@pUpstreamDirection", upstreamDirection);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add location {name} to DB", name);
            }
        }

        private void DbDeleteLocation(string name)
        {
            var app = Application.Current as App;
            if (app == null) return;

            try
            {
                using var conn = new SqlConnection(app.connectionString);
                conn.Open();
                using var cmd = new SqlCommand("kaharra.DeleteOrganizationLocation", conn);
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@pOrgId", app.CurrentOrganizationId);
                cmd.Parameters.AddWithValue("@pName", name);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete location {name} from DB", name);
            }
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

     

        private void ApplySettingsToMainWindow()
        {
            try
            {
                if (Application.Current.MainWindow is MainWindow main)
                {
                    main.Dispatcher.Invoke(() =>
                    {
                        ThemeHelper.ApplyHighContrastMode(_config.HighContrastMode);
                        main.RefreshLibraryConfidenceStyles();
                    });
                }
            }
            catch
            {
                // Best effort only.
            }
        }

        private void ShowInlineActionStatus(string text)
        {
            if (saveStatusText == null) return;

            saveStatusText.Text = text;
            saveStatusText.Foreground = System.Windows.Media.Brushes.ForestGreen;
            saveStatusText.Visibility = Visibility.Visible;

            // Auto-hide after 2 seconds
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            timer.Tick += (s, e) =>
            {
                // Only hide if we haven't been replaced by an unsaved-changes indicator
                if (!_hasUnsavedSettingsChanges)
                {
                    saveStatusText.Visibility = Visibility.Collapsed;
                    saveStatusText.Text = string.Empty;
                }
                timer.Stop();
            };
            timer.Start();
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
