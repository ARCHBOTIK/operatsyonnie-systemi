using System.Diagnostics;
using System.IO.Compression;

namespace SecurePassword;

public enum SyncTransferMode
{
    Upload,
    Download
}

public sealed class SyncOperationResult
{
    public bool Success { get; init; }
    public bool Cancelled { get; init; }
    public string Message { get; init; } = string.Empty;
    public SyncTransferMode Mode { get; init; }
}

public sealed class TcpBridge
{
    private static readonly string[] VaultFiles = ["keys.dat", "passwords.dat", "cards.dat", "notes.dat"];
    private static readonly string[] DataFiles = ["passwords.dat", "cards.dat", "notes.dat"];

    private readonly NetworkService _networkService;
    private readonly keyManager _keyManager;

    public TcpBridge(NetworkService networkService, keyManager keyManager)
    {
        _networkService = networkService;
        _keyManager = keyManager;
    }

    public SyncTransferMode GetPreferredMode()
    {
        return LocalVaultExists() ? SyncTransferMode.Upload : SyncTransferMode.Download;
    }

    public bool LocalVaultExists()
    {
        return VaultFiles.Any(file => File.Exists(GetVaultFilePath(file)));
    }

    public bool HasTransferableVault()
    {
        string keyPath = GetVaultFilePath("keys.dat");
        if (!File.Exists(keyPath))
            return false;

        return DataFiles
            .Select(GetVaultFilePath)
            .Any(path => File.Exists(path) && new FileInfo(path).Length > 0);
    }

    public IReadOnlyList<string> GetLocalPeerAddresses()
    {
        string? ipAddress = _networkService.GetLocalIpAddress();
        return string.IsNullOrWhiteSpace(ipAddress) ? [] : [ipAddress];
    }

    public string GetPeerAddressHint()
    {
        string? ipAddress = _networkService.GetLocalIpAddress();
        return string.IsNullOrWhiteSpace(ipAddress)
            ? "IP-адрес не найден. Подключитесь к Wi-Fi или локальной сети."
            : $"Адрес этого устройства: {ipAddress}";
    }

    public async Task<SyncOperationResult> SendVaultToPeerAsync(string host, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            return Error(SyncTransferMode.Upload, "Укажите IP-адрес устройства-получателя.");

        if (!HasTransferableVault())
            return Error(SyncTransferMode.Upload, "Локальная база пуста или ещё не создана.");

        try
        {
            byte[] bundle = CreateVaultBundle();
            await _networkService.SendFlow(host.Trim(), bundle, token);
            return Success(SyncTransferMode.Upload, "База успешно передана на другое устройство.");
        }
        catch (OperationCanceledException)
        {
            return Cancelled(SyncTransferMode.Upload);
        }
        catch (TimeoutException exception)
        {
            return Error(SyncTransferMode.Upload, exception.Message);
        }
        catch (Exception exception)
        {
            return Error(SyncTransferMode.Upload, $"Не удалось передать базу: {exception.Message}");
        }
    }

    public async Task<SyncOperationResult> ReceiveVaultFromPeerAsync(CancellationToken token = default)
    {
        try
        {
            byte[] bundle = await _networkService.ReceiveFlow(token);
            ExtractVaultBundle(bundle);
            _keyManager.ClearLoadedKey();
            return Success(SyncTransferMode.Download, "База успешно принята с другого устройства. Введите мастер-пароль повторно.");
        }
        catch (OperationCanceledException)
        {
            return Cancelled(SyncTransferMode.Download);
        }
        catch (Exception exception)
        {
            return Error(SyncTransferMode.Download, $"Не удалось принять базу: {exception.Message}");
        }
    }

    private static SyncOperationResult Success(SyncTransferMode mode, string message)
    {
        return new SyncOperationResult
        {
            Success = true,
            Mode = mode,
            Message = message
        };
    }

    private static SyncOperationResult Cancelled(SyncTransferMode mode)
    {
        return new SyncOperationResult
        {
            Cancelled = true,
            Mode = mode,
            Message = "Операция отменена."
        };
    }

    private static SyncOperationResult Error(SyncTransferMode mode, string message)
    {
        return new SyncOperationResult
        {
            Success = false,
            Mode = mode,
            Message = message
        };
    }

    private byte[] CreateVaultBundle()
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string fileName in VaultFiles)
            {
                string fullPath = GetVaultFilePath(fileName);
                if (!File.Exists(fullPath))
                {
                    WriteDiagnostic($"Skip missing file in sync bundle: {fileName}");
                    continue;
                }

                var entry = archive.CreateEntry(fileName, CompressionLevel.Fastest);
                using var entryStream = entry.Open();

                if (string.Equals(fileName, "keys.dat", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] keyFileBytes = _keyManager.ExportKeyFileForTransfer();
                    entryStream.Write(keyFileBytes);
                    WriteDiagnostic($"Added keys.dat to sync bundle, size={keyFileBytes.Length} bytes.");
                    continue;
                }

                using var fileStream = File.OpenRead(fullPath);
                fileStream.CopyTo(entryStream);
                WriteDiagnostic($"Added {fileName} to sync bundle, size={fileStream.Length} bytes.");
            }
        }

        WriteDiagnostic($"Created sync bundle, size={memoryStream.Length} bytes.");
        return memoryStream.ToArray();
    }

    private static void ExtractVaultBundle(byte[] bundle)
    {
        using var memoryStream = new MemoryStream(bundle);
        using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read);

        if (archive.Entries.Count == 0)
            throw new InvalidOperationException("Получена пустая база данных.");

        ValidateVaultBundle(archive);

        string tempDirectory = Path.Combine(FileSystem.CacheDirectory, $"vault-import-{Guid.NewGuid():N}");
        string backupDirectory = Path.Combine(FileSystem.CacheDirectory, $"vault-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        Directory.CreateDirectory(backupDirectory);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            if (!VaultFiles.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            string tempPath = Path.Combine(tempDirectory, entry.Name);
            using var entryStream = entry.Open();
            using var fileStream = File.Create(tempPath);
            entryStream.CopyTo(fileStream);
            WriteDiagnostic($"Extracted {entry.Name} to temp, size={fileStream.Length} bytes.");
        }

        try
        {
            BackupLocalVaultFiles(backupDirectory);
            RestoreImportedVaultFiles(tempDirectory);

            string keyPath = GetVaultFilePath("keys.dat");
            WriteDiagnostic($"Import finished. keys.dat exists={File.Exists(keyPath)}, size={GetFileSize(keyPath)} bytes.");
        }
        catch
        {
            RestoreBackup(backupDirectory);
            throw;
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
            TryDeleteDirectory(backupDirectory);
        }
    }

    private static void ValidateVaultBundle(ZipArchive archive)
    {
        var allowedEntries = archive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .Where(entry => VaultFiles.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        WriteDiagnostic("Received sync bundle entries: " +
            string.Join(", ", allowedEntries.Select(entry => $"{entry.Name}={entry.Length} bytes")));

        var keyEntry = allowedEntries.FirstOrDefault(entry =>
            string.Equals(entry.Name, "keys.dat", StringComparison.OrdinalIgnoreCase));
        if (keyEntry is null || keyEntry.Length == 0)
            throw new InvalidOperationException("Полученная база не содержит keys.dat.");

        bool hasDataFile = allowedEntries.Any(entry =>
            DataFiles.Contains(entry.Name, StringComparer.OrdinalIgnoreCase) &&
            entry.Length > 0);

        if (!hasDataFile)
            throw new InvalidOperationException("Получена пустая база без файлов данных.");
    }

    private static void BackupLocalVaultFiles(string backupDirectory)
    {
        foreach (string fileName in VaultFiles)
        {
            string destinationPath = GetVaultFilePath(fileName);
            if (!File.Exists(destinationPath))
                continue;

            string backupPath = Path.Combine(backupDirectory, fileName);
            File.Move(destinationPath, backupPath, overwrite: true);
            WriteDiagnostic($"Backed up local {fileName}, size={GetFileSize(backupPath)} bytes.");
        }
    }

    private static void RestoreImportedVaultFiles(string tempDirectory)
    {
        foreach (string fileName in VaultFiles)
        {
            string tempPath = Path.Combine(tempDirectory, fileName);
            if (!File.Exists(tempPath))
                continue;

            string destinationPath = GetVaultFilePath(fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Move(tempPath, destinationPath, overwrite: true);
            WriteDiagnostic($"Restored imported {fileName}, size={GetFileSize(destinationPath)} bytes.");
        }
    }

    private static void RestoreBackup(string backupDirectory)
    {
        foreach (string fileName in VaultFiles)
        {
            string backupPath = Path.Combine(backupDirectory, fileName);
            if (!File.Exists(backupPath))
                continue;

            string destinationPath = GetVaultFilePath(fileName);
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);

            File.Move(backupPath, destinationPath, overwrite: true);
            WriteDiagnostic($"Rolled back {fileName} from backup.");
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception)
        {
            WriteDiagnostic($"Failed to delete temporary sync directory {directory}: {exception.Message}");
        }
    }

    private static long GetFileSize(string path)
    {
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    private static string GetVaultFilePath(string fileName)
    {
        return Path.Combine(FileSystem.AppDataDirectory, fileName);
    }

    private static void WriteDiagnostic(string message)
    {
        Debug.WriteLine($"[P2P Sync] {message}");
    }
}
