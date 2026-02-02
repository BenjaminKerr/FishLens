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
    public interface IFileSystemManager
    {
        // **************************************************
        // Function: CreateDirectoryIfNotExists
        // Description: Ensures the specified directory exists, creating it if necessary.
        // **************************************************
        bool CreateDirectoryIfNotExists(string path);

        // **************************************************
        // Function: CopyFile
        // Description: Copies a file from sourcePath to destinationPath.
        // **************************************************
        bool CopyFile(string sourcePath, string destinationPath);

        // **************************************************
        // Function: ListFiles
        // Description: Lists files contained in the specified directory.
        // **************************************************
        IEnumerable<string> ListFiles(string directoryPath);
    }
}
