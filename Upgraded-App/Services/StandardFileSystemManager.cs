using FishLens_App.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.IO;

namespace FishLens_App.Services
{
    internal class StandardFileSystemManager : IFileSystemManager
    {
        private readonly ILogger _logger;

        public StandardFileSystemManager() : this(GetDefaultLogger())
        {
        }
        private static ILogger<StandardFileSystemManager> GetDefaultLogger()
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
            });
            return loggerFactory.CreateLogger<StandardFileSystemManager>();
        }
        public StandardFileSystemManager(ILogger<StandardFileSystemManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public override bool CopyFile(string sourcePath, string destinationPath)
        {
            throw new NotImplementedException();
        }

        public override bool EnsureDirectoryExists(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                _logger.LogInformation("Directory created: {Path}", path);
                return true;
            }
            catch (Exception ex) 
            {
                _logger.LogError(ex, "Failed to create directory: {Path}", path);
                return false;
            }
        }

        public override IEnumerable<string> ListFiles(string directoryPath)
        {
            throw new NotImplementedException();
        }
    }
}
