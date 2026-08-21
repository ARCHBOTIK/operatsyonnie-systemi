using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace SecurePassword
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Window?.SetFlags(WindowManagerFlags.Secure, WindowManagerFlags.Secure);
        }

        protected override void OnResume()
        {
            base.OnResume();
            Window?.SetFlags(WindowManagerFlags.Secure, WindowManagerFlags.Secure);
        }
    }
}
