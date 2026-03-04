using FishLens_App.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Configuration.Internal;
using System.Text.Json;
using System.Windows;

namespace FishLens_App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // Private sets so they aren't accidentally replaced with new instances
        public static IConfiguration Configuration { get; private set; }
        public static UserSettings Settings { get; set; }
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e); // Call the regular startup logic first

            Configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            Settings = Configuration.GetSection("UserSettings").Get<UserSettings>();
        }
    }
}
