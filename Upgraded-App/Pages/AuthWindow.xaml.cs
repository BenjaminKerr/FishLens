// ***************************************************************************************************************************
// File: AuthWindow.xaml.cs
// Description: Code-behind for the authentication window: sign-in, sign-up, password reset and email verification flows.
// Notes: N/A
// ***************************************************************************************************************************

using System.Windows;

namespace FishLens_App
{
    public partial class AuthWindow : Window
    {
        private EmailService emailService = new EmailService();

        private int _resetUserId;

        private string _pendingOrgName;
        private string _pendingEmail;
        private string _pendingUsername;
        private string _pendingPassword;
        private const int AdminRoleId = 1;

        // ****************************************************************
        // Function: AuthWindow
        // Description: Initializes UI components and shows the default panel.
        // Notes: Displays the "SignInPanel" on startup.
        public AuthWindow()
        {
            InitializeComponent();
            ShowPanel("SignInPanel");
        }

        // ****************************************************************
        // Function: ShowPanel
        // Description: Sets visibility for content panels and corresponding footers based on the requested panel name.
        // Notes: Valid panel names: "SignInPanel", "SignUpPanel", "VerifyEmailPanel", "ForgotPanel", "ResetPanel".
        internal void ShowPanel(string panelName)
        {
            SignInPanel.Visibility = Visibility.Collapsed;
            SignUpPanel.Visibility = Visibility.Collapsed;
            VerifyEmailPanel.Visibility = Visibility.Collapsed;
            ForgotPanel.Visibility = Visibility.Collapsed;
            ResetPanel.Visibility = Visibility.Collapsed;

            switch (panelName)
            {
                case "SignInPanel":
                    SignInPanel.Visibility = Visibility.Visible;
                    break;

                case "SignUpPanel":
                    SignUpPanel.Visibility = Visibility.Visible;
                    break;

                case "VerifyEmailPanel":
                    VerifyEmailPanel.Visibility = Visibility.Visible;
                    break;

                case "ForgotPanel":
                    ForgotPanel.Visibility = Visibility.Visible;
                    break;

                case "ResetPanel":
                    ResetPanel.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void GoToForgotPassword_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel("ForgotPanel");
        }
    }
}

