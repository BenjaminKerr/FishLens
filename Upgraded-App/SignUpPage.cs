// AuthWindow.SignUp.cs
// Handles the two-step sign-up flow (register → verify email).

using System;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FishLens_App
{
    public partial class AuthWindow
    {
        // ── Step 1: Validate fields and send verification code ─────────
        private void CreateAccount_Click(object sender, RoutedEventArgs e)
        {
            ClearSignUpErrors();

            string orgName = OrgNameBox.Text.Trim();
            string email = NewEmailBox.Text.Trim();
            string username = NewUsernameBox.Text.Trim();
            string password = NewPasswordBox.Password;
            string confirmPassword = ConfirmPasswordBox.Password;

            UpdateSignUpRequirements(username, password, confirmPassword);

            var fieldResult = UserValidationRules.ValidateSignUpFields(
                orgName, email, username, password, confirmPassword);

            if (!fieldResult.IsValid)
            {
                ApplySignUpFieldErrors(fieldResult);
                return;
            }

            try
            {
                using (SqlConnection conn = new SqlConnection(
                    (Application.Current as App).connectionString))
                {
                    conn.Open();

                    var dbResult = UserValidationRules.ValidateSignUpDb(
                        conn, orgName, email, username);

                    if (!dbResult.IsValid)
                    {
                        ApplySignUpFieldErrors(dbResult);
                        return;
                    }

                    SendSignUpVerificationCode(conn, email);

                    // Cache pending values for use after verification
                    _pendingOrgName = orgName;
                    _pendingEmail = email;
                    _pendingUsername = username;
                    _pendingPassword = password;

                    ShowPanel("VerifyEmailPanel");
                }
            }
            catch (SqlException ex)
            {
                Debug.WriteLine("SQL: " + ex.Message);
                ShowSignUpFieldError(OrgNameError, "Something went wrong. Please try again.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EX: " + ex.Message);
                ShowSignUpFieldError(OrgNameError, "An unexpected error occurred.");
            }
        }

        // ── Step 2: Verify code and create account ─────────────────────
        private void VerifyCode_Click(object sender, RoutedEventArgs e)
        {
            VerifyError.Visibility = Visibility.Collapsed;

            string enteredCode = VerificationCodeBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(enteredCode))
            {
                ShowVerifyError("Please enter the verification code.");
                return;
            }

            try
            {
                using (SqlConnection conn = new SqlConnection(
                    (Application.Current as App).connectionString))
                {
                    conn.Open();

                    string checkSql = @"
                        SELECT Id FROM [kaharra].[SignupVerificationTokens]
                        WHERE Email     = @email
                          AND Token     = @token
                          AND ExpiresAt > GETDATE()
                          AND Used      = 0";

                    using (SqlCommand cmd = new SqlCommand(checkSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@email", _pendingEmail);
                        cmd.Parameters.AddWithValue("@token", enteredCode);

                        object result = cmd.ExecuteScalar();

                        if (result == null)
                        {
                            ShowVerifyError("Invalid or expired code. Please try again.");
                            return;
                        }

                        int tokenId = Convert.ToInt32(result);

                        using (SqlCommand updateCmd = new SqlCommand(
                            @"UPDATE [kaharra].[SignupVerificationTokens] SET Used = 1 WHERE Id = @id",
                            conn))
                        {
                            updateCmd.Parameters.AddWithValue("@id", tokenId);
                            updateCmd.ExecuteNonQuery();
                        }

                        using (SqlCommand createCmd = new SqlCommand(
                            "kaharra.CreateOrganization", conn))
                        {
                            createCmd.CommandType = System.Data.CommandType.StoredProcedure;
                            createCmd.Parameters.AddWithValue("@pOrgName", _pendingOrgName);
                            createCmd.Parameters.AddWithValue("@pUser", _pendingUsername);
                            createCmd.Parameters.AddWithValue("@pPassword", _pendingPassword);
                            createCmd.Parameters.AddWithValue("@pRoleId", AdminRoleId);
                            createCmd.Parameters.AddWithValue("@pEmail", _pendingEmail);

                            var orgIdParam = new SqlParameter("@pOrgId", System.Data.SqlDbType.Int)
                            { Direction = System.Data.ParameterDirection.Output };
                            var userIdParam = new SqlParameter("@pUserId", System.Data.SqlDbType.Int)
                            { Direction = System.Data.ParameterDirection.Output };

                            createCmd.Parameters.Add(orgIdParam);
                            createCmd.Parameters.Add(userIdParam);
                            createCmd.ExecuteNonQuery();

                            var app = Application.Current as App;
                            app.CurrentUserId = (int)userIdParam.Value;
                            app.CurrentUsername = _pendingUsername;
                            app.CurrentRoleId = AdminRoleId;
                            app.CurrentOrganizationId = (int)orgIdParam.Value;
                        }
                    }

                    new MainWindow().Show();
                    this.Close();
                }
            }
            catch (SqlException ex)
            {
                Debug.WriteLine("SQL: " + ex.Message);
                ShowVerifyError("Something went wrong creating your account.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EX: " + ex.Message);
                ShowVerifyError("An unexpected error occurred.");
            }
        }

        // ── Resend / Back navigation ───────────────────────────────────
        private void ResendSignUpCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(
                    (Application.Current as App).connectionString))
                {
                    conn.Open();
                    SendSignUpVerificationCode(conn, _pendingEmail);
                    ShowVerifyError("A new code has been sent to your email.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EX: " + ex.Message);
                ShowVerifyError("Failed to resend. Please try again.");
            }
        }

        private void BackToSignUpPanel_Click(object sender, RoutedEventArgs e)
            => ShowPanel("SignUpPanel");

        private void GoToSignIn_Click(object sender, MouseButtonEventArgs e)
            => ShowPanel("SignInPanel");

        // ── Internal helpers ───────────────────────────────────────────
        private void SendSignUpVerificationCode(SqlConnection conn, string email)
        {
            string code = new Random().Next(100000, 999999).ToString();

            string insertSql = @"
                INSERT INTO [kaharra].[SignupVerificationTokens] (Email, Token, ExpiresAt)
                VALUES (@email, @token, @expires)";

            using (SqlCommand cmd = new SqlCommand(insertSql, conn))
            {
                cmd.Parameters.AddWithValue("@email", email);
                cmd.Parameters.AddWithValue("@token", code);
                cmd.Parameters.AddWithValue("@expires", DateTime.Now.AddMinutes(15));
                cmd.ExecuteNonQuery();
            }

            if (!emailService.SendVerificationCode(email, code))
                throw new Exception("Failed to send verification email.");
        }

        private void ClearSignUpErrors()
        {
            OrgNameError.Visibility = Visibility.Collapsed;
            EmailError.Visibility = Visibility.Collapsed;
            UsernameError.Visibility = Visibility.Collapsed;
            PasswordError.Visibility = Visibility.Collapsed;
            ConfirmPasswordError.Visibility = Visibility.Collapsed;
        }

        private void ShowSignUpFieldError(System.Windows.Controls.TextBlock block, string message)
        {
            block.Text = message;
            block.Visibility = Visibility.Visible;
        }

        private void ApplySignUpFieldErrors(ValidationResult result)
        {
            if (result.HasErrorFor("orgName")) ShowSignUpFieldError(OrgNameError, result.GetError("orgName"));
            if (result.HasErrorFor("email")) ShowSignUpFieldError(EmailError, result.GetError("email"));
            if (result.HasErrorFor("username")) ShowSignUpFieldError(UsernameError, result.GetError("username"));
            if (result.HasErrorFor("password")) ShowSignUpFieldError(PasswordError, result.GetError("password"));
            if (result.HasErrorFor("confirmPassword")) ShowSignUpFieldError(ConfirmPasswordError, result.GetError("confirmPassword"));
        }

        private void ShowVerifyError(string msg)
        {
            VerifyError.Text = msg;
            VerifyError.Visibility = Visibility.Visible;
        }

        private void UpdateSignUpRequirements(string username, string password, string confirm)
        {
            var pass = ColorBrush("#4CAF50");
            var fail = ColorBrush("#F44336");
            var gray = ColorBrush("#888888");

            ReqSignUpUsernameLength.Foreground = username.Length >= 6 ? pass : fail;
            ReqSignUpPasswordLength.Foreground = password.Length >= 6 ? pass : fail;

            ReqSignUpPasswordMatch.Foreground =
                (string.IsNullOrEmpty(password) && string.IsNullOrEmpty(confirm))
                    ? gray
                    : password == confirm ? pass : fail;
        }

        // ── Shared brush helper (available to all partials) ────────────
        private static SolidColorBrush ColorBrush(string hex) =>
            new SolidColorBrush(
                (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
    }
}

