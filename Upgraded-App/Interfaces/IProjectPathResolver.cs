using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishLens_App.Interfaces
{
    public abstract class IProjectPathResolver
    {
        public abstract string ResolveProjectRoot();
        public abstract string ResolvePath(string subdirectory);
        public abstract string ResolveYoloScriptPath();
    }
}
