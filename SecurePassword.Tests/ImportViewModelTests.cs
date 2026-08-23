using System.Security.Cryptography;
using SecurePassword.ViewModels.Import;
using Xunit;

namespace SecurePassword.Tests;

public sealed class ImportViewModelTests : IDisposable
{
    private readonly VaultSessionService _session = new();
    private readonly FakeImportReceiver _receiver = new();

    public ImportViewModelTests() => _session.MarkAuthenticated();

    public void Dispose() => _session.Dispose();

    [Fact]
    public async Task Import_StartReceiver_CreatesQrSession()
    {
        using var vm = CreateViewModel();

        await vm.StartReceiverAsync();

        Assert.Equal(ImportUiState.WaitingForSender, vm.UiState);
        Assert.True(vm.HasActiveQr);
        Assert.StartsWith("vaultpass://pair?v=1", vm.QrPayload);
        Assert.Equal("192.168.10.25", vm.ReceiverAddress);
        Assert.Equal(14, vm.PairingCode.Length);
    }

    [Fact]
    public async Task Import_Cancel_InvalidatesQr()
    {
        using var vm = CreateViewModel();
        await vm.StartReceiverAsync();

        vm.CancelReceiver();

        Assert.False(vm.HasActiveQr);
        Assert.Empty(vm.QrPayload);
        Assert.Empty(vm.PairingCode);
        Assert.True(_receiver.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Import_Timeout_InvalidatesQr()
    {
        _receiver.WaitForCancellation = true;
        using var vm = CreateViewModel(() => PairingSecret.Generate(1));
        await vm.StartReceiverAsync();

        await WaitUntilAsync(() => vm.UiState == ImportUiState.Cancelled, TimeSpan.FromSeconds(3));

        Assert.False(vm.HasActiveQr);
        Assert.Empty(vm.PairingCode);
    }

    [Fact]
    public async Task Import_ConfirmedTransfer_UsesVaultImportTransaction()
    {
        using var vm = CreateViewModel();
        await vm.StartReceiverAsync();
        var pending = new FakePendingImport();

        _receiver.Complete(pending);
        await WaitUntilAsync(() => vm.IsAwaitingConfirmation, TimeSpan.FromSeconds(1));
        vm.ConfirmImport();

        Assert.True(pending.Committed);
        Assert.False(pending.RolledBack);
        Assert.False(_session.IsAuthenticated);
    }

    [Fact]
    public async Task Import_RejectedConfirmation_DoesNotModifyVault()
    {
        using var vm = CreateViewModel();
        await vm.StartReceiverAsync();
        var pending = new FakePendingImport();

        _receiver.Complete(pending);
        await WaitUntilAsync(() => vm.IsAwaitingConfirmation, TimeSpan.FromSeconds(1));
        vm.RejectImport();

        Assert.False(pending.Committed);
        Assert.True(pending.RolledBack);
        Assert.True(_session.IsAuthenticated);
    }

    [Fact]
    public async Task Import_Success_LocksVaultForReauthentication()
    {
        using var vm = CreateViewModel();
        await vm.StartReceiverAsync();
        _receiver.Complete(new FakePendingImport());
        await WaitUntilAsync(() => vm.IsAwaitingConfirmation, TimeSpan.FromSeconds(1));

        vm.ConfirmImport();

        Assert.False(_session.IsAuthenticated);
    }

    [Fact]
    public async Task Import_Lock_CancelsReceiver()
    {
        using var vm = CreateViewModel();
        await vm.StartReceiverAsync();

        _session.Lock();

        Assert.True(_receiver.LastToken.IsCancellationRequested);
        Assert.False(vm.HasActiveQr);
        Assert.Equal(ImportUiState.Idle, vm.UiState);
    }

    [Fact]
    public async Task Import_Dispose_ClearsPairingState()
    {
        var vm = CreateViewModel();
        await vm.StartReceiverAsync();

        vm.Dispose();

        Assert.Empty(vm.QrPayload);
        Assert.Empty(vm.PairingCode);
        Assert.True(_receiver.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task OldQrAfterSuccessfulTransfer_IsCleared()
    {
        using var vm = CreateViewModel();
        await vm.StartReceiverAsync();
        string oldPayload = vm.QrPayload;
        _receiver.Complete(new FakePendingImport());

        await WaitUntilAsync(() => vm.IsAwaitingConfirmation, TimeSpan.FromSeconds(1));

        Assert.Empty(vm.QrPayload);
        Assert.NotEmpty(oldPayload);
    }

    [Fact]
    public async Task OldQrAfterAuthFailure_IsCleared()
    {
        using var vm = CreateViewModel();
        await vm.StartReceiverAsync();
        _receiver.Fail(new CryptographicException());

        await WaitUntilAsync(() => vm.UiState == ImportUiState.Failed, TimeSpan.FromSeconds(1));

        Assert.False(vm.HasActiveQr);
        Assert.Empty(vm.QrPayload);
    }

    private ImportViewModel CreateViewModel(Func<PairingSecret>? factory = null) =>
        new(_receiver, _session, factory);

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTimeOffset endsAt = DateTimeOffset.UtcNow.Add(timeout);
        while (!condition() && DateTimeOffset.UtcNow < endsAt)
            await Task.Delay(20);

        Assert.True(condition());
    }

    private sealed class FakeImportReceiver : IImportReceiverService
    {
        private TaskCompletionSource<IPendingVaultImport> _completion = NewCompletion();

        public CancellationToken LastToken { get; private set; }
        public bool WaitForCancellation { get; set; }

        public string? GetLocalPeerAddress() => "192.168.10.25";
        public bool LocalVaultExists() => true;

        public Task<IPendingVaultImport> ReceiveVaultForConfirmationAsync(PairingSecret pairingSecret, CancellationToken token = default)
        {
            LastToken = token;
            if (WaitForCancellation)
                return Task.Delay(Timeout.InfiniteTimeSpan, token).ContinueWith<IPendingVaultImport>(_ => throw new OperationCanceledException(token), TaskScheduler.Default);

            return _completion.Task;
        }

        public void Complete(IPendingVaultImport import) => _completion.TrySetResult(import);
        public void Fail(Exception exception) => _completion.TrySetException(exception);

        private static TaskCompletionSource<IPendingVaultImport> NewCompletion() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class FakePendingImport : IPendingVaultImport
    {
        public bool Committed { get; private set; }
        public bool RolledBack { get; private set; }
        public void Commit() => Committed = true;
        public void Rollback() => RolledBack = true;
    }
}
