using FishLens_App;
using Xunit;

namespace FishLens.Tests
{
    public class CheckBoxToggleTests
    {
        [Fact]
        public void Defaults_AreFalse()
        {
            var t = new CheckBoxToggle();
            Assert.False(t.OutputBox);
            Assert.False(t.ErrorBox);
        }
    }
}
