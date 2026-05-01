using DocumentFormat.OpenXml.Math;
using Org.BouncyCastle.Security;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace FishLens_App
{
    class ProgressBarBuilder
    {
        // **************************************************
        // Function: Build
        // Description: Builds a new row of bars.
        // **************************************************
        public List<MainWindow.VideoProgressState> Build(int count, int currentIndex)
        {
            var result = new List<MainWindow.VideoProgressState>();

            for (int i = 0; i < count; i++)
            {
                if (i < currentIndex)
                    result.Add(MainWindow.VideoProgressState.Filled);
                else if (i == currentIndex)
                    result.Add(MainWindow.VideoProgressState.Active);
                else
                    result.Add(MainWindow.VideoProgressState.Empty);
            }

            return result;
        }
    }
}
