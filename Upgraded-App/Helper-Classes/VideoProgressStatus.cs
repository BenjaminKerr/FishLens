using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishLens_App
{
    internal class VideoProgressStatus
    {
        public int Pid { get; set; }
        public string Status { get; set; }
        public string CurrentVideo { get; set; }
        public int Frame_Current { get; set; }
        public int Frame_Total { get; set; }

    }
}
