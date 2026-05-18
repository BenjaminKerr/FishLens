using FishLens_App.Services;
using Xunit;

namespace FishLens.Tests
{
    public class WorkerCountPolicyTests
    {
        private const ulong Gb = 1024UL * 1024UL * 1024UL;

        [Theory]
        [InlineData(1, 1)]
        [InlineData(4, 1)]
        [InlineData(5, 2)]
        [InlineData(12, 2)]
        [InlineData(13, 3)]
        [InlineData(30, 3)]
        [InlineData(31, 4)]
        public void StrongMachineUsesVideoCountTiers(int pendingVideos, int expectedWorkers)
        {
            int workers = WorkerCountPolicy.GetTargetWorkerCount(pendingVideos, logicalCpuCount: 16, totalMemoryBytes: 64 * Gb);

            Assert.Equal(expectedWorkers, workers);
        }

        [Fact]
        public void LowMemoryMachineStaysAtOneWorker()
        {
            int workers = WorkerCountPolicy.GetTargetWorkerCount(pendingVideos: 12, logicalCpuCount: 8, totalMemoryBytes: 8 * Gb);

            Assert.Equal(1, workers);
        }

        [Fact]
        public void MidRangeMachineCapsAtTwoWorkers()
        {
            int workers = WorkerCountPolicy.GetTargetWorkerCount(pendingVideos: 30, logicalCpuCount: 8, totalMemoryBytes: 16 * Gb);

            Assert.Equal(2, workers);
        }
    }
}
