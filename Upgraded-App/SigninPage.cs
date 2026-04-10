// AuthWindow.SignIn.cs
// Handles credential validation and navigation from the sign-in panel.

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

            try
            {
                var app = Application.Current as App;

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
                    }
                }
            }
            catch (SqlException ex) { Debug.WriteLine("SQL: " + ex.Message); }
            catch (Exception ex) { Debug.WriteLine("EX: " + ex.Message); }

            return success;
        }

        private void SigninButton_Click(object sender, RoutedEventArgs e)
        {
            if (Signin(SignInUsernameBox.Text, SignInPasswordBox.Password))
            {
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

