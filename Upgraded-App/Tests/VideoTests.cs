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
                name = "foo.mp4",
                trackId = "1",
                likelyClass = "fish",
                confidence = "99.9%",
                startTime = "0.0",
                endTime = "10.0",
                avgConfidence = 0.999,
                direction = "upstream"
            };

            Assert.Equal("foo.mp4", v.name);
            Assert.Equal("1", v.trackId);
            Assert.Equal("fish", v.likelyClass);
            Assert.Equal("99.9%", v.confidence);
        }
    }
}
