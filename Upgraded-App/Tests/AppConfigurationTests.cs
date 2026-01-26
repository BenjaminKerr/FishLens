using FishLens_App;
using Xunit;

namespace FishLens.Tests
{
    public class AppConfigurationTests
    {
        [Fact]
        public void Defaults_AreSet()
        {
            var cfg = new AppConfiguration();
            Assert.Equal(0.7, cfg.ConfidenceThreshold);
            Assert.True(cfg.AutoPlayVideos);
            Assert.Equal("Medium", cfg.VideoQuality);
            Assert.False(cfg.HighContrastMode);
            Assert.False(cfg.LargeText);
        }
    }
}
