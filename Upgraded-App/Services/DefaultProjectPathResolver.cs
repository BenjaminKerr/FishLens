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
        // Function: Resolves the Project Root
        // Description: Finds the project root and returns it as a string
        public override string ResolveProjectRoot()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            return Directory.GetParent(baseDirectory).Parent.Parent.Parent.Parent.FullName;
        }

        // **************************************************
        // Function: Resolves the a Path
        // Description: Finds the project root and returns it combined with the path
        public override string ResolvePath(string subdirectory)
        {
            return Path.Combine(ResolveProjectRoot(), subdirectory);
        }

        // **************************************************
        // Function: Resolves the Yolo Script Path
        // Description: Finds the project root and returns it combined with "main.py"
        public override string ResolveYoloScriptPath()
        {
            return Path.Combine(ResolveProjectRoot(), "main.py");
        }

        // **************************************************
        // Function: Resolves the CSV Script Path
        // Description: Finds the project root and returns it combined with "fish_summary.csv"
        public override string ResolveCsvScriptDirectory()
        {
            return Path.Combine(ResolveProjectRoot(), "fish_summary.csv");
        }

        // **************************************************
        // Function: Resolves the Source Folder
        // Description: Finds the folder selected by the user and returns it as a string
        public override string ResolveSourceFolder()
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
