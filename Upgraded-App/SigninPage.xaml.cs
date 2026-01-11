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
        private const string CLIENT_ID = "aed2d2f8-02e0-413c-a365-cdf377f5c329";
        private const string AUTHORITY = "https://login.microsoftonline.com/common";
        private string[] SCOPES = new[] { "User.Read", "Files.ReadWrite" };

        // Create client
        private IPublicClientApplication app =
            PublicClientApplicationBuilder.Create(CLIENT_ID)
                .WithAuthority(AUTHORITY)
                .WithRedirectUri("http://localhost")  
                .Build();
        public SigninPage()
        {
            InitializeComponent();
        }

        public async Task<AuthenticationResult> Signin()
        {
            try
            {
                var result = await app
                    .AcquireTokenInteractive(SCOPES)
                    .WithPrompt(Prompt.SelectAccount)
                    .ExecuteAsync();

                // Return the clients access token
                return result;
            }

            catch (MsalException ex)
            {
                MessageBox.Show($"Login failed: {ex.Message}");
                return null;
            }
        }

        //private async void signinbutton(object sender, RoutedEventArgs e)
        //{
        //    var result = await Signin();
        //    if(result != null) {
        //        MessageBox.Show("Sign in successful!");
        //        MainWindow main = new MainWindow(app, result);
        //        main.Show();
        //        this.Close();

        //    }
        //    else
        //        MessageBox.Show("Sign in unsuccesful, retry");

        //}
    
    }
}
