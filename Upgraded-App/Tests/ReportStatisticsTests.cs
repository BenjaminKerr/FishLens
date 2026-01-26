using System;
using System.Collections.Generic;
using FishLens_App;
using Xunit;

namespace FishLens.Tests
{
    public class ReportStatisticsTests
    {
        [Fact]
        public void CanAssign_DictionariesAndValues()
        {
            var s = new ReportStatistics();
            s.VideoDetections = new Dictionary<string,int> { ["a"] = 1 };
            s.SpeciesBreakdown = new Dictionary<string,int>();
            s.DetectionsByHour = new Dictionary<int,int>();
            s.DetectionsByDate = new Dictionary<DateTime,int>();

            Assert.Equal(1, s.VideoDetections["a"]);
            Assert.NotNull(s.SpeciesBreakdown);
        }
    }
}
