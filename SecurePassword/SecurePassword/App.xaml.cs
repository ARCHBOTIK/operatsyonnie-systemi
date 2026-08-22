#if WINDOWS
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Windows.Graphics;
#endif

using SecurePassword.Navigation;

namespace SecurePassword;

public partial class App : Application
{
    private readonly IAppRootNavigator _rootNavigator;
    private readonly VaultSessionService _vaultSession;

    public App(IAppRootNavigator rootNavigator, VaultSessionService vaultSession)
    {
        InitializeComponent();
        _rootNavigator = rootNavigator ?? throw new ArgumentNullException(nameof(rootNavigator));
        _vaultSession = vaultSession ?? throw new ArgumentNullException(nameof(vaultSession));
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_rootNavigator.GetInitialRoot())
        {
            Title = "VaultPass"
        };

        _rootNavigator.AttachWindow(window);

        window.Activated += (_, _) => _vaultSession.RecordActivity();
        window.Resumed += (_, _) => _vaultSession.RecordActivity();
        window.Stopped += (_, _) =>
        {
            if (_vaultSession.LockOnMinimize)
                _vaultSession.Lock();
        };
        window.Destroying += (_, _) =>
        {
            if (_vaultSession.LockOnExit)
                _vaultSession.Lock();
        };


#if WINDOWS
        window.Created += async (_, _) =>
        {
            await Task.Delay(100);

            window.Dispatcher.Dispatch(() =>
            {
                ConfigureWindowsWindow(window);
            });
        };
#endif

        return window;
    }

#if WINDOWS
    private static void ConfigureWindowsWindow(Window mauiWindow)
    {
        var nativeWindow = mauiWindow.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (nativeWindow is null)
            return;

        IntPtr hwnd = WindowNative.GetWindowHandle(nativeWindow);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Restore();
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
            presenter.IsResizable = true;
        }

        appWindow.MoveAndResize(new RectInt32(100, 100, 420, 860));
        SetMinWindowSize(hwnd, 360, 700);
    }

    private static int _minWidth = 360;
    private static int _minHeight = 700;
    private static nint _oldWndProc;

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);
    private static WndProcDelegate? _wndProcDelegate;

    private const int GWL_WNDPROC = -4;
    private const uint WM_GETMINMAXINFO = 0x0024;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point ptReserved;
        public Point ptMaxSize;
        public Point ptMaxPosition;
        public Point ptMinTrackSize;
        public Point ptMaxTrackSize;
    }

    private static void SetMinWindowSize(IntPtr hwnd, int minWidth, int minHeight)
    {
        _minWidth = minWidth;
        _minHeight = minHeight;

        if (_oldWndProc != 0)
            return;

        _wndProcDelegate = CustomWndProc;
        _oldWndProc = SetWindowLongPtr(hwnd, GWL_WNDPROC,
            System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
    }

    private static nint CustomWndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MinMaxInfo>(lParam);
            mmi.ptMinTrackSize.X = _minWidth;
            mmi.ptMinTrackSize.Y = _minHeight;
            System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
        }

        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);
#endif
}
