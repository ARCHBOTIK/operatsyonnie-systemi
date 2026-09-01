using SecurePassword.ViewModels.Sync;

#if ANDROID
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;
#endif

namespace SecurePassword.Views.Sync;

public partial class SyncPage : ContentPage
{
    private readonly SyncViewModel _viewModel;
#if ANDROID
    private CameraBarcodeReaderView? _cameraReader;
#endif

    public SyncPage(SyncViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = _viewModel;

        _viewModel.RequestLockAction = async () =>
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                _viewModel.Dispose();
                if (Navigation.ModalStack.Count > 0)
                    await Navigation.PopModalAsync();
            });
        };
    }

    protected override bool OnBackButtonPressed()
    {
        _viewModel.CancelCurrentOperation();
        _viewModel.Dispose();
        return base.OnBackButtonPressed();
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        try
        {
            _viewModel.CancelCurrentOperation();
            _viewModel.Dispose();
            if (Navigation.ModalStack.Count > 0)
                await Navigation.PopModalAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError(
                "Failed to close sync page. ExceptionType={0}",
                exception.GetType().FullName);
        }
    }

    private async void OnScanQrClicked(object? sender, EventArgs e)
    {
        try
        {
#if ANDROID
            if (!BarcodeScanning.IsSupported)
            {
                _viewModel.CameraPermissionDenied();
                return;
            }

            PermissionStatus permission = await Permissions.RequestAsync<Permissions.Camera>();
            if (permission != PermissionStatus.Granted)
            {
                _viewModel.CameraPermissionDenied();
                return;
            }

            _viewModel.BeginQrScan();
            EnsureCameraReader();
#else
            _viewModel.CameraPermissionDenied();
#endif
        }
        catch (Exception exception)
        {
            _viewModel.CameraPermissionDenied();
            System.Diagnostics.Trace.TraceError(
                "Failed to start QR scanner. ExceptionType={0}",
                exception.GetType().FullName);
        }
    }

#if ANDROID
    private void EnsureCameraReader()
    {
        if (_cameraReader is not null)
        {
            _cameraReader.IsDetecting = true;
            return;
        }

        _cameraReader = new CameraBarcodeReaderView
        {
            IsDetecting = true,
            Options = new BarcodeReaderOptions
            {
                Formats = BarcodeFormats.TwoDimensional,
                AutoRotate = true,
                Multiple = false
            }
        };
        _cameraReader.BarcodesDetected += OnBarcodesDetected;
        ScannerHost.Content = _cameraReader;
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        string? payload = e.Results.FirstOrDefault()?.Value;
        if (_cameraReader is not null)
            _cameraReader.IsDetecting = false;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            DisposeCameraReader();
            _viewModel.ApplyScannedQr(payload);
        });
    }

    private void DisposeCameraReader()
    {
        if (_cameraReader is null)
            return;

        _cameraReader.BarcodesDetected -= OnBarcodesDetected;
        _cameraReader.IsDetecting = false;
        ScannerHost.Content = null;
        _cameraReader = null;
    }
#endif

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if ANDROID
        DisposeCameraReader();
#endif
        _viewModel.ClearSensitiveData();
    }
}
