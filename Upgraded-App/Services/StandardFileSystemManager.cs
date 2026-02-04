// **************************************************
// ***********************************
// File: StandardFileSystemManager.cs
// Description: Implementations for System Management
// Author: Benjamin Kerr
// 2025 - 2026
// ***********************************
// **************************************************

using FishLens_App.Interfaces;
using FishLens_App.Services;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;

namespace FishLens_App.Services
{
    internal class StandardFileSystemManager : IFileSystemManager
    {
        private readonly ILogger _logger;

        // **************************************************
        // Function: StandardFileSystemManager Constructor
        // Description: Initializes StandardFileSystemManager with default logger
        // **************************************************
        public StandardFileSystemManager() : this(NullLogger<StandardFileSystemManager>.Instance)
        {
        }

        // **************************************************
        // Function: Parameterized Constructor
        // Description: Initializes StandardFileSystemManager with provided logger
        // **************************************************
        public StandardFileSystemManager(ILogger<StandardFileSystemManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // **************************************************
        // Function: CopyFile
        // Description: Copies a File
        // **************************************************
        public bool CopyFile(string sourcePath, string destinationPath)
        {
            try
            {
                // Ensure destination directory exists
                var destDir = Path.GetDirectoryName(destinationPath) ?? string.Empty;
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                File.Copy(sourcePath, destinationPath, overwrite: true);
                _logger.LogInformation("File copied from {Source} to {Destination}", sourcePath, destinationPath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to copy file from {Source} to {Destination}", sourcePath, destinationPath);
                return false;
            }
        }


        // **************************************************
        // Function: CreateDirectoryIfNotExists
        // Description: Creates a Directory if it Doesn't Already Exist
        // **************************************************
        public bool CreateDirectoryIfNotExists(string path)
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
        // Function: ListFiles
        // Description: Lists Files
        // **************************************************
        public IEnumerable<string> ListFiles(string directoryPath)
        {
            try
            {
                if (!Directory.Exists(directoryPath)) return Array.Empty<string>();
                return Directory.GetFiles(directoryPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list files in {Path}", directoryPath);
                return Array.Empty<string>();
            }
        }
    }
}
