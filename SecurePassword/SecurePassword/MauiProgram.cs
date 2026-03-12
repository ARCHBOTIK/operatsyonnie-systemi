using Microsoft.Extensions.Logging;

#if WINDOWS
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Microsoft.Maui.Handlers;
using WinRT.Interop;
using Windows.Graphics;

#endif

namespace SecurePassword
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
    		builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

#if WINDOWS
        // Выполнится, когда WindowHandler создастся (т.е. окно уже реально существует)
        WindowHandler.Mapper.AppendToMapping("PhoneSize", (handler, view) =>
        {
            const int W = 400;
            const int H = 800;

            var window = handler.PlatformView; // Microsoft.UI.Xaml.Window
            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            appWindow.Resize(new SizeInt32(W, H));

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
            }
        });
#endif
            // keyManager
            builder.Services.AddSingleton<keyManager>(sp =>
                new keyManager(
                    Path.Combine(FileSystem.AppDataDirectory, "keys.dat")));

            // Репозитории
            builder.Services.AddSingleton<SecureRepository<PasswordEntry>>(sp =>
                new SecureRepository<PasswordEntry>(
                    Path.Combine(FileSystem.AppDataDirectory, "passwords.dat"),
                    sp.GetRequiredService<keyManager>()));

            builder.Services.AddSingleton<SecureRepository<CardEntry>>(sp =>
                new SecureRepository<CardEntry>(
                    Path.Combine(FileSystem.AppDataDirectory, "cards.dat"),
                    sp.GetRequiredService<keyManager>()));

            builder.Services.AddSingleton<SecureRepository<NoteEntry>>(sp =>
                new SecureRepository<NoteEntry>(
                    Path.Combine(FileSystem.AppDataDirectory, "notes.dat"),
                    sp.GetRequiredService<keyManager>()));

            return builder.Build();
        }
    }
}
