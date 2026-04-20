using FishLens_App;
using FishLens_App.Models;
using Xunit;

namespace FishLens.Tests
{
    public class CheckBoxToggleTests
    {
        [Fact]
        public void Defaults_AreFalse()
        {
            var t = new CheckBoxToggle();
            Assert.False(t.FastMode);
        }
    }
}
