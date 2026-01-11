using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishLens_App
{
    public class Startup
    {
        public static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Configure logging
            services.AddLogging(configure =>
            {
                configure.SetMinimumLevel(LogLevel.Information);
            });

            return services.BuildServiceProvider();
        }
    }
}
