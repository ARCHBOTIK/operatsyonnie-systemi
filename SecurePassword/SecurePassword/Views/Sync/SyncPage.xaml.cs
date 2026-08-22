using SecurePassword.ViewModels.Sync;

namespace SecurePassword.Views.Sync;

public partial class SyncPage : ContentPage
{
    private readonly SyncViewModel _viewModel;

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
        _viewModel.CancelCurrentOperation();
        _viewModel.Dispose();
        if (Navigation.ModalStack.Count > 0)
            await Navigation.PopModalAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.ClearSensitiveData();
    }
}
