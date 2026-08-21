using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
#if WINDOWS
using Velopack;
#endif

namespace SecurePassword;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
#if WINDOWS
        VelopackApp.Build().Run();
#endif

        FileWorker.CleanupLeftoverTempFiles();
        VaultImportTransaction.RecoverPendingTransactions();

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

        builder.Services.AddSingleton<keyManager>(_ =>
            new keyManager(Path.Combine(FileSystem.AppDataDirectory, "keys.dat")));

        builder.Services.AddSingleton<MasterPasswordService>();
        builder.Services.AddSingleton<VaultSessionService>();
        builder.Services.AddSingleton<NetworkService>();
        builder.Services.AddSingleton<TcpBridge>();

#if ANDROID
        builder.Services.AddSingleton<IClipboardBackend, AndroidClipboardBackend>();
#else
        builder.Services.AddSingleton<IClipboardBackend, MauiClipboardBackend>();
#endif
        builder.Services.AddSingleton<ISecureClipboardService, SecureClipboardService>();

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

        builder.Services.AddSingleton<MasterPasswordPage>();

#if ANDROID
        builder.ConfigureLifecycleEvents(events =>
        {
            events.AddAndroid(android => android.OnCreate((activity, _) =>
            {
                var color = Android.Graphics.Color.ParseColor("#17BFA6");

                activity.Window?.SetStatusBarColor(color);
                activity.Window?.SetNavigationBarColor(color);

                if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.M)
                {
                    activity.Window?.DecorView!.SystemUiVisibility = 0;
                }
            }));
        });
#endif

        return builder.Build();
    }
}
