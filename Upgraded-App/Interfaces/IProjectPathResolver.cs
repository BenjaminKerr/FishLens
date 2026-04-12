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
        // Function: ResolveRunFolder
        // Description: Resolves the folder for the named seasonal run inside All History.
        // **************************************************
        string ResolveRunFolder(string runName);

        // **************************************************
        // Function: ResolveRunCsvPath
        // Description: Resolves the run-master fish CSV for a given run.
        // **************************************************
        string ResolveRunCsvPath(string runName);

        // **************************************************
        // Function: ResolveRunNoFishCsvPath
        // Description: Resolves the run-master no-fish CSV for a given run.
        // **************************************************
        string ResolveRunNoFishCsvPath(string runName);

        // **************************************************
        // Function: ResolveSessionCsvPath
        // Description: Resolves the session fish CSV (wiped on startup, appended this session).
        // **************************************************
        string ResolveSessionCsvPath(string runName);

        // **************************************************
        // Function: ResolveSessionNoFishCsvPath
        // Description: Resolves the session no-fish CSV.
        // **************************************************
        string ResolveSessionNoFishCsvPath(string runName);

        // **************************************************
        // Function: ResolveAllHistoryFolder
        // Description: Resolves the All History root folder.
        // **************************************************
        string ResolveAllHistoryFolder();

        // **************************************************
        // Function: ResolveAllTimeMasterFishCsvPath
        // Description: Resolves the all-time master fish CSV at the All History root.
        // **************************************************
        string ResolveAllTimeMasterFishCsvPath();

        // **************************************************
        // Function: ResolveSourceFolder
        // Description: Prompts or determines the source folder containing videos for analysis.
        // **************************************************
        string ResolveSourceFolder();
    }
}
