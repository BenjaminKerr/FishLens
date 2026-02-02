using FishLens_App.Interfaces;
using FishLens_App.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
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
using System.Data.SqlClient;

namespace FishLens_App
{
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class Settings : Page
    {
        #region Fields
        private string connectionString = "server=aura.cset.oit.edu,5433; " +
   "database=kaharra; " +
   "UID=kaharra; " +
   "password=kaharra";
        private readonly IProjectPathResolver _pathResolver;
        private readonly IFileSystemManager _fileSystemManager;
        private readonly ILogger<MainWindow> _logger;
        private CheckBoxToggle _checkBoxes;
        private AppConfiguration _config;
        #endregion

        #region Constructors
        public Settings(IProjectPathResolver pathresolver, IFileSystemManager fileSystemManager, ILogger<MainWindow> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pathResolver = pathresolver ?? throw new ArgumentNullException(nameof(pathresolver));
            _fileSystemManager = fileSystemManager ?? throw new ArgumentNullException(nameof(fileSystemManager));
            InitializeComponent();
            _checkBoxes = (Application.Current as App).CheckBoxes;
            _config = (Application.Current as App).Configuration;

            // Initialize UI from current state / persisted settings
            try
            {
                LoadSettings();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings");
            }
        }
        #endregion

        #region Event Handlers

        public void CreateUserClick(object sender, RoutedEventArgs e)
        {
            bool Status;

            String username = NewUsername.Text;

            String password = NewPassword.Password;
            Status = SignUp(username, password);
            if (Status == true)
            {
                MessageBox.Show("Sign up successful, Login saved");
                NewUsername.Text = "";
                NewPassword.Password = "";

            }
            else
            {
                MessageBox.Show("Sign up unsuccessful, retry");
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
        #endregion

        #region Private Methods

        private bool SignUp(string username, string password)
        {
            bool success = false;
            bool Pass = true;
            String query = "Kaharra.AddUser @puser = @Username , @pPassword = @Password";

            if (query.Contains(';') || query.Contains(')'))
            {
                Pass = false;
            }

            Console.WriteLine(query);
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))

                {

                    conn.Open();

                    if (conn.State == System.Data.ConnectionState.Open)

                    {

                        using (SqlCommand cmd = conn.CreateCommand())

                        {

                            cmd.CommandText = query;

                            cmd.Parameters.AddWithValue("@Username", username);
                            cmd.Parameters.AddWithValue("@Password", password);

                            String newQuery = cmd.ToString();
                            Console.WriteLine(newQuery);
                            if (Pass == true)
                            {
                                cmd.ExecuteNonQuery();
                                success = true;
                            }


                        }

                    }

                }

            }

            catch (Exception eSql)

            {

                Debug.WriteLine("Exception: " + eSql.Message);

            }


            return success;
        }

        private void LoadSettings()
        {
            // Set defaults from current runtime state
            confidenceThreshold.Value = Math.Round((_config?.ConfidenceThreshold ?? 0.7) * 100);
            confidenceValue.Text = $"{(int)Math.Round((_config?.ConfidenceThreshold ?? 0.7) * 100)}%";
            hideErrors.IsChecked = _checkBoxes.ErrorBox;
            hideOutput.IsChecked = _checkBoxes.OutputBox;

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


    }
}