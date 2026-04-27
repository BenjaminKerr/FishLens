using System;
using System.IO;
using FishLens_App.Services;
using Xunit;

namespace FishLens.Tests
{
    public class DefaultProjectPathResolverTests
    {
        [Fact]
        public void ResolvePath_CombinesWithProjectRoot()
        {
            var resolver = new DefaultProjectPathResolver();
                var combined = resolver.ResolvePath("foo");
            Assert.False(string.IsNullOrEmpty(combined));
            Assert.True(Path.IsPathRooted(combined));
        }

        [Fact]
        public void ResolveYoloScriptPath_EndsWithMainPy()
        {
            var resolver = new DefaultProjectPathResolver();
            var path = resolver.ResolveYoloScriptPath();
            Assert.EndsWith("main.py", path, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ResolveCsvScriptPath_EndsWithCsv()
        {
            var resolver = new DefaultProjectPathResolver();
            var path = resolver.ResolveCsvScriptPath();
            Assert.EndsWith("run_master.csv", path, StringComparison.OrdinalIgnoreCase);
        }
    }
}
