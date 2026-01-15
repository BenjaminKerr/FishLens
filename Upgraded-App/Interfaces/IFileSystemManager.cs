// **************************************************
// ***********************************
// File: IFileSystemManager.cs
// Description: Interface for System Management
// Author: Benjamin Kerr
// 2025 - 2026
// ***********************************
// **************************************************

using System.Collections.Generic;

namespace FishLens_App.Interfaces
{
    public abstract class IFileSystemManager
    {
        public abstract bool CreateDirectoryIfNotExists(string path);
        public abstract bool CopyFile(string sourcePath, string destinationPath);
        public abstract IEnumerable<string> ListFiles(string directoryPath);
    }
}
