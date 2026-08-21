using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SecurePassword;

public enum TransactionState
{
    Prepared,
    Committing,
    Committed,
    RolledBack
}

public sealed class TransactionFileRecord
{
    public string FileName { get; set; } = string.Empty;
    public bool ExistedBefore { get; set; }
    public string TargetPath { get; set; } = string.Empty;
    public string RelativeNewPath { get; set; } = string.Empty;
    public string RelativeBackupPath { get; set; } = string.Empty;
}

public sealed class TransactionManifest
{
    public Guid TransactionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public TransactionState State { get; set; }
    public List<TransactionFileRecord> Files { get; set; } = [];
}

public enum TransactionFailPoint
{
    None,
    BeforePrepared,
    AfterPrepared,
    AfterFileReplaced,
    BeforeCommitted
}

public sealed class VaultImportTransaction
{
    public static readonly string[] VaultFiles = ["keys.dat", "passwords.dat", "cards.dat", "notes.dat"];
    public static readonly string[] DataFiles = ["passwords.dat", "cards.dat", "notes.dat"];
    private const int MaxArchiveEntries = 10;
    private const long MaxSingleFileSize = 50 * 1024 * 1024; // 50 MB

    internal static Action<Guid, TransactionFailPoint, string?>? TestingFailPointHook { get; set; }

    public Guid TransactionId { get; }
    public string TransactionDirectory { get; }
    public TransactionManifest Manifest { get; private set; }

    private readonly string _newDir;
    private readonly string _backupDir;
    private readonly string _manifestPath;

    public VaultImportTransaction(Guid? transactionId = null)
    {
        TransactionId = transactionId ?? Guid.NewGuid();
        string baseDir = FileWorker.GetAppDataDirectory();
        TransactionDirectory = Path.Combine(baseDir, $".vault-import-{TransactionId:N}");
        _newDir = Path.Combine(TransactionDirectory, "new");
        _backupDir = Path.Combine(TransactionDirectory, "backup");
        _manifestPath = Path.Combine(TransactionDirectory, "manifest.json");

        Manifest = new TransactionManifest
        {
            TransactionId = TransactionId,
            CreatedAt = DateTimeOffset.UtcNow,
            State = TransactionState.Prepared
        };
    }

    public static void ValidateVaultArchive(byte[] archiveBytes)
    {
        ArgumentNullException.ThrowIfNull(archiveBytes);

        if (archiveBytes.Length == 0)
            throw new InvalidDataException("Archive is empty.");

        using var memoryStream = new MemoryStream(archiveBytes);
        using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read);

        if (archive.Entries.Count == 0)
            throw new InvalidDataException("Archive contains no entries.");

        if (archive.Entries.Count > MaxArchiveEntries)
            throw new InvalidDataException("Archive contains too many entries.");

        bool foundKeysFile = false;
        bool foundDataFile = false;

        foreach (var entry in archive.Entries)
        {
            string entryName = entry.Name;
            string fullName = entry.FullName;

            // Reject Zip Slip / Path Traversal
            if (string.IsNullOrWhiteSpace(entryName) ||
                fullName.Contains("..") ||
                fullName.StartsWith('/') ||
                fullName.StartsWith('\\') ||
                fullName.Contains(':') ||
                fullName.Contains('/') ||
                fullName.Contains('\\'))
            {
                throw new InvalidDataException($"Zip Slip or illegal path detected in archive: {fullName}");
            }

            // Reject unexpected or sensitive system files
            if (!VaultFiles.Contains(entryName, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Unexpected file in sync archive: {entryName}");
            }

            if (entry.Length > MaxSingleFileSize)
            {
                throw new InvalidDataException($"Archive entry {entryName} exceeds maximum allowed file size.");
            }

            if (string.Equals(entryName, "keys.dat", StringComparison.OrdinalIgnoreCase))
            {
                if (entry.Length == 0)
                    throw new InvalidDataException("keys.dat in archive is 0 bytes.");

                foundKeysFile = true;
            }
            else if (DataFiles.Contains(entryName, StringComparer.OrdinalIgnoreCase) && entry.Length > 0)
            {
                foundDataFile = true;
            }
        }

        if (!foundKeysFile)
            throw new InvalidDataException("Archive does not contain keys.dat.");

        if (!foundDataFile)
            throw new InvalidDataException("Archive contains no valid data files.");
    }

    public void Prepare(byte[] archiveBytes)
    {
        ValidateVaultArchive(archiveBytes);

        TestingFailPointHook?.Invoke(TransactionId, TransactionFailPoint.BeforePrepared, null);

        Directory.CreateDirectory(TransactionDirectory);
        Directory.CreateDirectory(_newDir);
        Directory.CreateDirectory(_backupDir);

        using var memoryStream = new MemoryStream(archiveBytes);
        using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read);

        var fileRecords = new List<TransactionFileRecord>();

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) ||
                !VaultFiles.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            string fileName = entry.Name.ToLowerInvariant();
            string targetPath = Path.Combine(FileWorker.GetAppDataDirectory(), fileName);
            string newFilePath = Path.Combine(_newDir, fileName);
            string backupFilePath = Path.Combine(_backupDir, fileName);
            bool existedBefore = File.Exists(targetPath);

            // Extract new file
            using (var entryStream = entry.Open())
            using (var newFileStream = new FileStream(newFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                entryStream.CopyTo(newFileStream);
                newFileStream.Flush(flushToDisk: true);
            }

            // Backup existing file if present
            if (existedBefore)
            {
                byte[] existingBytes = File.ReadAllBytes(targetPath);
                using var backupStream = new FileStream(backupFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                backupStream.Write(existingBytes, 0, existingBytes.Length);
                backupStream.Flush(flushToDisk: true);
            }

            fileRecords.Add(new TransactionFileRecord
            {
                FileName = fileName,
                ExistedBefore = existedBefore,
                TargetPath = targetPath,
                RelativeNewPath = Path.Combine("new", fileName),
                RelativeBackupPath = Path.Combine("backup", fileName)
            });
        }

        Manifest.Files = fileRecords;
        Manifest.State = TransactionState.Prepared;

        SaveManifest();

        TestingFailPointHook?.Invoke(TransactionId, TransactionFailPoint.AfterPrepared, null);
    }

    public void Commit()
    {
        if (Manifest.State != TransactionState.Prepared)
            throw new InvalidOperationException($"Cannot commit transaction in state {Manifest.State}.");

        Manifest.State = TransactionState.Committing;
        SaveManifest();

        foreach (var record in Manifest.Files)
        {
            string newFilePath = Path.Combine(TransactionDirectory, record.RelativeNewPath);
            if (!File.Exists(newFilePath))
                throw new FileNotFoundException($"Missing staged file: {newFilePath}");

            byte[] newBytes = File.ReadAllBytes(newFilePath);
            FileWorker.WriteFileAtomically(newBytes, record.TargetPath);

            TestingFailPointHook?.Invoke(TransactionId, TransactionFailPoint.AfterFileReplaced, record.FileName);
        }

        TestingFailPointHook?.Invoke(TransactionId, TransactionFailPoint.BeforeCommitted, null);

        Manifest.State = TransactionState.Committed;
        SaveManifest();

        // Cleanup transaction directory
        CleanupTransactionDirectory();
    }

    public void Rollback()
    {
        RollbackInternal(Manifest, TransactionDirectory);
    }

    private static void RollbackInternal(TransactionManifest manifest, string transactionDirectory)
    {
        foreach (var record in manifest.Files)
        {
            if (record.ExistedBefore)
            {
                string backupFilePath = Path.Combine(transactionDirectory, record.RelativeBackupPath);
                if (File.Exists(backupFilePath))
                {
                    byte[] backupBytes = File.ReadAllBytes(backupFilePath);
                    FileWorker.WriteFileAtomically(backupBytes, record.TargetPath);
                }
            }
            else
            {
                // File did not exist before; remove target if it was created
                if (File.Exists(record.TargetPath))
                {
                    try
                    {
                        File.Delete(record.TargetPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        manifest.State = TransactionState.RolledBack;
        SaveManifestStatic(manifest, Path.Combine(transactionDirectory, "manifest.json"));

        TryDeleteDirectory(transactionDirectory);
    }

    private void SaveManifest()
    {
        SaveManifestStatic(Manifest, _manifestPath);
    }

    private static void SaveManifestStatic(TransactionManifest manifest, string manifestPath)
    {
        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        FileWorker.WriteFileAtomically(jsonBytes, manifestPath);
    }

    private void CleanupTransactionDirectory()
    {
        TryDeleteDirectory(TransactionDirectory);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    public static void RecoverPendingTransactions()
    {
        string baseDir = FileWorker.GetAppDataDirectory();
        if (!Directory.Exists(baseDir))
            return;

        var directories = Directory.GetDirectories(baseDir, ".vault-import-*");
        foreach (var dir in directories)
        {
            string manifestFile = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestFile))
            {
                TryDeleteDirectory(dir);
                continue;
            }

            try
            {
                byte[] manifestBytes = File.ReadAllBytes(manifestFile);
                var manifest = JsonSerializer.Deserialize<TransactionManifest>(manifestBytes);
                if (manifest == null)
                {
                    TryDeleteDirectory(dir);
                    continue;
                }

                switch (manifest.State)
                {
                    case TransactionState.Prepared:
                        // Working vault was never touched; clean staging artifacts
                        TryDeleteDirectory(dir);
                        break;

                    case TransactionState.Committing:
                        // Incomplete commit -> rollback to previous valid state
                        RollbackInternal(manifest, dir);
                        break;

                    case TransactionState.Committed:
                    case TransactionState.RolledBack:
                        // Fully done -> clean up
                        TryDeleteDirectory(dir);
                        break;
                }
            }
            catch
            {
                TryDeleteDirectory(dir);
            }
        }
    }
}
