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
    public static readonly string[] VaultFiles = VaultImportTransaction.VaultFiles;
    public static readonly string[] DataFiles = VaultImportTransaction.DataFiles;

    private readonly NetworkService _networkService;
    private readonly keyManager _keyManager;

    public TcpBridge(NetworkService networkService, keyManager keyManager)
    {
        _networkService = networkService;
        _keyManager = keyManager;
    }

    /// <summary>
    /// Proxies status messages from the underlying <see cref="NetworkService"/> for UI progress updates.
    /// This keeps NetworkService internal implementation details encapsulated.
    /// </summary>
    public event Action<string>? StatusChanged
    {
        add => _networkService.StatusChanged += value;
        remove => _networkService.StatusChanged -= value;
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

    public async Task<SyncOperationResult> SendVaultToPeerAsync(
        string host,
        string pairingCode,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            return Error(SyncTransferMode.Upload, "Укажите IP-адрес устройства-получателя.");

        string normalizedCode = PairingSecret.Normalize(pairingCode);
        if (string.IsNullOrWhiteSpace(normalizedCode))
            return Error(SyncTransferMode.Upload, "Укажите одноразовый код сопряжения.");

        if (!HasTransferableVault())
            return Error(SyncTransferMode.Upload, "Локальная база пуста или ещё не создана.");

        try
        {
            byte[] bundle = CreateVaultBundle();
            await _networkService.SendVaultFlowAsync(host.Trim(), normalizedCode, bundle, token);
            return Success(SyncTransferMode.Upload, "База успешно зашифрована и передана на устройство.");
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

    public async Task<SyncOperationResult> ReceiveVaultFromPeerAsync(
        PairingSecret pairingSecret,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(pairingSecret);

        VaultImportTransaction? transaction = null;

        try
        {
            byte[] bundle = await _networkService.StartReceiverFlowAsync(pairingSecret, token);

            transaction = new VaultImportTransaction();
            transaction.Prepare(bundle);
            transaction.Commit();

            _keyManager.ClearLoadedKey();
            return Success(SyncTransferMode.Download, "База успешно принята и установлена. Введите мастер-пароль повторно.");
        }
        catch (OperationCanceledException)
        {
            transaction?.Rollback();
            return Cancelled(SyncTransferMode.Download);
        }
        catch (Exception exception)
        {
            transaction?.Rollback();
            return Error(SyncTransferMode.Download, $"Не удалось принять базу: {exception.Message}");
        }
    }

    public byte[] CreateVaultBundle()
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
                using var fileStream = File.OpenRead(fullPath);
                fileStream.CopyTo(entryStream);
                WriteDiagnostic($"Added {fileName} to sync bundle, size={fileStream.Length} bytes.");
            }
        }

        WriteDiagnostic($"Created sync bundle, size={memoryStream.Length} bytes.");
        return memoryStream.ToArray();
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

    private static string GetVaultFilePath(string fileName)
    {
        return Path.Combine(FileWorker.GetAppDataDirectory(), fileName);
    }

    private static void WriteDiagnostic(string message)
    {
        Debug.WriteLine($"[P2P Sync] {message}");
    }
}
