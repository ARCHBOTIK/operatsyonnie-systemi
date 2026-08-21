using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace SecurePassword;

public enum AtomicWriteStage
{
    TempCreated,
    DuringWrite,
    AfterFlush,
    BeforeCommit
}

internal class FileWorker
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(StringComparer.OrdinalIgnoreCase);

    internal static Action<string, AtomicWriteStage>? TestingFailPointHook { get; set; }
    internal static string? TestingAppDataDirectory { get; set; }

    public static string GetAppDataDirectory()
    {
        if (!string.IsNullOrWhiteSpace(TestingAppDataDirectory))
            return TestingAppDataDirectory;

        try
        {
            return FileSystem.AppDataDirectory;
        }
        catch
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
    }

    public static string ResolvePath(string fileNameOrPath)
    {
        return Path.IsPathRooted(fileNameOrPath)
            ? fileNameOrPath
            : Path.Combine(GetAppDataDirectory(), fileNameOrPath);
    }

    public static byte[] readFile(string fileName)
    {
        string path = ResolvePath(fileName);
        SemaphoreSlim fileLock = GetFileLock(path);
        fileLock.Wait();
        try
        {
            return File.ReadAllBytes(path);
        }
        finally
        {
            fileLock.Release();
        }
    }

    public static void writeFile(byte[] bytes, string fileName)
    {
        WriteFileAtomically(bytes, fileName);
    }

    public static void WriteFileAtomically(byte[] bytes, string fileName)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        string targetPath = ResolvePath(fileName);
        string? targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        SemaphoreSlim fileLock = GetFileLock(targetPath);
        fileLock.Wait();
        try
        {
            string fileNameOnly = Path.GetFileName(targetPath);
            string tempFileName = $".{fileNameOnly}.{Guid.NewGuid():N}.tmp";
            string tempPath = Path.Combine(targetDir ?? GetAppDataDirectory(), tempFileName);

            try
            {
                TestingFailPointHook?.Invoke(targetPath, AtomicWriteStage.TempCreated);

                using (var fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    TestingFailPointHook?.Invoke(targetPath, AtomicWriteStage.DuringWrite);
                    fileStream.Write(bytes, 0, bytes.Length);
                    fileStream.Flush(flushToDisk: true);
                }

                TestingFailPointHook?.Invoke(targetPath, AtomicWriteStage.AfterFlush);
                TestingFailPointHook?.Invoke(targetPath, AtomicWriteStage.BeforeCommit);

                CommitFile(tempPath, targetPath);
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }
        finally
        {
            fileLock.Release();
        }
    }

    private static void CommitFile(string tempPath, string targetPath)
    {
#if ANDROID
        File.Move(tempPath, targetPath, overwrite: true);
#else
        if (File.Exists(targetPath))
        {
            File.Replace(tempPath, targetPath, null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, targetPath);
        }
#endif
    }

    private static SemaphoreSlim GetFileLock(string fullPath)
    {
        string normalized = Path.GetFullPath(fullPath);
        return FileLocks.GetOrAdd(normalized, _ => new SemaphoreSlim(1, 1));
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    public static void CleanupLeftoverTempFiles()
    {
        try
        {
            string baseDir = GetAppDataDirectory();
            if (!Directory.Exists(baseDir))
                return;

            var tempFiles = Directory.GetFiles(baseDir, ".*.tmp");
            foreach (var tempFile in tempFiles)
            {
                TryDeleteFile(tempFile);
            }
        }
        catch
        {
        }
    }
}