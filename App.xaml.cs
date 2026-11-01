using System.Windows;
using System.Windows.Threading;
using RamManager.Helpers;
using RamManager.Services;
using RamManager.ViewModels;
using RamManager.Views;

namespace RamManager;

public partial class App : Application
{
#if DEBUG
    // Debug builds can run next to an installed release copy.
    private const string MutexName = @"Local\RamManager.Dev.SingleInstance";
    private const string ShowEventName = @"Local\RamManager.Dev.ShowWindow";
#else
    private const string MutexName = @"Local\RamManager.SingleInstance";
    private const string ShowEventName = @"Local\RamManager.ShowWindow";
#endif

    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private readonly CancellationTokenSource _cts = new();

    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private TrayIconService? _tray;

    public static bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Single instance: if already running, ask the existing instance to show itself.
        _mutex = new Mutex(true, MutexName, out _ownsMutex);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!_ownsMutex)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        PrivilegeHelper.EnableMemoryPrivileges();

        var settings = SettingsService.Load();
        _viewModel = new MainViewModel(settings);
        _window = new MainWindow { DataContext = _viewModel };
        MainWindow = _window;

        _tray = new TrayIconService();
        _tray.OpenRequested += ShowMainWindow;
        _tray.QuickCleanRequested += () => _viewModel.QuickCleanCommand.Execute(null);
        _tray.ExitRequested += ExitApp;
        _viewModel.MemoryUpdated += _tray.Update;
        _viewModel.ToastShown += message =>
        {
            if (_window is { IsVisible: false }) _tray.ShowBalloon("RAM Manager", message);
        };
        _viewModel.AlertRaised += alert =>
        {
            var icon = alert.Kind == AlertKind.Test ? System.Windows.Forms.ToolTipIcon.Info : System.Windows.Forms.ToolTipIcon.Warning;
            _tray.ShowNotification(alert.Title, alert.Message, icon, () =>
            {
                _viewModel.OpenAlert(alert);
                ShowMainWindow();
            });
        };

        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(ShowMainWindow), null, Timeout.Infinite, false);

        _window.Show();
        _ = _viewModel.RunAsync(_cts.Token);

#if DEBUG
        int index = Array.IndexOf(e.Args, "--screenshot");
        if (index >= 0 && index + 1 < e.Args.Length) _ = _window.CaptureScreenshotsAsync(e.Args[index + 1]);
#endif
    }

    public void ShowMainWindow()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true;  // bring to front reliably
        _window.Topmost = false;
        _window.Focus();
    }

    /// <summary>Called by the window when it is closed but the app keeps running in the tray.</summary>
    public void OnHiddenToTray()
    {
        if (_viewModel is null || _tray is null || _viewModel.Settings.TrayHintShown) return;
        _tray.ShowBalloon("RAM Manager is still running",
            "It keeps monitoring memory from the system tray. Right-click the tray icon to exit.");
        _viewModel.Settings.TrayHintShown = true;
        SettingsService.Save(_viewModel.Settings);
    }

    public void ExitApp()
    {
        if (IsExiting) return;
        IsExiting = true;
        _cts.Cancel();
        _window?.Close();
        Shutdown();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception");
        _viewModel?.ShowToast("Something went wrong: " + e.Exception.Message, ToastKind.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showWait?.Unregister(null);
        _tray?.Dispose();
        _viewModel?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _showEvent?.Dispose();
        base.OnExit(e);
    }
}
