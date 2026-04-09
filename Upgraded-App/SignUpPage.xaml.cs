using DocumentFormat.OpenXml.Drawing.Diagrams;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace FishLens_App
{
    public partial class SignUpPage : Window
    {
        private string connectionString =
            "server=aura.cset.oit.edu,5433; " +
            "database=kaharra; " +
            "UID=kaharra; " +
            "password=kaharra";

        // The RoleId that represents "Admin" in your Roles table
        private const int AdminRoleId = 1;
        private EmailService emailService = new EmailService();

        private string _pendingOrgName;
        private string _pendingEmail;
        private string _pendingUsername;
        private string _pendingPassword;

        public SignUpPage()
        {
            InitializeComponent();
        }

        private bool IsValidEmailFormat(string email)
        {
            string pattern = @"^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,10}$";
            return Regex.IsMatch(email, pattern);

        }

        // ****************************************************************
        // Function: CreateAccount_Click
        // Description: Handles the create account button being pressed before creating their
        // account and organization in the database
        // Notes: N/A
        private void CreateAccount_Click(object sender, RoutedEventArgs e)
        {
            ErrorMessage.Visibility = Visibility.Collapsed;

            string orgName = OrgName.Text.Trim();
            string email = NewEmail.Text.Trim();
            string username = NewUsername.Text.Trim();
            string password = NewPassword.Password;
            string confirmPassword = ConfirmPassword.Password;

            string error = null;


            //TODO: Make this a helper function
            if (string.IsNullOrWhiteSpace(orgName))
                error = "Please enter an organization name.";
            else if (string.IsNullOrWhiteSpace(email))
                error = "Please enter an email address.";
            else if (!IsValidEmailFormat(email))
                error = "Please enter a valid email address.";
            else if (!email.Contains("@") || !email.Contains("."))
                error = "Please enter a valid email address.";
            else if (string.IsNullOrWhiteSpace(username))
                error = "Please enter a username.";
            else if (username.Length < 6)
                error = "Username must be at least 6 characters.";
            else if (string.IsNullOrWhiteSpace(password))
                error = "Please enter a password.";
            else if (password.Length < 6)
                error = "Password must be at least 6 characters.";
            else if (password != confirmPassword)
                error = "Passwords do not match.";

            if (error != null)
            {
                ShowError(error);
            }
            else
            {
                try
                {
                    bool canProceed = true;

                    using (SqlConnection conn = new SqlConnection(connectionString))
                    {
                        conn.Open();

                        // Check if org name already exists
                        if (canProceed)
                        {
                            using (SqlCommand checkCmd = new SqlCommand(
                                "SELECT COUNT(*) FROM [kaharra].[Organizations] WHERE Name = @name", conn))
                            {
                                checkCmd.Parameters.AddWithValue("@name", orgName);
                                if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
                                {
                                    ShowError("An organization with that name already exists.");
                                    canProceed = false;
                                }
                            }
                        }

                        // Check if username already exists
                        if (canProceed)
                        {
                            using (SqlCommand checkCmd = new SqlCommand(
                                "SELECT COUNT(*) FROM [kaharra].[FishLensUsers] WHERE Username = @user", conn))
                            {
                                checkCmd.Parameters.AddWithValue("@user", username);
                                if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
                                {
                                    ShowError("That username is already taken.");
                                    canProceed = false;
                                }
                            }
                        }

                        // Check if email already exists
                        if (canProceed)
                        {
                            using (SqlCommand checkCmd = new SqlCommand(
                                "SELECT COUNT(*) FROM [kaharra].[FishLensUsers] WHERE Email = @email", conn))
                            {
                                checkCmd.Parameters.AddWithValue("@email", email);
                                if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
                                {
                                    ShowError("That email is already in use.");
                                    canProceed = false;
                                }
                            }
                        }

                        if (canProceed)
                        {
                            SendVerificationCode(conn, email);

                            // Cache form data in memory (same session, no security risk)
                            _pendingOrgName = orgName;
                            _pendingEmail = email;
                            _pendingUsername = username;
                            _pendingPassword = password;

                            SignUpStep.Visibility = Visibility.Collapsed;
                            VerifyEmailStep.Visibility = Visibility.Visible;
                        }
                    }
                }
                catch (SqlException ex)
                {
                    Debug.WriteLine("SQL Exception: " + ex.Message);
                    ShowError("Something went wrong. Please try again.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Exception: " + ex.Message);
                    ShowError("An unexpected error occurred. Please try again.");
                }
            }
        }


        private void SendVerificationCode(SqlConnection conn, string email)
        {
            string code = new Random().Next(100000, 999999).ToString();
            string insertSql = @" INSERT INTO [kaharra].[SignupVerificationTokens] (Email, Token, ExpiresAt) VALUES (@email, @token, @expires)"; 

            using (SqlCommand cmd = new SqlCommand(insertSql, conn)) 
            { 
                cmd.Parameters.AddWithValue("@email", email); 
                cmd.Parameters.AddWithValue("@token", code); 
                cmd.Parameters.AddWithValue("@expires", DateTime.Now.AddMinutes(15)); cmd.ExecuteNonQuery(); 
            }

            bool sent = emailService.SendResetCode(email, code); 
            if (!sent) throw new Exception("Failed to send verification email.");
        }


        // ****************************************************************
        // Function: VerifyCode_Click
        // Description: Looks up the code in SignupVerificationTokens exactly
        //              like ForgotPasswordWindow checks PasswordResetTokens,
        //              then creates the account if valid
        // ****************************************************************
        private void VerifyCode_Click(object sender, RoutedEventArgs e)
        {
            VerifyErrorMessage.Visibility = Visibility.Collapsed;

            string enteredCode = VerificationCodeBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(enteredCode))
            {
                ShowVerifyError("Please enter the verification code.");
                return;
            }

            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();

                    // Verify the code is valid, not expired, and not already used
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

                        // Mark token as used
                        string markUsedSql = @"
                            UPDATE [kaharra].[SignupVerificationTokens] 
                            SET Used = 1 
                            WHERE Id = @id";

                        using (SqlCommand updateCmd = new SqlCommand(markUsedSql, conn))
                        {
                            updateCmd.Parameters.AddWithValue("@id", tokenId);
                            updateCmd.ExecuteNonQuery();
                        }

                        // Create the org and account
                        using (SqlCommand createCmd = new SqlCommand("kaharra.CreateOrganization", conn))
                        {
                            createCmd.CommandType = System.Data.CommandType.StoredProcedure;

                            createCmd.Parameters.AddWithValue("@pOrgName", _pendingOrgName);
                            createCmd.Parameters.AddWithValue("@pUser", _pendingUsername);
                            createCmd.Parameters.AddWithValue("@pPassword", _pendingPassword);
                            createCmd.Parameters.AddWithValue("@pRoleId", AdminRoleId);
                            createCmd.Parameters.AddWithValue("@pEmail", _pendingEmail);

                            var orgIdParam = new SqlParameter("@pOrgId", System.Data.SqlDbType.Int)
                            {
                                Direction = System.Data.ParameterDirection.Output
                            };
                            createCmd.Parameters.Add(orgIdParam);

                            var userIdParam = new SqlParameter("@pUserId", System.Data.SqlDbType.Int)
                            {
                                Direction = System.Data.ParameterDirection.Output
                            };
                            createCmd.Parameters.Add(userIdParam);

                            createCmd.ExecuteNonQuery();

                            var app = Application.Current as App;
                            app.CurrentUserId = (int)userIdParam.Value;
                            app.CurrentUsername = _pendingUsername;
                            app.CurrentRoleId = AdminRoleId;
                            app.CurrentOrganizationId = (int)orgIdParam.Value;
                        }
                    }
                }

                MainWindow main = new MainWindow();
                main.Show();
                this.Close();
            }
            catch (SqlException ex)
            {
                Debug.WriteLine("SQL Exception: " + ex.Message);
                ShowVerifyError("Something went wrong creating your account. Please try again.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception: " + ex.Message);
                ShowVerifyError("An unexpected error occurred. Please try again.");
            }
        }

        // ****************************************************************
        // Function: ResendCode_Click
        // Description: Issues a fresh code to the same email, same as
        //              re-triggering SendCode in ForgotPasswordWindow
        // ****************************************************************
        private void ResendCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    SendVerificationCode(conn, _pendingEmail);
                    ShowVerifyError("A new code has been sent to your email.");
                }
            }
            catch (SqlException ex)
            {
                Debug.WriteLine("SQL Exception: " + ex.Message);
                ShowVerifyError("Failed to resend. Please try again.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception: " + ex.Message);
                ShowVerifyError("Failed to resend. Please try again.");
            }
        }

        // ****************************************************************
        // Function: BackToSignUp_Click
        // Description: Returns user to the sign-up form
        // ****************************************************************
        private void BackToSignUp_Click(object sender, RoutedEventArgs e)
        {
            VerifyEmailStep.Visibility = Visibility.Collapsed;
            SignUpStep.Visibility = Visibility.Visible;
            VerifyErrorMessage.Visibility = Visibility.Collapsed;
        }





        private void ShowVerifyError(string message)
        { 
            VerifyErrorMessage.Text = message; 
            VerifyErrorMessage.Visibility = Visibility.Visible;
        }


        private void ShowError(string message)
        {
            ErrorMessage.Text = message;
            ErrorMessage.Visibility = Visibility.Visible;
        }
        // ****************************************************************
        // Function: SignIn_Click
        // Description: Handle signin button click, takes user to the signin page
        // Notes: N/A
        private void SignIn_Click(object sender, MouseButtonEventArgs e)
        {
            SigninPage signin = new SigninPage();
            signin.Show();
            this.Close();
        }
    }
}


