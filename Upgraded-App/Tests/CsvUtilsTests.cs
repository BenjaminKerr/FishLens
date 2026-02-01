using System.IO;
using Xunit;
using FishLens_App.Services;

namespace FishLens.Tests
{
    public class CsvUtilsTests
    {
        [Fact]
        public void RemoveVideoFromCsv_RemovesTargetRow_PreservesHeaderAndOthers()
        {
            string temp = Path.GetTempFileName();
            try
            {
                var lines = new[]
                {
                    "filename,track,likely,conf,start,end,avg,direction",
                    "video1.mp4,1,fish,99,0,10,99,up",
                    "video2.mp4,2,not_fish,50,0,5,50,down",
                    "target.mp4,3,fish,80,0,8,80,up"
                };

                File.WriteAllLines(temp, lines);

                CsvUtils.RemoveVideoFromCsv(temp, "target.mp4");

                var remaining = File.ReadAllLines(temp);
                Assert.Equal(3, remaining.Length); // header + 2 rows
                Assert.DoesNotContain("target.mp4", remaining[1]);
                Assert.DoesNotContain("target.mp4", remaining[2 % remaining.Length]);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }
}
