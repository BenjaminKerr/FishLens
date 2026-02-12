// ***************************************************************************************************************************
// File: SigninPage.xaml.cs
// Description: This is the C# code for our signin page which handles the logic of logging in and unsuccesful logins
// Notes: Currently the login functionality is temporary, it does not actually check login information only whether or not it 
// contains text.
// ***************************************************************************************************************************

using DocumentFormat.OpenXml.Drawing.Charts;
using Microsoft.Identity.Client;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
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


namespace FishLens_App
{
    public partial class SigninPage : Window
    {
        private string connectionString = "server=aura.cset.oit.edu,5433; " +
   "database=kaharra; " +
   "UID=kaharra; " +
   "password=kaharra";

        // ****************************************************************
        // Function: SigninPage
        // Description: Initialize the signin page window.
        // Notes: N/A
        public SigninPage()
        {
            InitializeComponent();
        }

        // ****************************************************************
        // Function: Signin
        // Description: Validate username and password credentials for signin
        //     authorization.
        // Notes: Current sign in proccess unsalts with a stored procedure
        public bool Signin(string username, string password)
        {
            bool signinSuccessful = false;

            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();

                    using (SqlCommand cmd = new SqlCommand("kaharra.Unsalt", conn))
                    {
                        cmd.CommandType = System.Data.CommandType.StoredProcedure;

                        cmd.Parameters.AddWithValue("@pUser", username);
                        cmd.Parameters.AddWithValue("@pPassword", password);

                        int result = Convert.ToInt32(cmd.ExecuteScalar());

                        signinSuccessful = (result == 1);
                    }
                }
            }
            catch (SqlException ex)
            {
                Debug.WriteLine("SQL Exception: " + ex.Message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception: " + ex.Message);
            }

            return signinSuccessful;
        }



        // ****************************************************************
        // Function: SigninButtonClick
        // Description: Handle signin button click, validate credentials, and
        //     navigate to main window on success or display error message.
        // Notes: N/A
        public void SigninButtonClick(object sender, RoutedEventArgs e)
        {
            bool Status;

            String username = UserName.Text;
            
            String password = PassWord.Password;
            Status = Signin(username, password);
            if (Status == true)
            {
                MainWindow main = new MainWindow();
                main.Show();
                this.Close();
            }
            else
            {
                MessageBox.Show("Sign in unsuccessful, retry *For testing purposes username = Fishlens1 and Password = Testing1! case sensitive");
            }
        }

       

    }
}
