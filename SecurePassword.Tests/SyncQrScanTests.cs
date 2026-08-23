using SecurePassword.ViewModels.Sync;
using Xunit;

namespace SecurePassword.Tests;

public sealed class SyncQrScanTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _originalAppDataDir;
    private readonly keyManager _keyManager;
    private readonly VaultSessionService _session = new();
    private readonly TcpBridge _bridge;

    public SyncQrScanTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SyncQrTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _originalAppDataDir = FileWorker.TestingAppDataDirectory ?? string.Empty;
        FileWorker.TestingAppDataDirectory = _testDir;
        _keyManager = new keyManager(Path.Combine(_testDir, "keys.dat"));
        _bridge = new TcpBridge(new NetworkService(), _keyManager);
        _session.MarkAuthenticated();
    }

    public void Dispose()
    {
        _session.Dispose();
        _keyManager.ClearLoadedKey();
        FileWorker.TestingAppDataDirectory = _originalAppDataDir;
        try { Directory.Delete(_testDir, true); } catch { }
    }

    [Fact]
    public void Scan_ValidQr_PopulatesConnectionData()
    {
        using var vm = new SyncViewModel(_bridge, _session);
        vm.ApplyScannedQr(ValidPayload("192.168.10.25", "ABCD-EFGH-JKLM"));

        Assert.Equal(SyncTransferMode.Upload, vm.SelectedMode);
        Assert.Equal("192.168.10.25", vm.PeerAddress);
        Assert.Equal("ABCD-EFGH-JKLM", vm.PeerPairingCode);
        Assert.Empty(vm.ValidationError);
    }

    [Fact]
    public void Scan_InvalidQr_ShowsError()
    {
        using var vm = new SyncViewModel(_bridge, _session);
        vm.ApplyScannedQr("https://not-vaultpass.example/");

        Assert.True(vm.HasValidationError);
        Assert.True(vm.ShowManualEntry);
        Assert.Empty(vm.PeerAddress);
    }

    [Fact]
    public void CameraDenied_AllowsManualFallback()
    {
        using var vm = new SyncViewModel(_bridge, _session);
        vm.CameraPermissionDenied();

        Assert.True(vm.ShowManualEntry);
        Assert.True(vm.HasValidationError);
    }

    [Fact]
    public void SecondScan_ReplacesPreviousPairingData()
    {
        using var vm = new SyncViewModel(_bridge, _session);
        vm.ApplyScannedQr(ValidPayload("192.168.10.25", "ABCD-EFGH-JKLM"));
        vm.ApplyScannedQr(ValidPayload("10.0.0.12", "MNPR-STUV-WXYZ"));

        Assert.Equal("10.0.0.12", vm.PeerAddress);
        Assert.Equal("MNPR-STUV-WXYZ", vm.PeerPairingCode);
    }

    [Fact]
    public async Task SendScreenAction_AlwaysUsesUploadMode()
    {
        using var vm = new SyncViewModel(_bridge, _session);
        Assert.Equal(SyncTransferMode.Download, vm.SelectedMode);

        await vm.StartSendAsync();

        Assert.Equal(SyncTransferMode.Upload, vm.SelectedMode);
        Assert.True(vm.HasValidationError);
    }

    private static string ValidPayload(string host, string code) =>
        $"vaultpass://pair?v=1&host={host}&port=50555&code={code}&exp={DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds()}";
}
