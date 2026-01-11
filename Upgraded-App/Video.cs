using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishLens_App
{
    public class Video
    {
        public string video { get; set; }
        public string trackId { get; set; }
        public string likelyClass { get; set; }
        public string confidence { get; set; }
        public string startTime { get; set; }
        public string endTime { get; set; }
        public double avgConfidence { get; set; }
        public string direction { get; set; }
    }
}
