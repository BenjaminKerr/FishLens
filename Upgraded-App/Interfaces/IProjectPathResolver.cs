// **************************************************
// ***********************************
// File: IProjectPathResolver.cs
// Description: Interface for Path Resolvers
// Author: Benjamin Kerr
// 2025 - 2026
// ***********************************
// **************************************************

namespace FishLens_App.Interfaces
{
    public interface IProjectPathResolver
    {
        // **************************************************
        // Function: ResolveProjectRoot
        // Description: Resolves the project root directory.
        // **************************************************
        public string ResolveProjectRoot();

        // **************************************************
        // Function: ResolvePath
        // Description: Resolves a path relative to the project root.
        // **************************************************
        public string ResolvePath(string subdirectory);

        // **************************************************
        // Function: ResolveYoloScriptPath
        // Description: Resolves the path to the YOLO script used for analysis.
        // **************************************************
        public string ResolveYoloScriptPath();

        // **************************************************
        // Function: ResolveCsvScriptDirectory
        // Description: Resolves the path to the CSV script/data file used by the application.
        // **************************************************
        public string ResolveCsvScriptDirectory();

        // **************************************************
        // Function: ResolveSourceFolder
        // Description: Prompts or determines the source folder containing videos for analysis.
        // **************************************************
        public string ResolveSourceFolder();
    }
}
