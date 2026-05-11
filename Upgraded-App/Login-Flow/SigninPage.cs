// ***************************************************************************************************************************
// File: SigninPage.cs
// Description: This is the code behind for the sign in page, this will allow users to sign in to their account and access the main window. It will also load their saved settings from the database and apply them to the application.
// Notes: N/A
// ***************************************************************************************************************************


using FishLens_App.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FishLens_App
{
    public partial class AuthWindow
    {
        // ── Sign-in ────────────────────────────────────────────────────
        private bool Signin(string username, string password)
        {
            bool success = false;
            var app = Application.Current as App;
            try
            {
                using (SqlConnection conn = new SqlConnection(app.connectionString))
                {
                    conn.Open();

                    using (SqlCommand cmd = new SqlCommand("kaharra.Unsalt", conn))
                    {
                        cmd.CommandType = System.Data.CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@pUser", username);
                        cmd.Parameters.AddWithValue("@pPassword", password);
                        success = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                    }

                    if (success)
                    {
                        // 1. Look up user identity
                        string sql = @"SELECT Id, Username, RoleId, OrganizationId
                               FROM [kaharra].[FishLensUsers]
                               WHERE Username = @user";

                        using (SqlCommand cmd = new SqlCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@user", username);
                            using (SqlDataReader reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    app.CurrentUserId = reader.GetInt32(0);
                                    app.CurrentUsername = reader.GetString(1);
                                    app.CurrentRoleId = reader.GetInt32(2);
                                    app.CurrentOrganizationId =
                                        reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                                }
                            }
                        }

                        // 2. Load this user's saved settings from DB
                        using (SqlCommand settingsCmd = new SqlCommand("kaharra.GetUserSettings", conn))
                        {
                            settingsCmd.CommandType = System.Data.CommandType.StoredProcedure;
                            settingsCmd.Parameters.AddWithValue("@pUserId", app.CurrentUserId);

                            using (SqlDataReader reader = settingsCmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    app.CheckBoxes.FastMode = reader.GetBoolean(0);
                                    app.Configuration.HighContrastMode = reader.GetBoolean(1);
                                    app.Configuration.LargeText = reader.GetBoolean(2);

                                    string activeRunOverride = reader.IsDBNull(3) ? null : reader.GetString(3);
                                    if (!string.IsNullOrWhiteSpace(activeRunOverride))
                                        app.Configuration.ActiveRun = activeRunOverride;

                                    if (!reader.IsDBNull(4))
                                        app.Configuration.ActiveLocation = reader.GetString(4);
                                }
                            }
                        }




                        // 3. Load this user's organization's shared settings
                        using (SqlCommand orgSettingsCmd = new SqlCommand("kaharra.GetOrganizationSettings", conn))
                        {
                            orgSettingsCmd.CommandType = System.Data.CommandType.StoredProcedure;
                            orgSettingsCmd.Parameters.AddWithValue("@pOrgId", app.CurrentOrganizationId);

                            using (SqlDataReader reader = orgSettingsCmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    app.Configuration.ConfidenceThreshold = reader.GetDouble(0);

                                    // Org's default active run (only used if user has no override)
                                    if (!reader.IsDBNull(1))
                                    {
                                        string orgActiveRun = reader.GetString(1);
                                        if (string.IsNullOrWhiteSpace(app.Configuration.ActiveRun))
                                            app.Configuration.ActiveRun = orgActiveRun;
                                    }
                                }
                            }
                        }

                        // 4. Load org's locations into Configuration so MainWindow's dropdown works on first load
                        using (SqlCommand locsCmd = new SqlCommand("kaharra.GetOrganizationLocations", conn))
                        {
                            locsCmd.CommandType = System.Data.CommandType.StoredProcedure;
                            locsCmd.Parameters.AddWithValue("@pOrgId", app.CurrentOrganizationId);

                            var locations = new List<LocationEntry>();
                            using (SqlDataReader reader = locsCmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    locations.Add(new LocationEntry
                                    {
                                        Name = reader.GetString(0),
                                        UpstreamDirection = reader.GetString(1)
                                    });
                                }
                            }
                            if (locations.Count > 0)
                                app.Configuration.Locations = locations;
                        }

                        // 5. Load org's runs so the run dropdown is populated immediately
                        using (SqlCommand runsCmd = new SqlCommand("kaharra.GetOrganizationRuns", conn))
                        {
                            runsCmd.CommandType = System.Data.CommandType.StoredProcedure;
                            runsCmd.Parameters.AddWithValue("@pOrgId", app.CurrentOrganizationId);

                            var runs = new List<RunEntry>();
                            using (SqlDataReader reader = runsCmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    runs.Add(new RunEntry
                                    {
                                        Name = reader.GetString(0),
                                        Locked = reader.GetBoolean(1)
                                    });
                                }
                            }
                            app.Configuration.Runs = runs;
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                Debug.WriteLine("SQL: " + ex.Message);
                SignInError.Text = "Database error during sign-in. Please try again.";
                SignInError.Visibility = Visibility.Visible;
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EX: " + ex.Message);
                SignInError.Text = $"Sign-in failed: {ex.Message}";
                SignInError.Visibility = Visibility.Visible;
                return false;
            }

            if (success)
            {
                // Sync App-level passthroughs so MainWindow reads the right values
                app.ActiveRun = app.Configuration.ActiveRun ?? string.Empty;
                app.ActiveLocation = app.Configuration.ActiveLocation ?? "Unknown";
            }

            return success;
        }







        private void SigninButton_Click(object sender, RoutedEventArgs e)
        {
            if (Signin(SignInUsernameBox.Text, SignInPasswordBox.Password))
            {
                // Apply this user's saved settings to app resources before MainWindow renders
                var app = (App)Application.Current;
                app.ApplyCurrentSettings();
                app.EnsureRunStorageInitialized();

                new MainWindow().Show();
                this.Close();
            }
            else
            {
                SignInError.Text = "Invalid username or password.";
                SignInError.Visibility = Visibility.Visible;
            }
        }





      


        // ── Navigation links from sign-in panel ────────────────────────
        private void GoToSignUp_Click(object sender, MouseButtonEventArgs e)
            => ShowPanel("SignUpPanel");

        private void GoToForgotPassword_Click(object sender, MouseButtonEventArgs e)
            => ShowPanel("ForgotPanel");

        // ── Enter-key shortcuts ────────────────────────────────────────
        private void SignInUsername_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return) { e.Handled = true; SigninButton_Click(null, null); }
        }

        private void SignInPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return) { e.Handled = true; SigninButton_Click(null, null); }
        }

        private void UsernameBox_GotKeyFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.Dispatcher.BeginInvoke(new Action(() => tb.SelectAll()));
        }

        private void PasswordBox_GotKeyFocus(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox pb)
                pb.Dispatcher.BeginInvoke(new Action(() => pb.SelectAll()));
        }



    }
}

