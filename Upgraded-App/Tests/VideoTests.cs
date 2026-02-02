using FishLens_App.Models;
using Xunit;

namespace FishLens.Tests
{
    public class VideoTests
    {
        [Fact]
        public void Video_PropertyAssignment_Works()
        {
            var v = new Video
            {
                Name = "foo.mp4",
                TrackId = "1",
                LikelyClass = "fish",
                Confidence = "99.9%",
                StartTime = "0.0",
                EndTime = "10.0",
                AvgConfidence = 0.999,
                Direction = "upstream"
            };

            Assert.Equal("foo.mp4", v.Name);
            Assert.Equal("1", v.TrackId);
            Assert.Equal("fish", v.LikelyClass);
            Assert.Equal("99.9%", v.Confidence);
        }
    }
}
