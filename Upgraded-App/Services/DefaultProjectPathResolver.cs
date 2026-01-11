using FishLens_App.Interfaces;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishLens_App.Services
{
    public class DefaultProjectPathResolver : IProjectPathResolver
    {
        public override string ResolveProjectRoot()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            return Directory.GetParent(baseDirectory).Parent.Parent.Parent.Parent.FullName;
        }
        public override string ResolvePath(string subdirectory)
        {
            return Path.Combine(ResolveProjectRoot(), subdirectory);
        }
        public override string ResolveYoloScriptPath()
        {
            return Path.Combine(ResolveProjectRoot(), "main.py");
        }
        public override string ResolveCsvScriptDirectory()
        {
            return Path.Combine(ResolveProjectRoot(), "fish_summary.csv");
        }
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
