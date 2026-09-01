using SecurePassword.ViewModels.Import;

namespace SecurePassword.Views.Import;

public partial class ImportPage : ContentPage
{
    private readonly ImportViewModel _viewModel;

    public ImportPage(ImportViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = _viewModel;
    }

    protected override bool OnBackButtonPressed()
    {
        _viewModel.CancelReceiver();
        _viewModel.Dispose();
        return base.OnBackButtonPressed();
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        try
        {
            _viewModel.CancelReceiver();
            _viewModel.Dispose();

            if (Navigation.ModalStack.Count > 0)
                await Navigation.PopModalAsync();
            else if (Shell.Current is not null)
                await Shell.Current.GoToAsync("..");
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError(
                "Failed to close import page. ExceptionType={0}",
                exception.GetType().FullName);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.ClearSensitiveData();
    }
}
