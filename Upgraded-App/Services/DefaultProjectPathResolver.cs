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
            var projectRoot = Directory.GetParent(baseDirectory)?.Parent?.Parent?.Parent?.Parent?.FullName;

            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new InvalidOperationException("Unable to resolve project root.");
            }
            return projectRoot;
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
        // Function: ResolveAllHistoryFolder
        // Description: Resolves the All History root folder
        // **************************************************
        public string ResolveAllHistoryFolder()
        {
            return Path.Combine(ResolveProjectRoot(), "All History");
        }

        // **************************************************
        // Function: ResolveRunFolder
        // Description: Resolves the folder for a named seasonal run inside All History
        // **************************************************
        public string ResolveRunFolder(string runName)
        {
            return Path.Combine(ResolveAllHistoryFolder(), runName);
        }

        // **************************************************
        // Function: ResolveRunCsvPath
        // Description: Resolves the run-master fish CSV for a given run
        // **************************************************
        public string ResolveRunCsvPath(string runName)
        {
            return Path.Combine(ResolveRunFolder(runName), "run_master.csv");
        }

        // **************************************************
        // Function: ResolveRunNoFishCsvPath
        // Description: Resolves the run-master no-fish CSV for a given run
        // **************************************************
        public string ResolveRunNoFishCsvPath(string runName)
        {
            return Path.Combine(ResolveRunFolder(runName), "no_fish_summary.csv");
        }

        // **************************************************
        // Function: ResolveSessionCsvPath
        // Description: Resolves the session fish CSV (wiped on startup, appended this session)
        // **************************************************
        public string ResolveSessionCsvPath(string runName)
        {
            return Path.Combine(ResolveRunFolder(runName), "session_fish.csv");
        }

        // **************************************************
        // Function: ResolveSessionNoFishCsvPath
        // Description: Resolves the session no-fish CSV
        // **************************************************
        public string ResolveSessionNoFishCsvPath(string runName)
        {
            return Path.Combine(ResolveRunFolder(runName), "session_no_fish.csv");
        }

        // **************************************************
        // Function: ResolveAllTimeMasterFishCsvPath
        // Description: Resolves the all-time master fish CSV at the All History root
        // **************************************************
        public string ResolveAllTimeMasterFishCsvPath()
        {
            return Path.Combine(ResolveAllHistoryFolder(), "all_history.csv");
        }

        // **************************************************
        // Function: ResolveCsvScriptPath
        // Description: Resolves the active run's primary fish CSV.
        //              Debug runs write to debug.csv instead of run_master.csv.
        // **************************************************
        public string ResolveCsvScriptPath()
        {
            string activeRun = (System.Windows.Application.Current as FishLens_App.App)?.ActiveRun ?? string.Empty;
            if (activeRun.Equals("debug", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(ResolveRunFolder(activeRun), "debug.csv");
            return ResolveRunCsvPath(activeRun);
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
