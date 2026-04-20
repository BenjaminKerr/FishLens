using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
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
using System.Data.SqlClient;

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

        public SignUpPage()
        {
            InitializeComponent();
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
                            using (SqlCommand cmd = new SqlCommand("kaharra.CreateOrganization", conn))
                            {
                                cmd.CommandType = System.Data.CommandType.StoredProcedure;

                                cmd.Parameters.AddWithValue("@pOrgName", orgName);
                                cmd.Parameters.AddWithValue("@pUser", username);
                                cmd.Parameters.AddWithValue("@pPassword", password);
                                cmd.Parameters.AddWithValue("@pRoleId", AdminRoleId);
                                cmd.Parameters.AddWithValue("@pEmail", email);

                                var orgIdParam = new SqlParameter("@pOrgId", System.Data.SqlDbType.Int)
                                {
                                    Direction = System.Data.ParameterDirection.Output
                                };
                                cmd.Parameters.Add(orgIdParam);

                                var userIdParam = new SqlParameter("@pUserId", System.Data.SqlDbType.Int)
                                {
                                    Direction = System.Data.ParameterDirection.Output
                                };
                                cmd.Parameters.Add(userIdParam);

                                cmd.ExecuteNonQuery();

                                var app = Application.Current as App;
                                app.CurrentUserId = (int)userIdParam.Value;
                                app.CurrentUsername = username;
                                app.CurrentRoleId = AdminRoleId;
                                app.CurrentOrganizationId = (int)orgIdParam.Value;
                            }

                            (Application.Current as App)?.EnsureRunStorageInitialized();
                            MainWindow main = new MainWindow();
                            main.Show();
                            this.Close();
                        }
                    }
                }
                catch (SqlException ex)
                {
                    Debug.WriteLine("SQL Exception: " + ex.Message);
                    ShowError("Something went wrong creating your account. Please try again.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Exception: " + ex.Message);
                    ShowError("An unexpected error occurred. Please try again.");
                }
            }
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


