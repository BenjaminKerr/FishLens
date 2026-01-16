// **************************************************
// ***********************************
// File: StandardFileSystemManager.cs
// Description: Implementations for System Management
// Author: Benjamin Kerr
// 2025 - 2026
// ***********************************
// **************************************************

using FishLens_App.Interfaces;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System.IO;

namespace FishLens_App.Services
{
    internal class StandardFileSystemManager : IFileSystemManager
    {
        private readonly ILogger _logger;

        // **************************************************
        // Function: StandardFileSystemManager Constructor
        public StandardFileSystemManager() : this(GetDefaultLogger())
        {
        }

        // **************************************************
        // Function: Gets the Default Logger
        // Description: Creates and returns a logger
        // Notes: TO-DO COMBINE WITH MAINWINDOW GETDEFAULTLOGGER()
        private static ILogger<StandardFileSystemManager> GetDefaultLogger()
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information); //Most verbose setting
            });
            return loggerFactory.CreateLogger<StandardFileSystemManager>();
        }

        // **************************************************
        // Function: Parameterized Constructor
        public StandardFileSystemManager(ILogger<StandardFileSystemManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // **************************************************
        // Function: Copies a File
        // Notes: TO-DO IMPLEMENT COPYFILE
        public override bool CopyFile(string sourcePath, string destinationPath)
        {
            throw new NotImplementedException();
        }


        // **************************************************
        // Function: Creates a Directory if it Doesn't Already Exist
        // Description: Tries to create a directory, returns true if it can, false if not
        public override bool CreateDirectoryIfNotExists(string path)
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

        // **************************************************
        // Function: Lists Files
        // Notes: TO-DO IMPLEMENT LISTFILES
        public override IEnumerable<string> ListFiles(string directoryPath)
        {
            throw new NotImplementedException();
        }
    }
}
