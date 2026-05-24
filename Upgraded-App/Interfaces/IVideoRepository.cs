using FishLens_App.Models;
using System.Collections.Generic;
using System.IO;

namespace FishLens_App.Interfaces
{
    public interface IVideoRepository
    {
        void MakeDirectoryIfNotExists(string directory);
        void EnterDataInFile(string inputFolder, string outputDirectory);
        List<(FileInfo vid, Video data)> CreateSortedListOfVideos(string directory);
        Video GetData(string videoFileName);
    }
}
