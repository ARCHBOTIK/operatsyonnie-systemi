using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using ZXing.Net.Maui.Controls;
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
            .UseBarcodeReader()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif


        builder.Services.AddSingleton<keyManager>(_ =>
            new keyManager(Path.Combine(FileSystem.AppDataDirectory, "keys.dat")));

        builder.Services.AddSingleton<MasterPasswordService>();
        builder.Services.AddSingleton<VaultSessionService>();
        builder.Services.AddSingleton<NetworkService>();
        builder.Services.AddSingleton<TcpBridge>();
        builder.Services.AddSingleton<IImportReceiverService>(sp => sp.GetRequiredService<TcpBridge>());

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

        builder.Services.AddSingleton<SecurePassword.Navigation.IAppRootNavigator, SecurePassword.Navigation.AppRootNavigator>();
        builder.Services.AddTransient<AppShell>();
        builder.Services.AddTransient<MasterPasswordPage>();
        builder.Services.AddTransient<SecurePassword.ViewModels.Generator.GeneratorViewModel>();

        builder.Services.AddTransient<SecurePassword.Views.Generator.GeneratorPage>();
        builder.Services.AddTransient<SecurePassword.ViewModels.Settings.SettingsViewModel>();
        builder.Services.AddTransient<SecurePassword.Views.Settings.SettingsPage>();
        builder.Services.AddTransient<SecurePassword.ViewModels.Sync.SyncViewModel>();
        builder.Services.AddTransient<SecurePassword.Views.Sync.SyncPage>();
        builder.Services.AddTransient<SecurePassword.ViewModels.Import.ImportViewModel>();
        builder.Services.AddTransient<SecurePassword.Views.Import.ImportPage>();
        builder.Services.AddTransient<SecurePassword.ViewModels.Vault.VaultViewModel>();
        builder.Services.AddTransient<SecurePassword.Views.Vault.VaultPage>();
        builder.Services.AddTransient<SecurePassword.ViewModels.Vault.ItemDetailViewModel>();
        builder.Services.AddTransient<SecurePassword.Views.Vault.ItemDetailPage>();
        builder.Services.AddTransient<SecurePassword.ViewModels.Vault.ItemEditViewModel>();
        builder.Services.AddTransient<SecurePassword.Views.Vault.ItemEditPage>();




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
