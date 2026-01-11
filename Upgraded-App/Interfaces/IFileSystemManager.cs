using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishLens_App.Interfaces
{
    public abstract class IFileSystemManager
    {
        public abstract bool EnsureDirectoryExists(string path);
        public abstract bool CopyFile(string sourcePath, string destinationPath);
        public abstract IEnumerable<string> ListFiles(string directoryPath);
    }
}
