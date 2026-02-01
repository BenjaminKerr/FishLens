using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace FishLens_App.Services
{
    internal static class FileHelpers
    {
        private const int RETRY_COUNT = 3;
        private const int RETRY_DELAY_MS = 100;

        public static string[] SafeReadAllLines(string path)
        {
            for (int i = 0; i < RETRY_COUNT; i++)
            {
                try
                {
                    if (!File.Exists(path)) return Array.Empty<string>();
                    return File.ReadAllLines(path);
                }
                catch (IOException)
                {
                    Thread.Sleep(RETRY_DELAY_MS * (i + 1));
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(RETRY_DELAY_MS * (i + 1));
                }
            }

            return Array.Empty<string>();
        }

        public static bool SafeWriteAllLines(string path, IEnumerable<string> lines)
        {
            for (int i = 0; i < RETRY_COUNT; i++)
            {
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllLines(path, lines);
                    return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(RETRY_DELAY_MS * (i + 1));
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(RETRY_DELAY_MS * (i + 1));
                }
            }

            return false;
        }

        public static bool SafeCopy(string sourcePath, string destinationPath, bool overwrite = false)
        {
            for (int i = 0; i < RETRY_COUNT; i++)
            {
                try
                {
                    var destDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                    File.Copy(sourcePath, destinationPath, overwrite);
                    return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(RETRY_DELAY_MS * (i + 1));
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(RETRY_DELAY_MS * (i + 1));
                }
            }

            return false;
        }

        public static bool SafeMove(string sourcePath, string destinationPath, bool overwrite = false)
        {
            for (int i = 0; i < RETRY_COUNT; i++)
            {
                try
                {
                    var destDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                    if (overwrite && File.Exists(destinationPath)) File.Delete(destinationPath);
                    File.Move(sourcePath, destinationPath);
                    return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(RETRY_DELAY_MS * (i + 1));
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(RETRY_DELAY_MS * (i + 1));
                }
            }

            return false;
        }

        public static bool SafeDelete(string path)
        {
            for (int i = 0; i < RETRY_COUNT; i++)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(RETRY_DELAY_MS * (i + 1));
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(RETRY_DELAY_MS * (i + 1));
                }
            }

            return false;
        }
    }
}
