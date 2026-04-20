using DocumentFormat.OpenXml.Spreadsheet;
using FishLens_App;
using FishLens_App.Interfaces;
using FishLens_App.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FishLens_App
{
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class Settings : System.Windows.Controls.Page
    {
        #region Fields
        private string connectionString =
        "server=aura.cset.oit.edu,5433; " +
        "database=kaharra; " +
        "UID=kaharra; " +
        "password=kaharra";
        private readonly IProjectPathResolver _pathResolver;
        private readonly IFileSystemManager _fileSystemManager;
        private readonly ILogger<MainWindow> _logger;
        private CheckBoxToggle _checkBoxes;
        private AppConfiguration _config;
        private Dictionary<int, (string Username, int RoleId, string email)> _originalUserData = new();
        private bool _loadingSettings = false;
        private bool _hasUnsavedSettingsChanges = false;
        private bool _isUpdatingConfidenceControls = false;

        public List<Role> Roles { get; set; } = new List<Role>();
        private List<User> _users = new List<User>();
        private int CurrentOrgId => (Application.Current as App).CurrentOrganizationId;


        #endregion

        #region Constructors
        public Settings(IProjectPathResolver pathresolver, IFileSystemManager fileSystemManager, ILogger<MainWindow> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pathResolver = pathresolver ?? throw new ArgumentNullException(nameof(pathresolver));
            _fileSystemManager = fileSystemManager ?? throw new ArgumentNullException(nameof(fileSystemManager));
            InitializeComponent();

            Loaded += (s, e) =>
            {
                App.AnalysisStateChanged += OnAnalysisStateChanged;
                // Apply immediately in case analysis was already running when user navigated here
                ApplyAnalysisLock(App.IsAnalyzing);
            };
            Unloaded += (s, e) => App.AnalysisStateChanged -= OnAnalysisStateChanged;

            LoadAll();
        }

        // ****************************************************************
        // Function: OnAnalysisStateChanged / ApplyAnalysisLock
        // Description: Locks/unlocks run + location controls while YOLO analysis is running
        private void OnAnalysisStateChanged(bool isAnalyzing) =>
            Dispatcher.Invoke(() => ApplyAnalysisLock(isAnalyzing));

        private void ApplyAnalysisLock(bool isAnalyzing)
        {
            // Run setup section
            RunSetupCard.IsEnabled = !isAnalyzing;
            // Locations section (also disables dynamically-created delete buttons inside panel)
            LocationsCard.IsEnabled = !isAnalyzing;
            // Save Settings — disabled to prevent e.g. a Fast Mode toggle from restarting Python mid-run
            saveSettingsButton.IsEnabled = !isAnalyzing;
            // Banner
            analysisWarningBanner.Visibility = isAnalyzing ? Visibility.Visible : Visibility.Collapsed;
        }
        #endregion

        #region Event Handlers

        // ****************************************************************
        // Function: ScrollViewer_PreviewMouseWheel
        // Description: Slows down and smooths scrolling speed
        // Notes: Reduces scroll delta to make scrolling less snappy
        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer != null)
            {
                // Scroll slower - reduce delta by 75% (multiply by 0.25)
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - (e.Delta * 0.25));
                e.Handled = true;
            }
        }

        // ****************************************************************
        // Function: CreateUserClick
        // Description: Handle create user button click and calls signup
        // Notes: Temporary message boxes for debug
        public void CreateUserClick(object sender, RoutedEventArgs e)
        {
            if (RoleComboBox.SelectedItem is not Role selectedRole)
            {
                MessageBox.Show("Please select a role.");
            }
            else
            {
                string username = NewUsername.Text.Trim();
                string password = NewPassword.Password;
                string email = NewUserEmail.Text.Trim();
                string error = null;

                if (string.IsNullOrWhiteSpace(username))
                    error = "Please enter a username.";
                else if (username.Length < 6)
                    error = "Username must be at least 6 characters.";
                else if (string.IsNullOrWhiteSpace(password))
                    error = "Please enter a password.";
                else if (password.Length < 6)
                    error = "Password must be at least 6 characters.";
                else if (string.IsNullOrWhiteSpace(email) || !email.Contains("@") || !email.Contains("."))
                    error = "Please enter a valid email address";

                if (error != null)
                {
                    MessageBox.Show(error);
                }
                else
                {
                    int roleId = selectedRole.ID;
                    bool status = SignUp(username, email, password, roleId);

                    if (status)
                    {
                        NewUsername.Text = "";
                        NewPassword.Password = "";
                        RoleComboBox.SelectedIndex = -1;
                        NewUserEmail.Text = "";
                        MessageBox.Show("User created successfully!");
                        LoadUsers();
                    }
                    else
                    {
                        MessageBox.Show("Sign up unsuccessful, retry.");
                    }
                }
            }
        }




        private void ConfidenceThreshold_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (confidenceValueBox == null || _isUpdatingConfidenceControls) return;
            var percent = (int)Math.Round(e.NewValue);
            _isUpdatingConfidenceControls = true;
            confidenceValueBox.Text = percent.ToString();
            _isUpdatingConfidenceControls = false;
            MarkSettingsDirty();
        }

        private void ConfidenceValueBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyConfidenceThresholdFromTextBox();
        }

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

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // Try to navigate back if possible, otherwise hide the main frame (return to home)
            try
            {
                if (this.NavigationService != null && this.NavigationService.CanGoBack)
                {
                    this.NavigationService.GoBack();
                    return;
                }

                var main = Application.Current.MainWindow as MainWindow;
                if (main != null)
                {
                    main.MainFrame.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cancel navigation failed");
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                double previousConfidenceThreshold = _config?.ConfidenceThreshold ?? 0.0;

                // Update configuration in memory
                _config.ConfidenceThreshold = (confidenceThreshold?.Value ?? 0) / 100.0;

                // Ensure CheckBoxToggle is up to date
                bool prevFastMode = _checkBoxes.FastMode;
                _checkBoxes.FastMode = enableFastMode.IsChecked ?? false;
                bool highContrast = highContrastMode.IsChecked ?? false;
                bool largeTxt = largeText.IsChecked ?? false;

                // Persist settings to a JSON file in project root
                // Update additional settings in shared config
                _config.HighContrastMode = highContrast;
                _config.LargeText = largeTxt;

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

                string projectRoot = _pathResolver.ResolveProjectRoot();
                string configPath = System.IO.Path.Combine(projectRoot ?? string.Empty, "appsettings.json");
                SaveSettingsFile(configPath, settingsObj);
                _logger.LogInformation("Settings saved to {path}", configPath);

                // Apply certain settings immediately to main window
                ApplySettingsToMainWindow();

                // Restart Python only if Fast Mode actually changed
                if (_checkBoxes.FastMode != prevFastMode)
                    App.RaiseFastModeChanged();

                // Sync active location to App and always notify MainWindow so dropdown refreshes
                var appInst = Application.Current as App;
                if (appInst != null)
                    appInst.ActiveLocation = _config.ActiveLocation;
                App.RaiseLocationChanged();

                if (Math.Abs(_config.ConfidenceThreshold - previousConfidenceThreshold) > 0.0001)
                    App.RaiseConfidenceThresholdChanged();

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
            if (!_loadingSettings)
            {
                _checkBoxes.FastMode = enableFastMode.IsChecked ?? false;
                MarkSettingsDirty();
            }
        }

        private void SettingsControl_Changed(object sender, RoutedEventArgs e)
        {
            MarkSettingsDirty();
        }

        private void AddLocation_Click(object sender, RoutedEventArgs e)
        {
            string name = newLocationName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Please enter a location name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Prevent duplicates (case-insensitive)
            if (_config.Locations.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("A location with that name already exists.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string direction = (newLocationDirection.SelectedIndex == 0) ? "left" : "right";
            _config.Locations.Add(new LocationEntry { Name = name, UpstreamDirection = direction });
            newLocationName.Text = string.Empty;
            newLocationDirection.SelectedIndex = 0;
            RefreshLocationsPanel();
            MarkSettingsDirty();
        }

        private void DeleteLocation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string locName)
            {
                var entry = _config.Locations.FirstOrDefault(l => l.Name == locName);
                if (entry != null)
                {
                    _config.Locations.Remove(entry);

                    // If the deleted location was active, reset to Unknown (or first remaining)
                    var app = Application.Current as App;
                    if (app != null && app.ActiveLocation == locName)
                    {
                        string newActive = _config.Locations.FirstOrDefault()?.Name ?? "Unknown";
                        app.ActiveLocation = newActive;
                        _config.ActiveLocation = newActive;
                    }

                    RefreshLocationsPanel();
                    MarkSettingsDirty();
                }
            }
        }

        #endregion

        #region Private Methods

        // ****************************************************************
        // Function: SignUp
        // Description: Creates username and password credentials for signin with salting
        // Notes: Writes the new user into the temporary Kaharra SQL-backed flow.
        private bool SignUp(string username, string email, string password, int roleId)
        {
            bool success = false;
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("kaharra.AddUser", conn))
                    {
                        cmd.CommandType = System.Data.CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@pUser", username);
                        cmd.Parameters.AddWithValue("@pPassword", password);
                        cmd.Parameters.AddWithValue("@pRoleId", roleId);
                        cmd.Parameters.AddWithValue("@pOrgId", CurrentOrgId);
                        cmd.Parameters.AddWithValue("@pEmail", email);
                        cmd.ExecuteNonQuery();
                        success = true;
                    }
                }
            }
            catch (SqlException ex)
            {
                Debug.WriteLine("SQL Exception: " + ex.Message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception: " + ex.Message);
            }
            return success;
        }

        // ****************************************************************
        // Function: LoadAll
        // Description: Initializes the page from app state, persisted settings, and database-backed data.
        private void LoadAll()
        { 
            try
            {
                DataContext = this;
                _checkBoxes = (Application.Current as App).CheckBoxes;
                _config = (Application.Current as App).Configuration;
                LoadRoles();
                LoadUsers();
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

            if (!app.IsAdmin)
            {
                ManageUsersCard.Visibility = Visibility.Collapsed;
                ManageUsersText.Visibility = Visibility.Collapsed;
                CreateUserCard.Visibility = Visibility.Collapsed;
                CreateUserText.Visibility = Visibility.Collapsed;
                LocationsCard.Visibility = Visibility.Collapsed;
                LocationsText.Visibility = Visibility.Collapsed;
                RunSetupCard.Visibility = Visibility.Collapsed;
                RunSetupText.Visibility = Visibility.Collapsed;
            }
        }

        // ****************************************************************
        // Function: LoadUsers
        // Description: Loads the users for the Manage users card
        // Notes: N/A
        private void LoadUsers()
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();

                    string sql = @"
                SELECT Id, Username, Email, RoleId
                FROM [kaharra].[kaharra].[FishLensUsers]
                WHERE OrganizationId = @orgId
                ORDER BY Username";

                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@orgId", CurrentOrgId);

                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            var users = new List<User>();

                            while (reader.Read())
                            {
                                int id = reader.GetInt32(0);
                                string username = reader.GetString(1);
                                string email = reader.GetString(2);
                                int roleId = reader.GetInt32(3);

                                Role matchedRole = Roles.FirstOrDefault(r => r.ID == roleId);
                                users.Add(new User(id, username, matchedRole, email));
                            }

                            _users = users;
                            _originalUserData = users.ToDictionary(
                                u => u.Id,
                                u => (u.Username, u.role?.ID ?? -1, u.Email)
                            );
                            UsersGrid.ItemsSource = _users;
                        }
                    }
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Failed to load users.");
            }
        }

        // ****************************************************************
        // Function: UpdateUser
        // Description: Based on the changes made inside of the manage user section will
        // apply them to the FishLensUsers database
        // Notes: N/A
        private bool UpdateUser(int userId, string newUsername, int newRoleId, string email)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();

                    string sql = @"
                UPDATE [kaharra].[kaharra].[FishLensUsers]
                SET Username = @user, RoleId = @roleid, Email = @email
                WHERE Id = @userid";

                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@user", newUsername);
                        cmd.Parameters.AddWithValue("@roleid", newRoleId);
                        cmd.Parameters.AddWithValue("@email", email);
                        cmd.Parameters.AddWithValue("@userid", userId);

                        cmd.ExecuteNonQuery();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("UpdateUser error: " + ex.Message);
                return false;
            }
        }

        // ****************************************************************
        // Function: SaveUserChanges_Click
        // Description: Handles the save user changes click and ensures valid input
        // before calling updateuser
        // Notes: N/A
        private void SaveUserChanges_Click(object sender, RoutedEventArgs e)
        {
            if (_users == null || _users.Count == 0)
            {
                MessageBox.Show("No users to save.");
            }
            else
            {
                // Find only users that actually changed
                var changedUsers = _users.Where(u =>
                {
                    bool Pass = true;
                    if (string.IsNullOrWhiteSpace(u.Username) || u.role == null)
                        Pass = false;

                    if (!_originalUserData.TryGetValue(u.Id, out var original))
                        Pass = false;

                    if (Pass == true)
                    {
                        return u.Username.Trim() != original.Username || u.role.ID != original.RoleId || u.Email?.Trim() != original.email;
                    }
                    else
                    {
                        return Pass;
                    }
                }).ToList();

                if (changedUsers.Count == 0)
                {
                    MessageBox.Show(
                        "No changes detected.",
                        "Nothing to Save",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                }
                else
                {
                    int updated = 0;
                    var updatedNames = new List<string>();

                    foreach (var u in changedUsers)
                    {
                        if (UpdateUser(u.Id, u.Username.Trim(), u.role.ID, u.Email))
                        {
                            updated++;
                            updatedNames.Add(u.Username.Trim());
                        }
                    }

                    if (updated > 0)
                    {
                        string names = string.Join(", ", updatedNames);
                        string message = updated == 1
                            ? $"Successfully updated {names}."
                            : $"Successfully updated {updated} users: {names}.";

                        MessageBox.Show(
                            message,
                            "Changes Saved ✓",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show(
                            "Something went wrong — no changes were saved. Please try again.",
                            "Save Failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }

                    LoadUsers();
                }
            }
        }


        private void RefreshUsers_Click(object sender, RoutedEventArgs e)
        {
            LoadUsers();
        }

        // ****************************************************************
        // Function: LoadRoles
        // Description: Loads the roles available from the Roles table
        // Notes: N/A
        private void LoadRoles()
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();

                    using (SqlCommand cmd = new SqlCommand("SELECT Id, Name FROM Roles", conn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        var roles = new List<Role>();

                        while (reader.Read())
                        {
                            roles.Add(new Role(
                                reader.GetString(1), // Name
                                reader.GetInt32(0)   // Id
                            ));
                        }

                        Roles = roles;                 
                        RoleComboBox.ItemsSource = roles; 

                        DataContext = this;           
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("LoadRoles error: " + ex.Message);
                MessageBox.Show("Failed to load roles.");
            }
        }
        private void LoadSettings()
        {
            _loadingSettings = true;
            try
            {
            // Set defaults from current runtime state
            _isUpdatingConfidenceControls = true;
            confidenceThreshold.Value = Math.Round((_config?.ConfidenceThreshold ?? 0.7) * 100);
            confidenceValueBox.Text = $"{(int)Math.Round((_config?.ConfidenceThreshold ?? 0.7) * 100)}";
            _isUpdatingConfidenceControls = false;
            enableFastMode.IsChecked = _checkBoxes.FastMode;

            // Try to read persisted settings
            try
            {
                string projectRoot = _pathResolver.ResolveProjectRoot();
                string configPath = System.IO.Path.Combine(projectRoot ?? string.Empty, "appsettings.json");
                if (!File.Exists(configPath)) return;

                using var stream = File.OpenRead(configPath);
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;

                if (root.TryGetProperty("ConfidenceThreshold", out var confEl) && confEl.ValueKind == JsonValueKind.Number)
                {
                    double conf = confEl.GetDouble();
                    _config.ConfidenceThreshold = conf;
                    _isUpdatingConfidenceControls = true;
                    confidenceThreshold.Value = Math.Round(conf * 100);
                    confidenceValueBox.Text = $"{(int)Math.Round(conf * 100)}";
                    _isUpdatingConfidenceControls = false;
                }

                if (root.TryGetProperty("HighContrastMode", out var hcEl) && (hcEl.ValueKind == JsonValueKind.True || hcEl.ValueKind == JsonValueKind.False))
                {
                    _config.HighContrastMode = hcEl.GetBoolean();
                    highContrastMode.IsChecked = _config.HighContrastMode;
                }

                if (root.TryGetProperty("LargeText", out var ltEl) && (ltEl.ValueKind == JsonValueKind.True || ltEl.ValueKind == JsonValueKind.False))
                {
                    _config.LargeText = ltEl.GetBoolean();
                    largeText.IsChecked = _config.LargeText;
                }

                if (root.TryGetProperty("FastMode", out var fmEl) && (fmEl.ValueKind == JsonValueKind.True || fmEl.ValueKind == JsonValueKind.False))
                {
                    _checkBoxes.FastMode = fmEl.GetBoolean();
                    enableFastMode.IsChecked = _checkBoxes.FastMode;
                }

                if (root.TryGetProperty("ActiveLocation", out var alEl) && alEl.ValueKind == JsonValueKind.String)
                {
                    _config.ActiveLocation = alEl.GetString() ?? "Unknown";
                }

            if (root.TryGetProperty("ActiveRun", out var arEl) && arEl.ValueKind == JsonValueKind.String)
            {
                _config.ActiveRun = arEl.GetString() ?? string.Empty;
                var app = Application.Current as App;
                if (app != null) app.ActiveRun = _config.ActiveRun;
                }

                if (root.TryGetProperty("Runs", out var runsEl) && runsEl.ValueKind == JsonValueKind.Array)
                {
                    var runs = new List<RunEntry>();
                    foreach (var runEl in runsEl.EnumerateArray())
                    {
                        string rName = runEl.TryGetProperty("Name", out var rnEl) ? rnEl.GetString() ?? string.Empty : string.Empty;
                        bool rLocked = runEl.TryGetProperty("Locked", out var rlEl) && rlEl.ValueKind == JsonValueKind.True;
                        if (!string.IsNullOrWhiteSpace(rName))
                            runs.Add(new RunEntry { Name = rName, Locked = rLocked });
                    }
                    _config.Runs = runs;
                }

                LoadRunsDropdown();

                if (root.TryGetProperty("Locations", out var locsEl) && locsEl.ValueKind == JsonValueKind.Array)
                {
                    var locs = new List<LocationEntry>();
                    foreach (var locEl in locsEl.EnumerateArray())
                    {
                        string locName = locEl.TryGetProperty("Name", out var nEl) ? nEl.GetString() ?? "Unknown" : "Unknown";
                        string locDir = locEl.TryGetProperty("UpstreamDirection", out var dEl) ? dEl.GetString() ?? "left" : "left";
                        locs.Add(new LocationEntry { Name = locName, UpstreamDirection = locDir });
                    }
                    if (locs.Count > 0)
                        _config.Locations = locs;
                }

                RefreshLocationsPanel();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not parse settings file; using defaults");
            }
            }
            finally
            {
                _loadingSettings = false;
                _hasUnsavedSettingsChanges = false;
            }
        }

        private void RefreshLocationsPanel()
        {
            if (locationsListPanel == null) return;
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
                    Foreground = (System.Windows.Media.Brush)Application.Current.Resources["PrimaryText"],
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(nameBlock, 0);

                string dirLabel = loc.UpstreamDirection == "right" ? "Upstream: Right" : "Upstream: Left";
                var dirBlock = new TextBlock
                {
                    Text = dirLabel,
                    FontSize = (double)Application.Current.Resources["BaseFontSize"],
                    Foreground = (System.Windows.Media.Brush)Application.Current.Resources["SecondaryText"],
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
                    Background = System.Windows.Media.Brushes.IndianRed,
                    Foreground = System.Windows.Media.Brushes.White,
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

        private void ApplySettingsToMainWindow()
        {
            try
            {
                var main = Application.Current.MainWindow as MainWindow;
                if (main != null)
                {
                    // Apply high contrast mode (handles both enabled and disabled states)
                    main.Dispatcher.Invoke(() =>
                    {
                        ThemeHelper.ApplyHighContrastMode(_config.HighContrastMode);
                        main.RefreshLibraryConfidenceStyles();
                    });

                    // Apply large text setting
                    if (_config.LargeText)
                    {
                        Application.Current.Resources["BaseFontSize"] = 18.0;
                        Application.Current.Resources["HeaderFontSize"] = 32.0;
                        Application.Current.Resources["LargeHeaderFontSize"] = 42.0;
                        Application.Current.Resources["TitleFontSize"] = 72.0;
                    }
                    else
                    {
                        Application.Current.Resources["BaseFontSize"] = 14.0;
                        Application.Current.Resources["HeaderFontSize"] = 24.0;
                        Application.Current.Resources["LargeHeaderFontSize"] = 32.0;
                        Application.Current.Resources["TitleFontSize"] = 56.0;
                    }
                }
            }
            catch { }
        }

        #endregion

        private void UsersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Intentionally left blank. The grid still raises SelectionChanged, but the page
            // does not currently perform any action when a row becomes selected.
        }

        #region Run Management

        // ****************************************************************
        // Function: CreateRun_Click
        // Description: Creates a new seasonal run folder and adds it to appsettings.json
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

            // Create the run folder under All History
            try
            {
                string runFolder = _pathResolver.ResolveRunFolder(name);
                Directory.CreateDirectory(runFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not create run folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _config.Runs.Add(new RunEntry { Name = name, Locked = false });
            if (newRunNameBox != null) newRunNameBox.Text = string.Empty;

            // Auto-activate the newly created run
            _config.ActiveRun = name;
            var app = Application.Current as App;
            if (app != null) app.ActiveRun = name;
            App.RaiseRunChanged();

            PersistRuns();
            LoadRunsDropdown();
            UpdateRunStatusText();
            UpdateSaveStatusAfterImmediateRunPersist();
        }

        // ****************************************************************
        // Function: SetActiveRun_Click
        // Description: Sets the selected run as the active run for the whole app
        private void SetActiveRun_Click(object sender, RoutedEventArgs e)
        {
            if (activeRunDropdown?.SelectedItem is not string selectedDisplay || string.IsNullOrWhiteSpace(selectedDisplay))
            {
                MessageBox.Show("Please select a run from the dropdown first.", "No Run Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Map display name back to internal run name
            string selectedRun = selectedDisplay == "[Debug]" ? "debug" : selectedDisplay;

            var runEntry = _config.Runs.FirstOrDefault(r => r.Name == selectedRun);
            if (runEntry?.Locked == true)
            {
                MessageBox.Show($"'{selectedRun}' is locked and cannot be set as active. Reopen it first.", "Run Locked", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _config.ActiveRun = selectedRun;
            var app = Application.Current as App;
            if (app != null) app.ActiveRun = selectedRun;

            PersistRuns();
            UpdateRunStatusText();
            App.RaiseRunChanged();
            UpdateSaveStatusAfterImmediateRunPersist();
        }

        // ****************************************************************
        // Function: EndRun_Click
        // Description: Locks the selected run so it cannot be written to accidentally
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
            if (runEntry == null) return;

            if (runEntry.Locked)
            {
                // Already locked — offer to reopen
                var result = MessageBox.Show($"'{selectedRun}' is already locked. Reopen it?", "Reopen Run?", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    runEntry.Locked = false;
                    PersistRuns();
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

            if (confirm != MessageBoxResult.Yes) return;

            runEntry.Locked = true;

            // If this was the active run, clear it
            if (_config.ActiveRun == selectedRun)
            {
                _config.ActiveRun = string.Empty;
                var app = Application.Current as App;
                if (app != null) app.ActiveRun = string.Empty;
                App.RaiseRunChanged();
            }

            PersistRuns();
            UpdateRunStatusText();
            UpdateSaveStatusAfterImmediateRunPersist();
        }

        // ****************************************************************
        // Function: ActiveRunDropdown_SelectionChanged
        // Description: Updates the status text when the dropdown selection changes
        private void ActiveRunDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateRunStatusText();
        }

        // ****************************************************************
        // Function: LoadRunsDropdown
        // Description: Populates the run selector dropdown from _config.Runs
        private void LoadRunsDropdown()
        {
            if (activeRunDropdown == null) return;

            // Build name list: debug entry first (shown as [Debug]), then normal runs
            var names = new List<string> { "[Debug]" };
            names.AddRange(_config.Runs.Select(r => r.Name));
            activeRunDropdown.ItemsSource = names;

            // Map active run name to list entry ("debug" -> "[Debug]")
            string activeDisplay = _config.ActiveRun == "debug" ? "[Debug]" : _config.ActiveRun;

            // Restore selection to active run if present
            if (!string.IsNullOrWhiteSpace(activeDisplay) && names.Contains(activeDisplay))
                activeRunDropdown.SelectedItem = activeDisplay;
            else if (names.Count > 0)
                activeRunDropdown.SelectedIndex = 0;

            UpdateRunStatusText();
        }

        // ****************************************************************
        // Function: UpdateRunStatusText
        // Description: Updates the status label showing active run and lock state
        private void UpdateRunStatusText()
        {
            if (runStatusText == null) return;
            string selectedDisplay = activeRunDropdown?.SelectedItem as string ?? string.Empty;
            if (string.IsNullOrWhiteSpace(selectedDisplay)) { runStatusText.Text = string.Empty; return; }

            string selected = selectedDisplay == "[Debug]" ? "debug" : selectedDisplay;

            if (selected == "debug")
            {
                bool isActive = string.Equals("debug", _config.ActiveRun, StringComparison.OrdinalIgnoreCase);
                runStatusText.Text = $"[Debug] — {(isActive ? "✔ Active" : "Inactive")} (testing mode, not saved to history)";
                return;
            }

            var entry = _config.Runs.FirstOrDefault(r => r.Name == selected);
            bool isActiveSeason = string.Equals(selected, _config.ActiveRun, StringComparison.OrdinalIgnoreCase);
            bool isLocked = entry?.Locked ?? false;

            string status = isLocked ? "🔒 Locked (read-only)" : (isActiveSeason ? "✔ Active" : "Inactive");
            runStatusText.Text = $"{selected} — {status}";
        }

        // ****************************************************************
        // Function: PersistRuns
        // Description: Writes ActiveRun and Runs back to appsettings.json
        private void PersistRuns()
        {
            try
            {
                string projectRoot = _pathResolver.ResolveProjectRoot();
                string configPath = System.IO.Path.Combine(projectRoot, "appsettings.json");

                // Read existing JSON, replace only ActiveRun and Runs keys
                string existing = File.Exists(configPath) ? File.ReadAllText(configPath) : "{}";
                using var doc = JsonDocument.Parse(existing);
                var root = doc.RootElement;

                var dict = new Dictionary<string, object>();
                foreach (var prop in root.EnumerateObject())
                    dict[prop.Name] = prop.Value.Clone();

                dict["ActiveRun"] = (object)_config.ActiveRun;
                dict["Runs"] = (object)_config.Runs.Select(r => new { r.Name, r.Locked }).ToList();

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

        private void MarkSettingsDirty()
        {
            if (_loadingSettings)
                return;

            _hasUnsavedSettingsChanges = true;
            UpdateSaveStatus("\u26A0 Unsaved Changes", Brushes.DarkOrange);
        }

        private void SetSaveStatusSaved()
        {
            _hasUnsavedSettingsChanges = false;
            UpdateSaveStatus("\u2714 Saved Changes", Brushes.ForestGreen);
        }

        private void UpdateSaveStatusAfterImmediateRunPersist()
        {
            if (_hasUnsavedSettingsChanges)
            {
                UpdateSaveStatus("\u26A0 Unsaved Changes", Brushes.DarkOrange);
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

        #endregion
    }
}
