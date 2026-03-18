using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Maui.Controls;
#if WINDOWS
using Microsoft.UI.Xaml.Controls;
#endif
namespace SecurePassword
{

    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }
#if WINDOWS
        private void blazorWebView_BlazorWebViewInitializing(object? sender, BlazorWebViewInitializingEventArgs e)
        {

        // Пока можно оставить пустым

        }

        private void blazorWebView_BlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
        {
        if (e.WebView is WebView2 webView && webView.CoreWebView2 is not null)
        {
            webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
        }
        }
#endif
}
}
