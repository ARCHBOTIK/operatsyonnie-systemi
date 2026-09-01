using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SecurePassword.ViewModels.Base;

namespace SecurePassword.ViewModels.Import;

/// <summary>UI-only states for the receiver import flow; SPP1 itself is unchanged.</summary>
public enum ImportUiState
{
    Idle,
    WaitingForSender,
    AwaitingConfirmation,
    Completed,
    Cancelled,
    Failed
}

/// <summary>
/// Owns a single short-lived receiver pairing session. The QR carries only connection
/// bootstrap data; authentication, decryption and transactional storage remain in SPP1/TcpBridge.
/// </summary>
public sealed class ImportViewModel : BaseViewModel, ISensitiveViewModel
{
    private readonly IImportReceiverService _receiverService;
    private readonly VaultSessionService _vaultSession;
    private readonly Func<PairingSecret> _pairingSecretFactory;
    private readonly ILogger<ImportViewModel>? _logger;
    private ReceiverPairingBootstrap? _receiverBootstrap;
    private CancellationTokenSource? _receiverCts;
    private IPendingVaultImport? _pendingImport;
    private int _generation;
    private bool _disposed;

    private ImportUiState _uiState = ImportUiState.Idle;
    private string _qrPayload = string.Empty;
    private string _pairingCode = string.Empty;
    private string _receiverAddress = string.Empty;
    private string _expiresAtText = string.Empty;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _hasLocalVault;

    public Action? RequestLockAction { get; set; }

    public ImportViewModel(
        IImportReceiverService receiverService,
        VaultSessionService vaultSession,
        Func<PairingSecret>? pairingSecretFactory = null,
        ILogger<ImportViewModel>? logger = null)
    {
        _receiverService = receiverService ?? throw new ArgumentNullException(nameof(receiverService));
        _vaultSession = vaultSession ?? throw new ArgumentNullException(nameof(vaultSession));
        _pairingSecretFactory = pairingSecretFactory ?? (() => PairingSecret.Generate());
        _logger = logger;
        _vaultSession.StateChanged += OnSessionStateChanged;

        StartReceiverCommand = new AsyncRelayCommand(StartReceiverAsync, () => CanStartReceiver);
        CreateNewQrCommand = new AsyncRelayCommand(StartReceiverAsync, () => CanCreateNewQr);
        CancelReceiverCommand = new RelayCommand(CancelReceiver, () => CanCancelReceiver);
        ConfirmImportCommand = new RelayCommand(ConfirmImport, () => CanConfirmImport);
        RejectImportCommand = new RelayCommand(RejectImport, () => CanConfirmImport);

        HasLocalVault = _receiverService.LocalVaultExists();
    }

    public ICommand StartReceiverCommand { get; }
    public ICommand CreateNewQrCommand { get; }
    public ICommand CancelReceiverCommand { get; }
    public ICommand ConfirmImportCommand { get; }
    public ICommand RejectImportCommand { get; }

    public ImportUiState UiState
    {
        get => _uiState;
        private set
        {
            if (SetProperty(ref _uiState, value))
            {
                OnPropertyChanged(nameof(IsWaitingForSender));
                OnPropertyChanged(nameof(IsAwaitingConfirmation));
                OnPropertyChanged(nameof(HasActiveQr));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool IsWaitingForSender => UiState == ImportUiState.WaitingForSender;
    public bool IsAwaitingConfirmation => UiState == ImportUiState.AwaitingConfirmation;
    public bool HasActiveQr => !string.IsNullOrEmpty(QrPayload) && IsWaitingForSender;
    public bool CanStartReceiver => !_disposed && !IsWaitingForSender && !IsAwaitingConfirmation && _vaultSession.IsAuthenticated;
    public bool CanCreateNewQr => !_disposed && !IsAwaitingConfirmation && _vaultSession.IsAuthenticated;
    public bool CanCancelReceiver => IsWaitingForSender || IsAwaitingConfirmation;
    public bool CanConfirmImport => IsAwaitingConfirmation && _pendingImport is not null && _vaultSession.IsAuthenticated;

    /// <summary>Raw bootstrap URI used only by the on-device QR generator; never persisted or logged.</summary>
    public string QrPayload
    {
        get => _qrPayload;
        private set
        {
            if (SetProperty(ref _qrPayload, value))
                OnPropertyChanged(nameof(HasActiveQr));
        }
    }

    public string PairingCode
    {
        get => _pairingCode;
        private set => SetProperty(ref _pairingCode, value);
    }

    public string ReceiverAddress
    {
        get => _receiverAddress;
        private set => SetProperty(ref _receiverAddress, value);
    }

    public string ExpiresAtText
    {
        get => _expiresAtText;
        private set => SetProperty(ref _expiresAtText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasLocalVault
    {
        get => _hasLocalVault;
        private set => SetProperty(ref _hasLocalVault, value);
    }

    public Task StartReceiverAsync()
    {
        if (_disposed || IsAwaitingConfirmation || !_vaultSession.IsAuthenticated)
            return Task.CompletedTask;

        _vaultSession.RecordActivity();
        InvalidateActiveSession(rollbackPending: true);
        ErrorMessage = string.Empty;
        HasLocalVault = _receiverService.LocalVaultExists();

        string? localAddress = _receiverService.GetLocalPeerAddress();
        if (string.IsNullOrWhiteSpace(localAddress))
        {
            UiState = ImportUiState.Failed;
            ErrorMessage = "Не удалось определить локальный IP-адрес. Подключитесь к Wi-Fi или локальной сети.";
            return Task.CompletedTask;
        }

        ReceiverPairingBootstrap bootstrap;
        try
        {
            bootstrap = ReceiverPairingBootstrap.Create(localAddress, _pairingSecretFactory);
        }
        catch (Exception exception)
        {
            UiState = ImportUiState.Failed;
            ErrorMessage = exception.Message;
            return Task.CompletedTask;
        }

        _receiverBootstrap = bootstrap;
        _receiverCts = new CancellationTokenSource();
        _receiverCts.CancelAfter(bootstrap.ExpiresAt - DateTimeOffset.UtcNow);

        PairingCode = bootstrap.PairingCode;
        ReceiverAddress = bootstrap.ReceiverAddress;
        ExpiresAtText = bootstrap.ExpiresAt.LocalDateTime.ToString("t");
        QrPayload = bootstrap.QrPayload;
        StatusMessage = "Ожидание подключения с другого устройства…";
        UiState = ImportUiState.WaitingForSender;

        int generation = ++_generation;
        _ = ReceiveAndPrepareAsync(bootstrap.PairingSecret, generation, _receiverCts.Token);
        return Task.CompletedTask;
    }

    private async Task ReceiveAndPrepareAsync(PairingSecret pairingSecret, int generation, CancellationToken token)
    {
        try
        {
            IPendingVaultImport pendingImport = await _receiverService
                .ReceiveVaultForConfirmationAsync(pairingSecret, token);

            if (!IsCurrent(generation) || token.IsCancellationRequested || !_vaultSession.IsAuthenticated)
            {
                TryRollback(pendingImport);
                return;
            }

            _pendingImport = pendingImport;
            ClearQrDisplayAfterTransfer();
            StatusMessage = "Данные проверены. Подтвердите замену локального хранилища.";
            UiState = ImportUiState.AwaitingConfirmation;
        }
        catch (OperationCanceledException)
        {
            if (!IsCurrent(generation))
                return;

            InvalidateActiveSession(rollbackPending: false);
            UiState = ImportUiState.Cancelled;
            StatusMessage = "Сеанс сопряжения отменён или срок действия QR-кода истёк.";
        }
        catch (Exception)
        {
            if (!IsCurrent(generation))
                return;

            // Authentication failures and every other terminal receive error invalidate this QR/session.
            InvalidateActiveSession(rollbackPending: false);
            UiState = ImportUiState.Failed;
            ErrorMessage = "Не удалось аутентифицировать или получить данные. Создайте новый QR-код и повторите попытку.";
        }
    }

    public void CancelReceiver()
    {
        if (!CanCancelReceiver)
            return;

        InvalidateActiveSession(rollbackPending: true);
        UiState = ImportUiState.Cancelled;
        StatusMessage = "Импорт отменён. Предыдущий QR-код больше не действует.";
        ErrorMessage = string.Empty;
    }

    public void ConfirmImport()
    {
        if (!CanConfirmImport || _pendingImport is null)
            return;

        try
        {
            _pendingImport.Commit();
            _pendingImport = null;
            UiState = ImportUiState.Completed;
            StatusMessage = "Хранилище импортировано. Требуется повторный вход.";
            _vaultSession.Lock();
            RequestLockAction?.Invoke();
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Failed to commit received vault import.");
            TryRollbackPendingImport();
            UiState = ImportUiState.Failed;
            ErrorMessage = "Не удалось завершить импорт. Исходное хранилище не было заменено.";
        }
        finally
        {
            ClearQrDisplayAfterTransfer();
            RaiseCommandCanExecuteChanged();
        }
    }

    public void RejectImport()
    {
        if (!CanConfirmImport)
            return;

        if (!TryRollbackPendingImport())
        {
            UiState = ImportUiState.Failed;
            ErrorMessage = "Не удалось отменить импорт. Перезапустите приложение для безопасного восстановления.";
            RaiseCommandCanExecuteChanged();
            return;
        }

        ClearQrDisplayAfterTransfer();
        UiState = ImportUiState.Cancelled;
        StatusMessage = "Импорт отклонён. Локальное хранилище не изменено.";
        RaiseCommandCanExecuteChanged();
    }

    public void ClearSensitiveData() => InvalidateActiveSession(rollbackPending: true);

    private bool IsCurrent(int generation) => !_disposed && generation == _generation;

    private void InvalidateActiveSession(bool rollbackPending)
    {
        ++_generation;
        try { _receiverCts?.Cancel(); } catch { }
        _receiverCts?.Dispose();
        _receiverCts = null;
        _receiverBootstrap?.Dispose();
        _receiverBootstrap = null;

        if (rollbackPending)
        {
            TryRollbackPendingImport();
        }

        QrPayload = string.Empty;
        PairingCode = string.Empty;
        ReceiverAddress = string.Empty;
        ExpiresAtText = string.Empty;
        RaiseCommandCanExecuteChanged();
    }

    private bool TryRollbackPendingImport()
    {
        IPendingVaultImport? pendingImport = _pendingImport;
        _pendingImport = null;
        return pendingImport is null || TryRollback(pendingImport);
    }

    private bool TryRollback(IPendingVaultImport pendingImport)
    {
        try
        {
            pendingImport.Rollback();
            return true;
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Failed to roll back received vault import.");
            return false;
        }
    }

    private void ClearQrDisplayAfterTransfer()
    {
        _receiverBootstrap?.Dispose();
        _receiverBootstrap = null;
        _receiverCts?.Dispose();
        _receiverCts = null;
        QrPayload = string.Empty;
        PairingCode = string.Empty;
        ReceiverAddress = string.Empty;
        ExpiresAtText = string.Empty;
    }

    private void OnSessionStateChanged()
    {
        try
        {
            if (MainThread.IsMainThread)
                HandleSessionStateChanged();
            else
                MainThread.BeginInvokeOnMainThread(HandleSessionStateChanged);
        }
        catch
        {
            HandleSessionStateChanged();
        }
    }

    private void HandleSessionStateChanged()
    {
        if (_vaultSession.IsAuthenticated)
            return;

        InvalidateActiveSession(rollbackPending: true);
        UiState = ImportUiState.Idle;
        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;
    }

    private void RaiseCommandCanExecuteChanged()
    {
        ((AsyncRelayCommand)StartReceiverCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)CreateNewQrCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CancelReceiverCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ConfirmImportCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RejectImportCommand).RaiseCanExecuteChanged();
    }

    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _vaultSession.StateChanged -= OnSessionStateChanged;
        InvalidateActiveSession(rollbackPending: true);
        base.Dispose();
    }
}
