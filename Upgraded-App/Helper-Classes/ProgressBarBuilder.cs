// ***************************************************************************************************************************
// File: User.cs
// Description: Helper class to create a dynamic row of rectangles as a collective processing bar.
// Notes: N/A
// ***************************************************************************************************************************
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
        // Description: Builds a new row of bars as a 
        // collective progress bar.
        // Notes: N/A
        public List<VideoProgressStatus> InitialBuild(int count)
        {
            var result = new List<VideoProgressStatus>();

            for (int i = 0; i < count; i++)
            {
                VideoProgressStatus status = new VideoProgressStatus();
                status.SetEmpty();
                result.Add(status);

            }

            return result;
        }
    }
}
