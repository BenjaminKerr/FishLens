using System;
using System.IO;
using FishLens_App.Services;
using Xunit;

namespace FishLens.Tests
{
    public class StandardFileSystemManagerTests : IDisposable
    {
        private readonly string _tempDir;

        public StandardFileSystemManagerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "FishLensTest_" + Guid.NewGuid().ToString("N"));
        }

        [Fact]
        public void CreateDirectoryIfNotExists_CreatesDirectory_ReturnsTrue()
        {
            var manager = new StandardFileSystemManager();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);

            var result = manager.CreateDirectoryIfNotExists(_tempDir);

            Assert.True(result);
            Assert.True(Directory.Exists(_tempDir));
        }

        [Fact]
        public void CopyFile_CopiesFile_ReturnsTrue()
        {
            var manager = new StandardFileSystemManager();
            var src = Path.Combine(_tempDir, "src.txt");
            var dst = Path.Combine(_tempDir, "sub", "dst.txt");
            Directory.CreateDirectory(_tempDir);
            File.WriteAllText(src, "hello");

            var result = manager.CopyFile(src, dst);

            Assert.True(result);
            Assert.True(File.Exists(dst));
            Assert.Equal("hello", File.ReadAllText(dst));
        }

        [Fact]
        public void ListFiles_ReturnsFiles()
        {
            var manager = new StandardFileSystemManager();
            Directory.CreateDirectory(_tempDir);
            var f1 = Path.Combine(_tempDir, "a.txt");
            var f2 = Path.Combine(_tempDir, "b.txt");
            File.WriteAllText(f1, "1");
            File.WriteAllText(f2, "2");

            var files = manager.ListFiles(_tempDir);

            Assert.Contains(f1, files);
            Assert.Contains(f2, files);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
