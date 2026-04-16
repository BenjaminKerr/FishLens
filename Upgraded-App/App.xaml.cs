using FishLens_App.Models;
using System;
using System.Windows;

namespace FishLens_App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // Private sets so they aren't accidentally replaced with new instances
        public CheckBoxToggle CheckBoxes { get; private set; }

        public string connectionString =
        "server=aura.cset.oit.edu,5433; " +
        "database=kaharra; " +
        "UID=kaharra; " +
        "password=kaharra";
        public AppConfiguration Configuration { get; private set; }
        public int CurrentUserId { get; set; }
        public string CurrentUsername { get; set; }
        public int CurrentRoleId { get; set; }
        public int CurrentOrganizationId { get; set; }

        public bool IsAdmin => CurrentRoleId == 1;
        public bool IsUser => CurrentRoleId == 2;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e); // Call the regular startup logic first
            CheckBoxes = new CheckBoxToggle();
            Configuration = new AppConfiguration();
        }

        public void ResetSettingsToDefaults()
        {
            CurrentUserId = 0;
            CurrentUsername = null;
            CurrentRoleId = 0;
            CurrentOrganizationId = 0;

            Configuration.ConfidenceThreshold = 0.7;
            Configuration.HighContrastMode = false;
            Configuration.LargeText = false;
            CheckBoxes.OutputBox = false;
            CheckBoxes.ErrorBox = false;

            ApplyCurrentSettings();
        }




        public void ApplyCurrentSettings()
        {
            ThemeHelper.ApplyHighContrastMode(Configuration.HighContrastMode);

            if (Configuration.LargeText)
            {
                Resources["BaseFontSize"] = 18.0;
                Resources["HeaderFontSize"] = 32.0;
                Resources["LargeHeaderFontSize"] = 42.0;
                Resources["TitleFontSize"] = 72.0;
            }
            else
            {
                Resources["BaseFontSize"] = 14.0;
                Resources["HeaderFontSize"] = 24.0;
                Resources["LargeHeaderFontSize"] = 32.0;
                Resources["TitleFontSize"] = 56.0;
            }
        }









    }
}
