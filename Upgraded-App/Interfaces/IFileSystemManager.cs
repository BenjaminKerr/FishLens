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
        // **************************************************
        // Function: CreateDirectoryIfNotExists
        // Description: Ensures the specified directory exists, creating it if necessary.
        // **************************************************
        public abstract bool CreateDirectoryIfNotExists(string path);

        // **************************************************
        // Function: CopyFile
        // Description: Copies a file from sourcePath to destinationPath.
        // **************************************************
        public abstract bool CopyFile(string sourcePath, string destinationPath);

        // **************************************************
        // Function: ListFiles
        // Description: Lists files contained in the specified directory.
        // **************************************************
        public abstract IEnumerable<string> ListFiles(string directoryPath);
    }
}
