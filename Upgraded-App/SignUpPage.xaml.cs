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
            ClearAllFieldErrors();

            string orgName = OrgName.Text.Trim();
            string email = NewEmail.Text.Trim();
            string username = NewUsername.Text.Trim();
            string password = NewPassword.Password;
            string confirmPassword = ConfirmPassword.Password;

            // Field validation via shared class
            var fieldResult = UserValidationRules.ValidateSignUpFields(
                orgName, email, username, password, confirmPassword);

            UpdateRequirements(username, password, confirmPassword);

            if (!fieldResult.IsValid)
            {
                ApplyFieldErrors(fieldResult);

            }
            else
            {
                try
                {
                    using (SqlConnection conn = new SqlConnection((Application.Current as App).connectionString))
                    {
                        conn.Open();

                        // DB duplicate checks via shared class
                        var dbResult = UserValidationRules.ValidateSignUpDb(conn, orgName, email, username);

                        if (!dbResult.IsValid)
                        {
                            ApplyFieldErrors(dbResult);

                        }
                        else
                        {
                            SendVerificationCode(conn, email);

                            _pendingEmail = email;
                            _pendingPassword = password;

                            SignUpStep.Visibility = Visibility.Collapsed;
                            VerifyEmailStep.Visibility = Visibility.Visible;
                        }
                    }
                }
                catch (SqlException ex)
                {
                    Debug.WriteLine("SQL Exception: " + ex.Message);
                    ShowFieldError(OrgNameError, "Something went wrong. Please try again.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Exception: " + ex.Message);
                    ShowFieldError(OrgNameError, "An unexpected error occurred. Please try again.");
                }
            }
        }


        private void ApplyFieldErrors(ValidationResult result)
        {
            if (result.HasErrorFor("orgName"))
                ShowFieldError(OrgNameError, result.GetError("orgName"));
            if (result.HasErrorFor("email"))
                ShowFieldError(EmailError, result.GetError("email"));
            if (result.HasErrorFor("username"))
                ShowFieldError(UsernameError, result.GetError("username"));
            if (result.HasErrorFor("password"))
                ShowFieldError(PasswordError, result.GetError("password"));
            if (result.HasErrorFor("confirmPassword"))
                ShowFieldError(ConfirmPasswordError, result.GetError("confirmPassword"));
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

            bool sent = emailService.SendVerificationCode(email, code); 
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

            }
            else
            {

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

                            }
                            else
                            {
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
        }


        // ****************************************************************
        // Helper: ClearAllFieldErrors
        // Description: Hides all inline field error messages before re-validating
        // ****************************************************************
        private void ClearAllFieldErrors()
        {
            OrgNameError.Visibility = Visibility.Collapsed;
            EmailError.Visibility = Visibility.Collapsed;
            UsernameError.Visibility = Visibility.Collapsed;
            PasswordError.Visibility = Visibility.Collapsed;
            ConfirmPasswordError.Visibility = Visibility.Collapsed;
        }

        // ****************************************************************
        // Helper: ShowFieldError
        // Description: Sets text and makes a specific field error visible
        // ****************************************************************
        private void ShowFieldError(TextBlock block, string message)
        {
            block.Text = message;
            block.Visibility = Visibility.Visible;
        }

        // ****************************************************************
        // Helper: UpdateRequirements
        // Description: Colors requirement hints green/red on submit.
        //              Password match stays gray when both boxes are empty.
        // ****************************************************************
        private void UpdateRequirements(string username, string password, string confirmPassword)
        {
            Brush gray = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888"));
            Brush pass = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
            Brush fail = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336"));

            ReqUsernameLength.Foreground = username.Length >= 6 ? pass : fail;
            ReqPasswordLength.Foreground = password.Length >= 6 ? pass : fail;

            // Stay gray when both are empty
            if (string.IsNullOrEmpty(password) && string.IsNullOrEmpty(confirmPassword))
                ReqPasswordMatch.Foreground = gray;
            else
                ReqPasswordMatch.Foreground = password == confirmPassword ? pass : fail;
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


