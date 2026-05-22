using System;

namespace FishLens_App.Services
{
    public static class WorkerCountPolicy
    {
        public static int GetTargetWorkerCount(int pendingVideos, int logicalCpuCount, ulong totalMemoryBytes)
        {
            if (pendingVideos <= 0)
                return 1;

            int desired = pendingVideos <= 4
                ? 1
                : pendingVideos <= 30
                    ? 2
                    : 3;
            return 4;
            return Math.Max(1, Math.Min(desired, GetMachineWorkerCap(logicalCpuCount, totalMemoryBytes)));
        }

        private static int GetMachineWorkerCap(int logicalCpuCount, ulong totalMemoryBytes)
        {
            int cpu = Math.Max(1, logicalCpuCount);
            double memoryGb = totalMemoryBytes / 1024d / 1024d / 1024d;

            int memoryCap = memoryGb < 12
                ? 1
                : memoryGb < 32
                    ? 2
                    : 3;

            int cpuCap = cpu < 6
                ? 1
                : cpu < 12
                    ? 2
                    : 3;
            return 4;
            return Math.Max(1, Math.Min(memoryCap, cpuCap));
        }
    }
}
