using Org.BouncyCastle.Security;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace FishLens_App.Helper_Classes
{
    class ProgressBarBuilder
    {
        // **************************************************
        // Function: Build
        // Description: Builds a new row of bars.
        // **************************************************
            public List<bool> Build(int count, int active)
            {
                var result = new List<bool>();

                for (int i = 0; i < count; i++)
                    result.Add(i < active);

                return result;
            }
    }
}
