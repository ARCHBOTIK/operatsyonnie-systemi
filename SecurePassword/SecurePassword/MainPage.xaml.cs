using Microsoft.AspNetCore.Components.WebView.Maui;
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

        private void blazorWebView_BlazorWebViewInitializing(
            object? sender,
            BlazorWebViewInitializingEventArgs e)
        {
#if WINDOWS
            // Пока можно оставить пустым или добавить Windows-логику
#endif
        }

        private void blazorWebView_BlazorWebViewInitialized(
            object? sender,
            BlazorWebViewInitializedEventArgs e)
        {
#if WINDOWS
            if (e.WebView is WebView2 webView && webView.CoreWebView2 is not null)
            {
                webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            }
#endif
        }
    }
}