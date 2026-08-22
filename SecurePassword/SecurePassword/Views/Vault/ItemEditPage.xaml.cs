using SecurePassword.ViewModels.Vault;

namespace SecurePassword.Views.Vault;

public partial class ItemEditPage : ContentPage
{
    private readonly ItemEditViewModel _viewModel;

    public ItemEditPage(ItemEditViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = _viewModel;

        _viewModel.CloseAction = async () =>
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                _viewModel.Dispose();
                if (Navigation.ModalStack.Count > 0)
                    await Navigation.PopModalAsync();
            });
        };

        _viewModel.ItemSavedAction = async () =>
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                _viewModel.Dispose();
                if (Navigation.ModalStack.Count > 0)
                    await Navigation.PopModalAsync();
            });
        };

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
        _viewModel.Dispose();
        return base.OnBackButtonPressed();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.ClearSensitiveData();
        _viewModel.Dispose();
    }
}
