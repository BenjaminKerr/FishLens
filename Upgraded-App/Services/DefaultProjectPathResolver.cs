using FishLens_App.Interfaces;
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
    }
}
