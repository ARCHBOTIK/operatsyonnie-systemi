using SecurePassword.ViewModels.Vault;

namespace SecurePassword.Views.Vault;

public partial class VaultPage : ContentPage
{
    private readonly VaultViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;

    public VaultPage(VaultViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
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

        _viewModel.NavigateToDetailAction = async (item) =>
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var detailVm = _serviceProvider.GetRequiredService<ItemDetailViewModel>();
                var detailPage = new ItemDetailPage(detailVm);

                detailVm.ItemDeletedAction = async () =>
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await _viewModel.LoadVaultAsync();
                        if (Navigation.ModalStack.Count > 0)
                            await Navigation.PopModalAsync();
                    });
                };

                detailVm.NavigateToEditAction = async (id, type) =>
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        var editVm = _serviceProvider.GetRequiredService<ItemEditViewModel>();
                        editVm.InitializeForEdit(id, type);
                        var editPage = new ItemEditPage(editVm);

                        editVm.ItemSavedAction = async () =>
                        {
                            await MainThread.InvokeOnMainThreadAsync(async () =>
                            {
                                await detailVm.LoadItemAsync(id, type);
                                await _viewModel.LoadVaultAsync();
                                if (Navigation.ModalStack.Count > 0)
                                    await Navigation.PopModalAsync();
                            });
                        };

                        await Navigation.PushModalAsync(editPage);
                    });
                };

                await detailVm.LoadItemAsync(item.Id, item.Type);
                await Navigation.PushModalAsync(detailPage);
            });
        };

        _viewModel.NavigateToAddItemAction = async () =>
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var editVm = _serviceProvider.GetRequiredService<ItemEditViewModel>();
                editVm.InitializeForAdd();
                var editPage = new ItemEditPage(editVm);

                editVm.ItemSavedAction = async () =>
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await _viewModel.LoadVaultAsync();
                        if (Navigation.ModalStack.Count > 0)
                            await Navigation.PopModalAsync();
                    });
                };

                await Navigation.PushModalAsync(editPage);
            });
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadVaultAsync();
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
            await Navigation.PopModalAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.ClearSensitiveData();
    }
}
