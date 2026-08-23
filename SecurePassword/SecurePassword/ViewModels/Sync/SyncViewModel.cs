using System.Windows.Input;
using SecurePassword.ViewModels.Base;

namespace SecurePassword.ViewModels.Sync;

/// <summary>
/// Represents the discrete UI states of the P2P synchronisation flow.
/// These are UI-only representations — they do NOT add new protocol states to SPP1.
/// </summary>
public enum SyncUiState
{
    Idle,
    Preparing,
    WaitingForPeer,
    Authenticating,
    Transferring,
    Completed,
    Cancelled,
    Failed
}

/// <summary>
/// ViewModel for the native XAML P2P Sync screen.
/// Orchestrates UI state, command gating, cancellation and session lifecycle.
/// Does NOT implement TCP protocol, cryptography, serialisation or vault I/O.
/// All network and crypto work is delegated to <see cref="TcpBridge"/>.
/// </summary>
public sealed class SyncViewModel : BaseViewModel, ISensitiveViewModel
{
    private readonly TcpBridge _syncBridge;
    private readonly VaultSessionService _vaultSession;

    private SyncTransferMode _selectedMode;
    private SyncUiState _uiState = SyncUiState.Idle;

    private string _peerAddress = string.Empty;
    private string _peerPairingCode = string.Empty;
    private string _receiverPairingCode = string.Empty;
    private string _addressHint = string.Empty;
    private string _validationError = string.Empty;
    private string _statusMessage = string.Empty;
    private string _resultMessage = string.Empty;
    private bool _resultIsSuccess;
    private bool _resultIsCancelled;
    private bool _hasTransferableVault;
    private bool _hasLocalVault;
    private double _progressValue;
    private bool _showManualEntry = !OperatingSystem.IsAndroid();
    private bool _isScannerVisible;

    // Sensitive: PairingSecret holds zeroed secret bytes on Dispose
    private PairingSecret? _receiverPairingSecret;

    // Operation generation token: guards against late callbacks from cancelled operations
    private int _currentOperationGeneration;
    private CancellationTokenSource? _operationCts;
    private bool _disposed;

    public Action? RequestLockAction { get; set; }

    public SyncViewModel(TcpBridge syncBridge, VaultSessionService vaultSession)
    {
        _syncBridge = syncBridge ?? throw new ArgumentNullException(nameof(syncBridge));
        _vaultSession = vaultSession ?? throw new ArgumentNullException(nameof(vaultSession));

        _vaultSession.StateChanged += OnSessionStateChanged;
        _syncBridge.StatusChanged += OnNetworkStatusChanged;


        SelectUploadModeCommand = new RelayCommand(() => SelectMode(SyncTransferMode.Upload));
        SelectDownloadModeCommand = new RelayCommand(() => SelectMode(SyncTransferMode.Download));
        StartSyncCommand = new AsyncRelayCommand(StartSyncAsync, () => CanStart);
        StartSendCommand = new AsyncRelayCommand(StartSendAsync, () => CanStart);
        CancelSyncCommand = new RelayCommand(CancelCurrentOperation, () => CanCancel);
        ToggleManualEntryCommand = new RelayCommand(() =>
        {
            ShowManualEntry = !ShowManualEntry;
            if (ShowManualEntry)
                SelectMode(SyncTransferMode.Upload);
        });

        InitialiseState();
    }

    // ─── Commands ──────────────────────────────────────────────────────────────
    public ICommand SelectUploadModeCommand { get; }
    public ICommand SelectDownloadModeCommand { get; }
    public ICommand StartSyncCommand { get; }
    public ICommand StartSendCommand { get; }
    public ICommand CancelSyncCommand { get; }
    public ICommand ToggleManualEntryCommand { get; }

    // ─── Mode ─────────────────────────────────────────────────────────────────
    public SyncTransferMode SelectedMode
    {
        get => _selectedMode;
        private set
        {
            if (SetProperty(ref _selectedMode, value))
            {
                OnPropertyChanged(nameof(IsUploadMode));
                OnPropertyChanged(nameof(IsDownloadMode));
                RaiseCommandsCanExecuteChanged();
            }
        }
    }
    public bool IsUploadMode => _selectedMode == SyncTransferMode.Upload;
    public bool IsDownloadMode => _selectedMode == SyncTransferMode.Download;

    // ─── UI State machine ──────────────────────────────────────────────────────
    public SyncUiState UiState
    {
        get => _uiState;
        private set
        {
            if (SetProperty(ref _uiState, value))
            {
                OnPropertyChanged(nameof(IsOperationActive));
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(IsFailed));
                OnPropertyChanged(nameof(IsCancelled));
                OnPropertyChanged(nameof(ShowProgress));
                OnPropertyChanged(nameof(ShowResultBanner));
                RaiseCommandsCanExecuteChanged();
            }
        }
    }
    public bool IsOperationActive => _uiState is
        SyncUiState.Preparing or SyncUiState.WaitingForPeer or
        SyncUiState.Authenticating or SyncUiState.Transferring;
    public bool IsIdle => _uiState == SyncUiState.Idle;
    public bool IsCompleted => _uiState == SyncUiState.Completed;
    public bool IsFailed => _uiState == SyncUiState.Failed;
    public bool IsCancelled => _uiState == SyncUiState.Cancelled;
    public bool ShowProgress => IsOperationActive;
    public bool ShowResultBanner => _uiState is SyncUiState.Completed or SyncUiState.Failed or SyncUiState.Cancelled;

    // ─── Command gating ────────────────────────────────────────────────────────
    public bool CanStart => !IsOperationActive && _vaultSession.IsAuthenticated;
    public bool CanCancel => IsOperationActive;

    // ─── Sender inputs ─────────────────────────────────────────────────────────
    public string PeerAddress
    {
        get => _peerAddress;
        set => SetProperty(ref _peerAddress, value);
    }
    public string PeerPairingCode
    {
        get => _peerPairingCode;
        set => SetProperty(ref _peerPairingCode, value);
    }

    /// <summary>Windows and denied-camera users always retain this manual fallback.</summary>
    public bool ShowManualEntry
    {
        get => _showManualEntry;
        set => SetProperty(ref _showManualEntry, value);
    }

    public bool IsScannerVisible
    {
        get => _isScannerVisible;
        private set => SetProperty(ref _isScannerVisible, value);
    }

    public bool CanScanQr => OperatingSystem.IsAndroid();

    public void BeginQrScan()
    {
        if (!CanScanQr || IsOperationActive)
            return;

        ValidationError = string.Empty;
        IsScannerVisible = true;
    }

    /// <summary>
    /// Accepts one scanner result only. Raw QR text is never retained after this method returns.
    /// </summary>
    public void ApplyScannedQr(string? rawPayload)
    {
        IsScannerVisible = false;
        if (!QrPairingPayload.TryParse(rawPayload, out QrPairingPayload? payload, out string error))
        {
            ValidationError = error;
            ShowManualEntry = true;
            return;
        }

        SelectMode(SyncTransferMode.Upload);
        PeerAddress = payload!.Host;
        PeerPairingCode = payload.PairingCode;
        ValidationError = string.Empty;
        ShowManualEntry = true;
    }

    public void CameraPermissionDenied()
    {
        IsScannerVisible = false;
        ShowManualEntry = true;
        ValidationError = "Камера недоступна. Введите IP-адрес и код сопряжения вручную.";
    }

    // ─── Receiver display ──────────────────────────────────────────────────────
    /// <summary>Formatted pairing code shown to user (XXXX-XXXX-XXXX). Not raw secret bytes.</summary>
    public string ReceiverPairingCode
    {
        get => _receiverPairingCode;
        private set => SetProperty(ref _receiverPairingCode, value);
    }
    public string AddressHint
    {
        get => _addressHint;
        private set => SetProperty(ref _addressHint, value);
    }

    // ─── Feedback ─────────────────────────────────────────────────────────────
    public string ValidationError
    {
        get => _validationError;
        private set
        {
            if (SetProperty(ref _validationError, value))
                OnPropertyChanged(nameof(HasValidationError));
        }
    }
    public bool HasValidationError => !string.IsNullOrWhiteSpace(_validationError);
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }
    public string ResultMessage
    {
        get => _resultMessage;
        private set => SetProperty(ref _resultMessage, value);
    }
    public bool ResultIsSuccess
    {
        get => _resultIsSuccess;
        private set => SetProperty(ref _resultIsSuccess, value);
    }
    public bool ResultIsCancelled
    {
        get => _resultIsCancelled;
        private set => SetProperty(ref _resultIsCancelled, value);
    }
    public bool HasTransferableVault
    {
        get => _hasTransferableVault;
        private set => SetProperty(ref _hasTransferableVault, value);
    }
    public bool HasLocalVault
    {
        get => _hasLocalVault;
        private set => SetProperty(ref _hasLocalVault, value);
    }
    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, Math.Clamp(value, 0, 1));
    }

    // ─── Mode selection ────────────────────────────────────────────────────────
    private void SelectMode(SyncTransferMode mode)
    {
        if (IsOperationActive) return;

        _vaultSession.RecordActivity();
        SelectedMode = mode;
        ValidationError = string.Empty;
        ResultMessage = string.Empty;
        UiState = SyncUiState.Idle;
        RefreshVaultState();
    }

    // ─── Initialisation ────────────────────────────────────────────────────────
    private void InitialiseState()
    {
        SelectedMode = _syncBridge.GetPreferredMode();
        RefreshVaultState();
    }

    private void RefreshVaultState()
    {
        HasTransferableVault = _syncBridge.HasTransferableVault();
        HasLocalVault = _syncBridge.LocalVaultExists();
        AddressHint = _syncBridge.GetPeerAddressHint();

        if (SelectedMode == SyncTransferMode.Download)
        {
            if (_receiverPairingSecret is null || _receiverPairingSecret.IsExpired)
            {
                _receiverPairingSecret?.Dispose();
                _receiverPairingSecret = PairingSecret.Generate();
            }
            ReceiverPairingCode = _receiverPairingSecret.FormattedCode;
        }
        else
        {
            ReceiverPairingCode = string.Empty;
        }
    }

    // ─── Start sync ────────────────────────────────────────────────────────────
    public async Task StartSyncAsync()
    {
        if (IsOperationActive) return;

        _vaultSession.RecordActivity();
        if (!_vaultSession.IsAuthenticated)
        {
            ValidationError = "Сессия завершена. Выполните вход повторно.";
            return;
        }

        ValidationError = string.Empty;
        ResultMessage = string.Empty;
        ResultIsSuccess = false;
        ResultIsCancelled = false;

        if (!ValidateInputs()) return;

        if (SelectedMode == SyncTransferMode.Download)
        {
            if (_receiverPairingSecret is null || _receiverPairingSecret.IsExpired)
            {
                _receiverPairingSecret?.Dispose();
                _receiverPairingSecret = PairingSecret.Generate();
                ReceiverPairingCode = _receiverPairingSecret.FormattedCode;
            }
        }

        int myGeneration = System.Threading.Interlocked.Increment(ref _currentOperationGeneration);

        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        var token = _operationCts.Token;

        SetState(SyncUiState.Preparing, "Подготовка...", 0.05);

        SyncOperationResult result;
        try
        {
            if (SelectedMode == SyncTransferMode.Upload)
            {
                SetState(SyncUiState.WaitingForPeer, "Выполняем безопасное подключение...", 0.2);
                result = await _syncBridge.SendVaultToPeerAsync(PeerAddress.Trim(), PeerPairingCode.Trim(), token);
            }
            else
            {
                SetState(SyncUiState.WaitingForPeer, "Ожидаем подключение и проверку кода сопряжения...", 0.2);
                result = await _syncBridge.ReceiveVaultFromPeerAsync(_receiverPairingSecret!, token);
            }
        }
        catch (OperationCanceledException)
        {
            result = new SyncOperationResult { Cancelled = true, Mode = SelectedMode, Message = "Операция отменена пользователем." };
        }
        catch (Exception ex)
        {
            result = new SyncOperationResult { Success = false, Mode = SelectedMode, Message = $"Ошибка: {ex.Message}" };
        }
        finally
        {
            _operationCts?.Dispose();
            _operationCts = null;
        }

        // Late callback guard
        if (myGeneration != _currentOperationGeneration) return;

        // Session lock guard
        if (!_vaultSession.IsAuthenticated)
        {
            ClearSensitiveData();
            SetState(SyncUiState.Idle, string.Empty, 0);
            return;
        }

        ApplyResult(result);
    }

    /// <summary>
    /// The dedicated SyncPage is sender-only in Stage 8B. Keeping StartSyncAsync
    /// preserves the legacy receiver orchestration used by existing callers/tests.
    /// </summary>
    public async Task StartSendAsync()
    {
        if (SelectedMode != SyncTransferMode.Upload)
            SelectMode(SyncTransferMode.Upload);

        await StartSyncAsync();
    }

    private bool ValidateInputs()
    {
        if (SelectedMode == SyncTransferMode.Upload)
        {
            if (!HasTransferableVault) { ValidationError = "На этом устройстве нет базы для передачи."; return false; }
            if (string.IsNullOrWhiteSpace(PeerAddress)) { ValidationError = "Введите IP-адрес устройства-получателя."; return false; }
            if (string.IsNullOrWhiteSpace(PeerPairingCode)) { ValidationError = "Введите одноразовый код сопряжения с экрана получателя."; return false; }
        }
        return true;
    }

    private void ApplyResult(SyncOperationResult result)
    {
        ResultIsSuccess = result.Success;
        ResultIsCancelled = result.Cancelled;
        ResultMessage = result.Message;

        if (result.Success)
        {
            SetState(SyncUiState.Completed, result.Message, 1.0);
            if (result.Mode == SyncTransferMode.Download)
            {
                ClearReceiverPairingSecret();
                // Lock session and navigate to locked root
                _vaultSession.Lock();
                RequestLockAction?.Invoke();
            }
        }
        else if (result.Cancelled)
        {
            SetState(SyncUiState.Cancelled, result.Message, 0);
            ClearReceiverPairingSecret();
        }
        else
        {
            SetState(SyncUiState.Failed, result.Message, 0);
            ClearReceiverPairingSecret();
        }

        RefreshVaultState();
    }

    private void SetState(SyncUiState state, string statusMessage, double progress)
    {
        UiState = state;
        StatusMessage = statusMessage;
        ProgressValue = progress;
    }

    // ─── Cancellation ──────────────────────────────────────────────────────────
    public void CancelCurrentOperation()
    {
        if (!CanCancel) return;
        _operationCts?.Cancel();
        ResultIsCancelled = true;
        ResultMessage = "Операция отменена пользователем.";
        SetState(SyncUiState.Cancelled, "Операция отменена.", 0);
        ClearReceiverPairingSecret();
    }

    // ─── Sensitive data ────────────────────────────────────────────────────────
    public void ClearSensitiveData()
    {
        IsScannerVisible = false;
        PeerPairingCode = string.Empty;
        ClearReceiverPairingSecret();
        ValidationError = string.Empty;
    }

    private void ClearReceiverPairingSecret()
    {
        _receiverPairingSecret?.Dispose();
        _receiverPairingSecret = null;
        ReceiverPairingCode = string.Empty;
    }

    // ─── Session lifecycle ─────────────────────────────────────────────────────
    private void OnSessionStateChanged()
    {
        try
        {
            if (MainThread.IsMainThread) { HandleSessionStateChanged(); return; }
            MainThread.BeginInvokeOnMainThread(HandleSessionStateChanged);
        }
        catch
        {
            HandleSessionStateChanged();
        }
    }

    private void HandleSessionStateChanged()
    {
        if (!_vaultSession.IsAuthenticated)
        {
            _operationCts?.Cancel();
            ClearSensitiveData();
            PeerAddress = string.Empty;
            ResultMessage = string.Empty;
            ResultIsSuccess = false;
            ResultIsCancelled = false;
            SetState(SyncUiState.Idle, string.Empty, 0);
            RequestLockAction?.Invoke();
        }
        else
        {
            RefreshVaultState();
            RaiseCommandsCanExecuteChanged();
        }
    }

    // ─── Network status relay ──────────────────────────────────────────────────
    private void OnNetworkStatusChanged(string status)
    {
        try
        {
            if (MainThread.IsMainThread) { RelayNetworkStatus(status); return; }
            MainThread.BeginInvokeOnMainThread(() => RelayNetworkStatus(status));
        }
        catch { RelayNetworkStatus(status); }
    }

    private void RelayNetworkStatus(string status)
    {
        if (!IsOperationActive) return;
        StatusMessage = status;
        if (status.Contains("Проверка аутентификации") || status.Contains("Выполнение аутентификации"))
        {
            if (_uiState == SyncUiState.WaitingForPeer) UiState = SyncUiState.Authenticating;
        }
        else if (status.Contains("Приём зашифрованных данных") || status.Contains("Шифрование и отправка"))
        {
            if (_uiState is SyncUiState.Authenticating or SyncUiState.WaitingForPeer) UiState = SyncUiState.Transferring;
        }
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────
    private void RaiseCommandsCanExecuteChanged()
    {
        ((AsyncRelayCommand)StartSyncCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)StartSendCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CancelSyncCommand).RaiseCanExecuteChanged();
    }

    // ─── Dispose ───────────────────────────────────────────────────────────────
    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vaultSession.StateChanged -= OnSessionStateChanged;
        _syncBridge.StatusChanged -= OnNetworkStatusChanged;

        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _operationCts = null;
        ClearSensitiveData();
        base.Dispose();
    }
}
