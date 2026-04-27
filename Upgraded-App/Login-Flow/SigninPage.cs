// ***************************************************************************************************************************
// File: SigninPage.cs
// Description: This is the code behind for the sign in page, this will allow users to sign in to their account and access the main window. It will also load their saved settings from the database and apply them to the application.
// Notes: N/A
// ***************************************************************************************************************************


using System;
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

                        // Load this user's saved settings from DB — only on successful login
                        using (SqlCommand settingsCmd = new SqlCommand("kaharra.GetUserSettings", conn))
                        {
                            settingsCmd.CommandType = System.Data.CommandType.StoredProcedure;
                            settingsCmd.Parameters.AddWithValue("@pUserId", app.CurrentUserId);

                            using (SqlDataReader reader = settingsCmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    // Legacy output/error columns remain in the procedure result,
                                    // but the current UI only keeps Fast Mode locally.
                                    app.Configuration.HighContrastMode = reader.GetBoolean(2);
                                    app.Configuration.LargeText = reader.GetBoolean(3);
                                }
                            }
                        }

                        // Load this user's organization's shared settings (e.g., confidence threshold)
                        using (SqlCommand orgSettingsCmd = new SqlCommand("kaharra.GetOrganizationSettings", conn))
                        {
                            orgSettingsCmd.CommandType = System.Data.CommandType.StoredProcedure;
                            orgSettingsCmd.Parameters.AddWithValue("@pOrgId", app.CurrentOrganizationId);

                            using (SqlDataReader reader = orgSettingsCmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    app.Configuration.ConfidenceThreshold = reader.GetDouble(0);
                                }
                                // No row → defaults stay in place (0.7 from constructor)
                            }
                        }



                    }
                }
            }
            catch (SqlException ex) { Debug.WriteLine("SQL: " + ex.Message); }
            catch (Exception ex) { Debug.WriteLine("EX: " + ex.Message); }

            if (success)
            {
                // After DB-backed user/org settings are loaded, refresh the JSON-backed
                // runtime settings such as Fast Mode, active run, and active location.
                app.LoadRuntimeSettingsFromJson();
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

