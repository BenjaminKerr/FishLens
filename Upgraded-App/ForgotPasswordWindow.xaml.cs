using System;
using System.Collections.Generic;
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
using System.Windows.Shapes;
using System.Data.SqlClient;
using System.Configuration;

namespace FishLens_App
{
    /// <summary>
    /// Interaction logic for ForgotPasswordWindow.xaml
    /// </summary>
  

    public partial class ForgotPasswordWindow : Window
    {
        private string connectionString =
         "server=aura.cset.oit.edu,5433; " +
         "database=kaharra; " +
         "UID=kaharra; " +
         "password=kaharra";

        private EmailService emailService = new EmailService();
        private int currentUserId;

        public ForgotPasswordWindow()
        {
            InitializeComponent();
        }

        // STEP 1: User enters username, we send a code to their email
        private void SendCode_Click(object sender, RoutedEventArgs e)
        {
            string username = UsernameBox.Text.Trim();

            if (string.IsNullOrEmpty(username))
            {
                MessageBox.Show("Please enter your username.");
            }
            else
            {
                try
                {
                    using (SqlConnection conn = new SqlConnection(connectionString))
                    {
                        conn.Open();

                        // Look up the user's email
                        string sql = @"SELECT Id, Email FROM [kaharra].[FishLensUsers] 
                               WHERE Username = @user";

                        using (SqlCommand cmd = new SqlCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@user", username);

                            using (SqlDataReader reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    currentUserId = reader.GetInt32(0);
                                    string email = reader.IsDBNull(1) ? null : reader.GetString(1);

                                    if (string.IsNullOrEmpty(email))
                                    {
                                        MessageBox.Show("No email on file. Contact an administrator.");
                                    }
                                    else
                                    {

                                        reader.Close();

                                        // Generate a 6-digit code
                                        string resetCode = new Random().Next(100000, 999999).ToString();

                                        // Store the code in the database with 15 min expiry
                                        string insertSql = @"
                                INSERT INTO [kaharra].[PasswordResetTokens] 
                                    (UserId, Token, ExpiresAt)
                                VALUES 
                                    (@userId, @token, @expires)";

                                        using (SqlCommand insertCmd = new SqlCommand(insertSql, conn))
                                        {
                                            insertCmd.Parameters.AddWithValue("@userId", currentUserId);
                                            insertCmd.Parameters.AddWithValue("@token", resetCode);
                                            insertCmd.Parameters.AddWithValue("@expires",
                                                DateTime.Now.AddMinutes(15));
                                            insertCmd.ExecuteNonQuery();
                                        }

                                        // Send the email
                                        bool sent = emailService.SendResetCode(email, resetCode);

                                        if (sent)
                                        {
                                            MessageBox.Show("A reset code has been sent to your email.");
                                            EmailStep.Visibility = Visibility.Collapsed;
                                            ResetStep.Visibility = Visibility.Visible;
                                        }
                                        else
                                        {
                                            MessageBox.Show("Failed to send email.");
                                        }
                                    }
                                }
                                else
                                {
                                    // Doesn't reveal if username exists or not
                                    MessageBox.Show("If that username exists, a code has been sent.");
                                }
                                
                            }
                        }
                    }
                }
                catch (SqlException ex)
                {
                    System.Diagnostics.Debug.WriteLine("SQL Exception: " + ex.Message);
                    MessageBox.Show("An error occurred. Please try again.");
                }
            }
        }

        // STEP 2: User enters the code and new password
        private void ResetPassword_Click(object sender, RoutedEventArgs e)
        {
            string code = CodeBox.Text.Trim();
            string newPassword = NewPasswordBox.Password;
            string confirmPassword = ConfirmPasswordBox.Password;

            if (newPassword != confirmPassword)
            {
                MessageBox.Show("Passwords do not match.");
                return;
            }

            if (string.IsNullOrEmpty(newPassword))
            {
                MessageBox.Show("Password cannot be empty.");
                return;
            }

            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();

                    // Verify the code is valid and not expired
                    string checkSql = @"
                    SELECT Id FROM [kaharra].[PasswordResetTokens]
                    WHERE UserId = @userId 
                      AND Token = @token 
                      AND ExpiresAt > GETDATE() 
                      AND Used = 0";

                    using (SqlCommand cmd = new SqlCommand(checkSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@userId", currentUserId);
                        cmd.Parameters.AddWithValue("@token", code);

                        object result = cmd.ExecuteScalar();

                        if (result != null)
                        {
                            int tokenId = Convert.ToInt32(result);

                            // Mark token as used
                            string updateToken = @"UPDATE [kaharra].[PasswordResetTokens] 
                                               SET Used = 1 WHERE Id = @id";
                            using (SqlCommand updateCmd = new SqlCommand(updateToken, conn))
                            {
                                updateCmd.Parameters.AddWithValue("@id", tokenId);
                                updateCmd.ExecuteNonQuery();
                            }

                            // Reset the password using your stored procedure
                            using (SqlCommand resetCmd = new SqlCommand(
                                "kaharra.ResetPassword", conn))
                            {
                                resetCmd.CommandType = System.Data.CommandType.StoredProcedure;
                                resetCmd.Parameters.AddWithValue("@pUserId", currentUserId);
                                resetCmd.Parameters.AddWithValue("@pNewPassword", newPassword);
                                resetCmd.ExecuteNonQuery();
                            }

                            MessageBox.Show("Password reset successfully! You can now sign in.");
                            this.Close();
                        }
                        else
                        {
                            MessageBox.Show("Invalid or expired code. Please try again.");
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                System.Diagnostics.Debug.WriteLine("SQL Exception: " + ex.Message);
                MessageBox.Show("An error occurred. Please try again.");
            }
        }
    }

}
