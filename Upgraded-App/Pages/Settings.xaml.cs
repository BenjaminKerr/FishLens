// ***************************************************************************************************************************
// File: Settings.xaml.cs
// Description: This is the code behind for the settings page, this will allow users to adjust their settings such as hiding output and error messages, adjusting confidence thresholds, and toggling high contrast mode.
// Admin users will have access to additional settings that affect the entire organization.
// Notes: N/A
// ***************************************************************************************************************************

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
        // Function: ConfidenceThreshold_ValueChanged
        // Description: Updates the displayed percentage when the confidence slider value changes.
        // Notes: Rounds the new slider value to the nearest integer and updates the UI label `confidenceValue`.
        private void ConfidenceThreshold_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (confidenceValue == null) return;
            var percent = (int)Math.Round(e.NewValue);
            confidenceValue.Text = $"{percent}%";
        }

        // ****************************************************************
        // Function: Cancel_Click
        // Description: Handles Cancel button click — navigates back if possible or hides main frame.
        // Notes: Uses NavigationService if available; otherwise collapses `MainFrame`. Exceptions are logged.
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
        // ****************************************************************
        // Function: SaveSettings_Click
        // Description: Persists user and (if admin) organization settings to the database and applies them.
        // Notes: Updates in-memory `_checkBoxes` and `_config`, calls stored procedures, shows status and logs actions.
        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var app = Application.Current as App;
                int userId = app.CurrentUserId;
                int orgId = app.CurrentOrganizationId;

                // User-scoped settings — everyone saves their own
                _checkBoxes.ErrorBox = hideErrors.IsChecked ?? false;
                _checkBoxes.OutputBox = hideOutput.IsChecked ?? false;
                _config.HighContrastMode = highContrastMode.IsChecked ?? false;
                _config.LargeText = largeText.IsChecked ?? false;

                // Org-scoped setting — admins only
                if (app.IsAdmin)
                {
                    _config.ConfidenceThreshold = (confidenceThreshold?.Value ?? 0) / 100.0;
                }

                using (SqlConnection conn = new SqlConnection(app.connectionString))
                {
                    conn.Open();

                    // Save user settings
                    using (SqlCommand cmd = new SqlCommand("kaharra.SaveUserSettings", conn))
                    {
                        cmd.CommandType = System.Data.CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@pUserId", userId);
                        cmd.Parameters.AddWithValue("@pOutputBox", _checkBoxes.OutputBox);
                        cmd.Parameters.AddWithValue("@pErrorBox", _checkBoxes.ErrorBox);
                        cmd.Parameters.AddWithValue("@pHighContrastMode", _config.HighContrastMode);
                        cmd.Parameters.AddWithValue("@pLargeText", _config.LargeText);
                        cmd.ExecuteNonQuery();
                    }

                    // Save org settings (admins only)
                    if (app.IsAdmin)
                    {
                        using (SqlCommand orgCmd = new SqlCommand("kaharra.SaveOrganizationSettings", conn))
                        {
                            orgCmd.CommandType = System.Data.CommandType.StoredProcedure;
                            orgCmd.Parameters.AddWithValue("@pOrgId", orgId);
                            orgCmd.Parameters.AddWithValue("@pConfidenceThreshold", _config.ConfidenceThreshold);
                            orgCmd.Parameters.AddWithValue("@pUpdatedByUserId", userId);
                            orgCmd.ExecuteNonQuery();
                        }
                    }
                }

                MessageBox.Show("Settings saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                _logger.LogInformation("Settings saved for user {userId}", userId);
                app.ApplyCurrentSettings();

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings to database");
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }






        // ****************************************************************
        // Function: LoadPageVisibility
        // Description: Adjusts UI visibility for admin-only sections based on current user's role.
        // Notes: Collapses analysis UI for non-admin users.
        private void LoadPageVisibility()
        {
            var app = Application.Current as App;
            if (!app.IsAdmin)
            {
                AnalysisHeader.Visibility = Visibility.Collapsed;
                AnalysisCard.Visibility = Visibility.Collapsed;
            }
        }

        // ****************************************************************
        // Function: ToggleErrorMessages
        // Description: Event handler to update internal ErrorBox flag when the hideErrors checkbox is toggled.
        // Notes: Reads current checkbox state into the `_checkBoxes` DTO.
        private void ToggleErrorMessages(object sender, RoutedEventArgs e)
        {
            _checkBoxes.ErrorBox = hideErrors.IsChecked ?? false;
        }

        // ****************************************************************
        // Function: ToggleOutputMessages
        // Description: Event handler to update internal OutputBox flag when the hideOutput checkbox is toggled.
        // Notes: Reads current checkbox state into the `_checkBoxes` DTO.
        private void ToggleOutputMessages(object sender, RoutedEventArgs e)
        {
            _checkBoxes.OutputBox = hideOutput.IsChecked ?? false;
        }

        #endregion

        #region Private Methods



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
              
                LoadSettings();
                LoadPageVisibility();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings");
            }
        }



        // ****************************************************************
        // Function: LoadSettings
        // Description: Loads user and organization settings from database and applies them to UI and in-memory configuration.
        // Notes: Applies runtime defaults first, attempts DB reads and falls back to defaults on failure.
        private void LoadSettings()
        {
            var app = Application.Current as App;
            int userId = app.CurrentUserId;
            int orgId = app.CurrentOrganizationId;

            // Apply runtime-default fallbacks first
            confidenceThreshold.Value = Math.Round((_config?.ConfidenceThreshold ?? 0.7) * 100);
            confidenceValue.Text = $"{(int)Math.Round((_config?.ConfidenceThreshold ?? 0.7) * 100)}%";
            hideErrors.IsChecked = _checkBoxes.ErrorBox;
            hideOutput.IsChecked = _checkBoxes.OutputBox;

            try
            {
                using (SqlConnection conn = new SqlConnection(app.connectionString))
                {
                    conn.Open();

                    // User settings
                    using (SqlCommand cmd = new SqlCommand("kaharra.GetUserSettings", conn))
                    {
                        cmd.CommandType = System.Data.CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@pUserId", userId);

                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                bool outputBox = reader.GetBoolean(0);
                                bool errorBox = reader.GetBoolean(1);
                                bool highContrast = reader.GetBoolean(2);
                                bool largeTxt = reader.GetBoolean(3);

                                _checkBoxes.OutputBox = outputBox;
                                _checkBoxes.ErrorBox = errorBox;
                                _config.HighContrastMode = highContrast;
                                _config.LargeText = largeTxt;

                                hideOutput.IsChecked = outputBox;
                                hideErrors.IsChecked = errorBox;
                                highContrastMode.IsChecked = highContrast;
                                largeText.IsChecked = largeTxt;
                            }
                        }
                    }

                    // Org settings (shared confidence threshold)
                    using (SqlCommand orgCmd = new SqlCommand("kaharra.GetOrganizationSettings", conn))
                    {
                        orgCmd.CommandType = System.Data.CommandType.StoredProcedure;
                        orgCmd.Parameters.AddWithValue("@pOrgId", orgId);

                        using (SqlDataReader reader = orgCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                double conf = reader.GetDouble(0);
                                _config.ConfidenceThreshold = conf;
                                confidenceThreshold.Value = Math.Round(conf * 100);
                                confidenceValue.Text = $"{(int)Math.Round(conf * 100)}%";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load settings from database; using defaults");
            }
        }


        #endregion


    }
}