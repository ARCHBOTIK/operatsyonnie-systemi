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

public interface IPendingVaultImport
{
    void Commit();
    void Rollback();
}

public interface IImportReceiverService
{
    string? GetLocalPeerAddress();
    bool LocalVaultExists();
    Task<IPendingVaultImport> ReceiveVaultForConfirmationAsync(PairingSecret pairingSecret, CancellationToken token = default);
}

public sealed class TcpBridge : IImportReceiverService
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

    public string? GetLocalPeerAddress() => _networkService.GetLocalIpAddress();

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

        if (!PairingSecret.TryNormalize(pairingCode, out string normalizedCode))
            return Error(SyncTransferMode.Upload, "Код сопряжения должен содержать 12 допустимых символов.");

        if (!QrPairingPayload.IsPrivateIpv4(host.Trim()))
            return Error(SyncTransferMode.Upload, "Укажите private IPv4-адрес устройства-получателя.");

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

        try
        {
            IPendingVaultImport pendingImport = await ReceiveVaultForConfirmationAsync(pairingSecret, token);
            pendingImport.Commit();
            return Success(SyncTransferMode.Download, "База успешно принята и установлена. Введите мастер-пароль повторно.");
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

    /// <summary>
    /// Receives and validates the encrypted SPP1 payload, but leaves its crash-safe
    /// transaction prepared until the user explicitly confirms the replacement.
    /// </summary>
    public async Task<IPendingVaultImport> ReceiveVaultForConfirmationAsync(
        PairingSecret pairingSecret,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(pairingSecret);
        VaultImportTransaction? transaction = null;
        byte[]? bundle = null;

        try
        {
            bundle = await _networkService.StartReceiverFlowAsync(pairingSecret, token);
            transaction = new VaultImportTransaction();
            transaction.Prepare(bundle);
            return new PendingVaultImport(transaction, _keyManager);
        }
        catch
        {
            transaction?.Rollback();
            throw;
        }
        finally
        {
            if (bundle is not null)
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(bundle);
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

    private sealed class PendingVaultImport : IPendingVaultImport
    {
        private readonly VaultImportTransaction _transaction;
        private readonly keyManager _keyManager;
        private bool _completed;

        public PendingVaultImport(VaultImportTransaction transaction, keyManager keyManager)
        {
            _transaction = transaction;
            _keyManager = keyManager;
        }

        public void Commit()
        {
            if (_completed)
                throw new InvalidOperationException("Import has already completed.");

            _transaction.Commit();
            _keyManager.ClearLoadedKey();
            _completed = true;
        }

        public void Rollback()
        {
            if (_completed)
                return;

            _transaction.Rollback();
            _completed = true;
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

    private static string GetVaultFilePath(string fileName)
    {
        return Path.Combine(FileWorker.GetAppDataDirectory(), fileName);
    }

    private static void WriteDiagnostic(string message)
    {
        Debug.WriteLine($"[P2P Sync] {message}");
    }
}
