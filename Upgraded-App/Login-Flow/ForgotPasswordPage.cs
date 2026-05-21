// ***************************************************************************************************************************
// File: ForgotPasswordPage.cs
// Description: This is the code behind for the forgot password page, this will allow users to reset their password if they have forgotten it. It will send a reset code to their email and then allow them to enter a new password.
// Notes: N/A
// ***************************************************************************************************************************


using System;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Windows;

namespace FishLens_App
{
    public partial class AuthWindow
    {
        // ── Step 1: Send reset code ────────────────────────────────────
        private void SendResetCode_Click(object sender, RoutedEventArgs e)
        {
            string username = ForgotUsernameBox.Text.Trim();

            if (string.IsNullOrEmpty(username))
            {
                ShowForgotError("Please enter your username.");
                return;
            }

            try
            {
                using (SqlConnection conn = new SqlConnection(
                    (Application.Current as App).connectionString))
                {
                    conn.Open();

                    string sql = @"SELECT Id, Email FROM [kaharra].[FishLensUsers]
                                   WHERE Username = @user";

                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@user", username);

                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                _resetUserId = reader.GetInt32(0);
                                string email = reader.IsDBNull(1) ? null : reader.GetString(1);

                                reader.Close();

                                if (string.IsNullOrEmpty(email))
                                {
                                    ShowForgotError("No email on file. Contact an administrator.");
                                    return;
                                }

                                string code = new Random().Next(100000, 999999).ToString();

                                string insertSql = @"
                                    INSERT INTO [kaharra].[PasswordResetTokens]
                                        (UserId, Token, ExpiresAt)
                                    VALUES
                                        (@userId, @token, @expires)";

                                using (SqlCommand insertCmd = new SqlCommand(insertSql, conn))
                                {
                                    insertCmd.Parameters.AddWithValue("@userId", _resetUserId);
                                    insertCmd.Parameters.AddWithValue("@token", code);
                                    insertCmd.Parameters.AddWithValue("@expires",
                                        DateTime.Now.AddMinutes(15));
                                    insertCmd.ExecuteNonQuery();
                                }

                                if (emailService.SendResetCode(email, code))
                                {
                                    ForgotError.Visibility = Visibility.Collapsed;
                                    ShowPanel("ResetPanel");
                                }
                                else
                                {
                                    ShowForgotError("Failed to send email. Please try again.");
                                }
                            }
                            else
                            {
                                // Don't reveal whether the username exists
                                ShowPanel("ResetPanel");
                            }
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                Debug.WriteLine("SQL: " + ex.Message);
                ShowForgotError("An error occurred. Please try again.");
            }
        }

        // ── Step 2: Verify code and reset password ────────────────────
        private void ResetPassword_Click(object sender, RoutedEventArgs e)
        {
            string code = ResetCodeBox.Text.Trim();
            string newPassword = ResetNewPasswordVisible.Visibility == Visibility.Visible
                ? ResetNewPasswordVisible.Text
                : ResetNewPasswordBox.Password;
            string confirmPassword = ResetConfirmPasswordVisible.Visibility == Visibility.Visible
                ? ResetConfirmPasswordVisible.Text
                : ResetConfirmPasswordBox.Password;

            UpdateResetPasswordRequirements(newPassword, confirmPassword);

            var results = UserValidationRules.ValidatePasswordReset(newPassword, confirmPassword);

            if (!results.IsValid)
            {
                if (results.HasErrorFor("password"))
                    ReqResetPasswordLength.Text = "✗ " + results.GetError("password");
                if (results.HasErrorFor("confirmPassword"))
                    ReqResetPasswordMatch.Text = "✗ " + results.GetError("confirmPassword");
                return;
            }

            try
            {
                using (SqlConnection conn = new SqlConnection(
                    (Application.Current as App).connectionString))
                {
                    conn.Open();

                    string checkSql = @"
                        SELECT Id FROM [kaharra].[PasswordResetTokens]
                        WHERE UserId   = @userId
                          AND Token    = @token
                          AND ExpiresAt > GETDATE()
                          AND Used     = 0";

                    using (SqlCommand cmd = new SqlCommand(checkSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@userId", _resetUserId);
                        cmd.Parameters.AddWithValue("@token", code);

                        object result = cmd.ExecuteScalar();

                        if (result == null)
                        {
                            ShowResetError("Invalid or expired code. Please try again.");
                            return;
                        }

                        int tokenId = Convert.ToInt32(result);

                        using (SqlCommand updateCmd = new SqlCommand(
                            @"UPDATE [kaharra].[PasswordResetTokens] SET Used = 1 WHERE Id = @id",
                            conn))
                        {
                            updateCmd.Parameters.AddWithValue("@id", tokenId);
                            updateCmd.ExecuteNonQuery();
                        }

                        using (SqlCommand resetCmd = new SqlCommand("kaharra.ResetPassword", conn))
                        {
                            resetCmd.CommandType = System.Data.CommandType.StoredProcedure;
                            resetCmd.Parameters.AddWithValue("@pUserId", _resetUserId);
                            resetCmd.Parameters.AddWithValue("@pNewPassword", newPassword);
                            resetCmd.ExecuteNonQuery();
                        }

                        MessageBox.Show("Password reset! You can now sign in.");
                        ShowPanel("SignInPanel");
                    }
                }
            }
            catch (SqlException ex)
            {
                Debug.WriteLine("SQL: " + ex.Message);
                ShowResetError("An error occurred. Please try again.");
            }
        }

        // ── Navigation ─────────────────────────────────────────────────
        private void BackToSignInFromForgot_Click(object sender, RoutedEventArgs e)
            => ShowPanel("SignInPanel");

        private void BackToForgotFromReset_Click(object sender, RoutedEventArgs e)
            => ShowPanel("ForgotPanel");

        // ── Helpers ────────────────────────────────────────────────────
        private void ShowForgotError(string msg)
        {
            ForgotError.Text = msg;
            ForgotError.Visibility = Visibility.Visible;
        }

        private void ShowResetError(string msg)
        {
            ResetError.Text = msg;
            ResetError.Visibility = Visibility.Visible;
        }

        private void UpdateResetPasswordRequirements(string password, string confirm)
        {
            var gray = ColorBrush("#888888");
            var pass = ColorBrush("#4CAF50");
            var fail = ColorBrush("#F44336");

            ReqResetPasswordLength.Foreground = UserValidationRules.IsValidPassword(password)
                ? pass : fail;

            ReqResetPasswordMatch.Foreground =
                (string.IsNullOrEmpty(password) && string.IsNullOrEmpty(confirm))
                    ? gray
                    : UserValidationRules.PasswordsMatch(password, confirm) ? pass : fail;
        }
    }
}

