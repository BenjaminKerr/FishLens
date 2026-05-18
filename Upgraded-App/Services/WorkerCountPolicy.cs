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
                : pendingVideos <= 12
                    ? 2
                    : pendingVideos <= 30
                        ? 3
                        : 4 + Math.Max(0, (pendingVideos - 31) / 30);

            return Math.Max(1, Math.Min(desired, GetMachineWorkerCap(logicalCpuCount, totalMemoryBytes)));
        }

        private static int GetMachineWorkerCap(int logicalCpuCount, ulong totalMemoryBytes)
        {
            int cpu = Math.Max(1, logicalCpuCount);
            double memoryGb = totalMemoryBytes / 1024d / 1024d / 1024d;

            int memoryCap = memoryGb < 12
                ? 1
                : memoryGb < 24
                    ? 2
                    : memoryGb < 48
                        ? 4
                        : 5;

            int cpuCap = cpu < 6
                ? 1
                : cpu < 10
                    ? 2
                    : cpu < 16
                        ? 4
                        : 5;

            return Math.Max(1, Math.Min(memoryCap, cpuCap));
        }
    }
}
