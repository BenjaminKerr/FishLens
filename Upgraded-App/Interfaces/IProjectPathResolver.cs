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
    public abstract class IProjectPathResolver
    {
        public abstract string ResolveProjectRoot();
        public abstract string ResolvePath(string subdirectory);
        public abstract string ResolveYoloScriptPath();
        public abstract string ResolveCsvScriptDirectory();
        public abstract string ResolveSourceFolder();
    }
}
