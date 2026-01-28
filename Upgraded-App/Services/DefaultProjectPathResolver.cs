// **************************************************
// ***********************************
// File: DefaultProjectPathResolver.cs
// Description: Implementations for Path Resolvers
// Author: Benjamin Kerr
// 2025 - 2026
// ***********************************
// **************************************************

using FishLens_App.Interfaces;
using Microsoft.Win32;
using System;
using System.IO;

namespace FishLens_App.Services
{
    public class DefaultProjectPathResolver : IProjectPathResolver
    {
        // **************************************************
        // Function: ResolveProjectRoot
        // Description: Resolves the Project Root
        // **************************************************
        public string ResolveProjectRoot()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            return Directory.GetParent(baseDirectory).Parent.Parent.Parent.Parent.FullName;
        }

        // **************************************************
        // Function: ResolvePath
        // Description: Resolves the a Path
        // **************************************************
        public string ResolvePath(string subdirectory)
        {
            return Path.Combine(ResolveProjectRoot(), subdirectory);
        }

        // **************************************************
        // Function: ResolveYoloScriptPath
        // Description: Resolves the Yolo Script Path
        // **************************************************
        public string ResolveYoloScriptPath()
        {
            return Path.Combine(ResolveProjectRoot(), "main.py");
        }

        // **************************************************
        // Function: ResolveCsvScriptDirectory
        // Description: Resolves the CSV Script Path
        // **************************************************
        public string ResolveCsvScriptDirectory()
        {
            return Path.Combine(ResolveProjectRoot(), "fish_summary.csv");
        }

        // **************************************************
        // Function: ResolveSourceFolder
        // Description: Resolves the Source Folder
        // **************************************************
        public string ResolveSourceFolder()
        {
            // User opens a folder full of videos
            OpenFolderDialog openFolderDialog = new OpenFolderDialog();
            openFolderDialog.Title = "Select a folder full of video files for analysis";
            string sourceFolderPath = string.Empty;
            if (openFolderDialog.ShowDialog() == true)
            {
                sourceFolderPath = openFolderDialog.FolderName;
            }
            return sourceFolderPath;
        }
    }
}
