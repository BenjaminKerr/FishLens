using System;
using System.IO;
using Xunit;
using FishLens_App.Services;

namespace FishLens.Tests
{
    public class CsvUtilsReadTests
    {
        [Fact]
        public void ReadVideoFromCsv_ReturnsVideo_WhenPresent()
        {
            string temp = Path.GetTempFileName();
            try
            {
                var lines = new[]
                {
                    "filename,track,likely,conf,start,end,avg,direction",
                    "video1.mp4,1,fish,99,0,10,99,up",
                    "target.mp4,2,fish,80,0,8,80,down"
                };
                File.WriteAllLines(temp, lines);

                var v = CsvUtils.ReadVideoFromCsv(temp, "target.mp4");
                Assert.Equal("target.mp4", v.name);
                Assert.Equal("2", v.trackId);
                Assert.Equal(80, (int)v.avgConfidence);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        [Fact]
        public void ReadVideoFromCsv_ReturnsDefault_WhenMissing()
        {
            string temp = Path.GetTempFileName();
            try
            {
                var lines = new[]
                {
                    "filename,track,likely,conf,start,end,avg,direction",
                    "video1.mp4,1,fish,99,0,10,99,up"
                };
                File.WriteAllLines(temp, lines);

                var v = CsvUtils.ReadVideoFromCsv(temp, "missing.mp4");
                Assert.Equal("missing.mp4", v.name);
                Assert.Equal("N/A", v.likelyClass);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        [Fact]
        public void ReadVideoFromCsv_HandlesMalformedRows()
        {
            string temp = Path.GetTempFileName();
            try
            {
                var lines = new[]
                {
                    "filename,track,likely,conf,start,end,avg,direction",
                    "badrowwithoutcommas",
                    "target.mp4,2,fish,80" // truncated but should still parse safely
                };
                File.WriteAllLines(temp, lines);

                var v = CsvUtils.ReadVideoFromCsv(temp, "target.mp4");
                Assert.Equal("target.mp4", v.name);
                Assert.Equal("2", v.trackId);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }
}
