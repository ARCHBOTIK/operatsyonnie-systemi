using Microsoft.Extensions.Logging;
using Velopack;

#if WINDOWS
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Microsoft.Maui.Handlers;
using WinRT.Interop;
using Windows.Graphics;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

#endif

namespace SecurePassword
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            VelopackApp.Build().Run();
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

            // keyManager
            builder.Services.AddSingleton<keyManager>(sp =>
                new keyManager(
                    Path.Combine(FileSystem.AppDataDirectory, "keys.dat")));

            builder.Services.AddSingleton<MasterPasswordService>();

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

            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddSingleton<MasterPasswordPage>();

            return builder.Build();
        }
    }
}
