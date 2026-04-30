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
        // Notes: Currently uses temporary logic that only checks if fields
        //     contain text, not actual database validation.
        public bool Signin(string username, string password)
        {
            bool SigninSuccessful = true;
          //Temp logic below until create database for logins
            if (username.Length == 0 || password.Length == 0)
            {
                SigninSuccessful = false;
            }

            return SigninSuccessful;
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
                MessageBox.Show("Sign in unsuccessful, retry *For testing purposes just fill out both text boxes with anything");
            }
        }
    
    }
}
