using SecurePassword.ViewModels.Settings;

namespace SecurePassword.Views.Settings;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
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
                {
                    await Navigation.PopModalAsync();
                }
            });
        };

        _viewModel.NavigateToSyncAction = async () =>
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (Navigation.ModalStack.Count > 0)
                {
                    await Navigation.PopModalAsync();
                }

                if (Shell.Current != null)
                {
                    await Shell.Current.GoToAsync("//sync");
                }
            });
        };

        _viewModel.NavigateToImportAction = async () =>
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (Navigation.ModalStack.Count > 0)
                {
                    await Navigation.PopModalAsync();
                }

                if (Shell.Current != null)
                {
                    await Shell.Current.GoToAsync("import");
                }
            });
        };
    }

    protected override bool OnBackButtonPressed()
    {
        _viewModel.Dispose();
        return base.OnBackButtonPressed();
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
        if (Navigation.ModalStack.Count > 0)
        {
            await Navigation.PopModalAsync();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.ClearSensitiveData();
    }
}
