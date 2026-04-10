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
        string ResolveProjectRoot();

        // **************************************************
        // Function: ResolvePath
        // Description: Resolves a path relative to the project root.
        // **************************************************
        string ResolvePath(string subdirectory);

        // **************************************************
        // Function: ResolveYoloScriptPath
        // Description: Resolves the path to the YOLO script used for analysis.
        // **************************************************
        string ResolveYoloScriptPath();

        // **************************************************
        // Function: ResolveCsvScriptPath
        // Description: Resolves the path to the CSV script/data file used by the application.
        // **************************************************
        string ResolveCsvScriptPath();

        // **************************************************
        // Function: ResolveNoFishCsvPath
        // Description: Resolves the path to the no-fish summary CSV.
        // **************************************************
        string ResolveNoFishCsvPath();

        // **************************************************
        // Function: ResolveSourceFolder
        // Description: Prompts or determines the source folder containing videos for analysis.
        // **************************************************
        string ResolveSourceFolder();
    }
}
