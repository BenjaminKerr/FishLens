using System;
using System.IO;
using Xunit;
using FishLens_App.Services;

namespace FishLens.Tests
{
    public class CsvUtilsUpdateTests
    {
        [Fact]
        public void UpdateCsvRow_ReplacesTargetRow()
        {
            string temp = Path.GetTempFileName();
            try
            {
                var lines = new[]
                {
                    "filename,track,likely,conf,start,end,avg,direction",
                    "video1.mp4,1,fish,99,0,10,99,up",
                    "target.mp4,2,fish,80,0,8,80,down",
                    "video3.mp4,3,not_fish,50,0,5,50,up"
                };

                File.WriteAllLines(temp, lines);

                string updatedRow = "target.mp4,2,fish,85,0,8,85,down";
                CsvUtils.UpdateCsvRow(temp, "target.mp4", updatedRow);

                var remaining = File.ReadAllLines(temp);
                // header + 3 rows
                Assert.Equal(4, remaining.Length);
                Assert.Contains(updatedRow, remaining);
                Assert.DoesNotContain("target.mp4,2,fish,80,0,8,80,down", remaining);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        [Fact]
        public void UpdateCsvRow_ThrowsWhenTargetMissing()
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

                Assert.Throws<InvalidOperationException>(() => CsvUtils.UpdateCsvRow(temp, "missing.mp4", "missing.mp4,1,fish,0,0,0,0,up"));
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        [Fact]
        public void UpdateCsvRow_EmptyFile_Noop()
        {
            string temp = Path.GetTempFileName();
            try
            {
                // empty file
                File.WriteAllText(temp, string.Empty);

                // Should not throw and should remain empty
                CsvUtils.UpdateCsvRow(temp, "any.mp4", "any.mp4,1,fish,0,0,0,0,up");
                var content = File.ReadAllText(temp);
                Assert.Equal(string.Empty, content);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }
}
