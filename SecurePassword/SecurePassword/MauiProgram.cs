using Microsoft.Extensions.Logging;
#if WINDOWS
using Velopack;
#endif

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
#if WINDOWS
            VelopackApp.Build().Run();
#endif
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
#if ANDROID
            builder.ConfigureLifecycleEvents(events =>
            {

                events.AddAndroid(android => android.OnCreate((activity, bundle) =>
                {
                    var color = Android.Graphics.Color.ParseColor("#17BFA6");

                    activity.Window.SetStatusBarColor(color);
                    activity.Window.SetNavigationBarColor(color);

                    if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.M)
                    {
                        activity.Window.DecorView.SystemUiVisibility = 0;
                    }
                }));

            });
#endif

            return builder.Build();
        }
    }
}
