using SecurePassword.ViewModels.Generator;

namespace SecurePassword.Views.Generator;

public partial class GeneratorPage : ContentPage
{
    private readonly GeneratorViewModel _viewModel;

    public GeneratorPage(GeneratorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = _viewModel;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.ClearSensitiveData();
    }

    protected override bool OnBackButtonPressed()
    {
        _viewModel.Dispose();
        return base.OnBackButtonPressed();
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        try
        {
            _viewModel.Dispose();
            if (Navigation.ModalStack.Count > 0)
            {
                await Navigation.PopModalAsync();
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError(
                "Failed to close generator page. ExceptionType={0}",
                exception.GetType().FullName);
        }
    }
}
