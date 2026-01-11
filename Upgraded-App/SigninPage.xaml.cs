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
            bool SigninSuccessful = false;
          //Temp logic below until create database for logins
            if (username == null || password == null)
            {
                SigninSuccessful = true;
            }

            return SigninSuccessful;
        }

        public void SigninButtonClick(object sender, RoutedEventArgs e)
        {
            bool Status;

            String username = UserName.Text;
            
            String password = PassWord.ToString();
            Status = Signin(username, password);
        }
    
    }
}
