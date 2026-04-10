// AuthWindow.xaml.cs
// Shared state and panel navigation for the unified auth window.

using System.Windows;

namespace FishLens_App
{
    public partial class AuthWindow : Window
    {
        // ── Shared state accessible by all partial files ──────────────
        private EmailService emailService = new EmailService();

        // ForgotPassword flow
        private int _resetUserId;

        // SignUp flow
        private string _pendingOrgName;
        private string _pendingEmail;
        private string _pendingUsername;
        private string _pendingPassword;
        private const int AdminRoleId = 1;

        // ── Panel names (mirrors XAML x:Name values) ──────────────────
        // SignInPanel, SignUpPanel, VerifyEmailPanel,
        // ForgotPanel, ResetPanel

        public AuthWindow()
        {
            InitializeComponent();
            ShowPanel("SignInPanel");   // always start at sign-in
        }

        // ── Central panel switcher ─────────────────────────────────────
        // Call this from any partial file to navigate between steps.
        internal void ShowPanel(string panelName)
        {
            // ── Hide all content panels ──────────────────────────────
            SignInPanel.Visibility = Visibility.Collapsed;
            SignUpPanel.Visibility = Visibility.Collapsed;
            VerifyEmailPanel.Visibility = Visibility.Collapsed;
            ForgotPanel.Visibility = Visibility.Collapsed;
            ResetPanel.Visibility = Visibility.Collapsed;

            // ── Hide all footer states ───────────────────────────────
            SignInFooter.Visibility = Visibility.Collapsed;
            SignUpFooter.Visibility = Visibility.Collapsed;
            ForgotFooter.Visibility = Visibility.Collapsed;

            // ── Show the right pair ──────────────────────────────────
            switch (panelName)
            {
                case "SignInPanel":
                    SignInPanel.Visibility = Visibility.Visible;
                    SignInFooter.Visibility = Visibility.Visible;
                    break;

                case "SignUpPanel":
                    SignUpPanel.Visibility = Visibility.Visible;
                    SignUpFooter.Visibility = Visibility.Visible;
                    break;

                case "VerifyEmailPanel":
                    VerifyEmailPanel.Visibility = Visibility.Visible;
                    SignUpFooter.Visibility = Visibility.Visible;  // same footer as sign-up
                    break;

                case "ForgotPanel":
                    ForgotPanel.Visibility = Visibility.Visible;
                    ForgotFooter.Visibility = Visibility.Visible;
                    break;

                case "ResetPanel":
                    ResetPanel.Visibility = Visibility.Visible;
                    ForgotFooter.Visibility = Visibility.Visible;  // same footer as forgot
                    break;
            }
        }


    }
}

