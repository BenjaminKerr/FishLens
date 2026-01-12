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

    /// <summary>
    /// Interaction logic for SigninPage.xaml
    /// </summary>
    public partial class SigninPage : Window
    {
    
        public SigninPage()
        {
            InitializeComponent();
        }

        public bool Signin(string username, string password)
        {
            bool SigninSuccessful = true;
          //Temp logic below until create database for logins
            if (username.Length== 0 || password.Length == 0)
            {
                SigninSuccessful = false;
            }

            return SigninSuccessful;
        }

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
