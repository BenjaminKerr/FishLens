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
            
            LoadAll();
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
            if (confidenceValue == null) return;
            var percent = (int)Math.Round(e.NewValue);
            confidenceValue.Text = $"{percent}%";
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
                // Update configuration in memory
                _config.ConfidenceThreshold = (confidenceThreshold?.Value ?? 0) / 100.0;

                // Ensure CheckBoxToggle is up to date
                _checkBoxes.ErrorBox = hideErrors.IsChecked ?? false;
                _checkBoxes.OutputBox = hideOutput.IsChecked ?? false;
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
                    OutputBox = _checkBoxes.OutputBox,
                    ErrorBox = _checkBoxes.ErrorBox,
                    FastMode = _checkBoxes.FastMode,
                    HighContrastMode = _config.HighContrastMode,
                    LargeText = _config.LargeText
                };

                string projectRoot = _pathResolver.ResolveProjectRoot();
                string configPath = System.IO.Path.Combine(projectRoot ?? string.Empty, "appsettings.json");
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(configPath, JsonSerializer.Serialize(settingsObj, options));

                MessageBox.Show("Settings saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                _logger.LogInformation("Settings saved to {path}", configPath);

                // Apply certain settings immediately to main window
                ApplySettingsToMainWindow();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings");
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ToggleErrorMessages(object sender, RoutedEventArgs e)
        {
            _checkBoxes.ErrorBox = hideErrors.IsChecked ?? false;
        }

        private void ToggleOutputMessages(object sender, RoutedEventArgs e)
        {
            _checkBoxes.OutputBox = hideOutput.IsChecked ?? false;
        }

        private void ToggleFastMode(object sender, RoutedEventArgs e)
        {
            _checkBoxes.FastMode = enableFastMode.IsChecked ?? false;
            App.RaiseFastModeChanged();
        }
        #endregion

        #region Private Methods

        // ****************************************************************
        // Function: Signup
        // Description: Creates username and password credentials for signin with salting
        //     
        // Notes: Temporarily stores into Kaharra SQL
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
        // Description: Function to cleanup code Initializes UI from current state / persisted settings / database values
        // Notes: N/A
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
            catch (Exception ex)
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
            string error;

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
            // Set defaults from current runtime state
            confidenceThreshold.Value = Math.Round((_config?.ConfidenceThreshold ?? 0.7) * 100);
            confidenceValue.Text = $"{(int)Math.Round((_config?.ConfidenceThreshold ?? 0.7) * 100)}%";
            hideErrors.IsChecked = _checkBoxes.ErrorBox;
            hideOutput.IsChecked = _checkBoxes.OutputBox;
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
                    confidenceThreshold.Value = Math.Round(conf * 100);
                    confidenceValue.Text = $"{(int)Math.Round(conf * 100)}%";
                }

                if (root.TryGetProperty("OutputBox", out var outEl) && outEl.ValueKind == JsonValueKind.True || outEl.ValueKind == JsonValueKind.False)
                {
                    _checkBoxes.OutputBox = outEl.GetBoolean();
                    hideOutput.IsChecked = _checkBoxes.OutputBox;
                }

                if (root.TryGetProperty("ErrorBox", out var errEl) && errEl.ValueKind == JsonValueKind.True || errEl.ValueKind == JsonValueKind.False)
                {
                    _checkBoxes.ErrorBox = errEl.GetBoolean();
                    hideErrors.IsChecked = _checkBoxes.ErrorBox;
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
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not parse settings file; using defaults");
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

        }
    }
}