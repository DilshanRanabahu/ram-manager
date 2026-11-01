using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using RamManager.ViewModels;
using static RamManager.Native.NativeMethods;

namespace RamManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        IsVisibleChanged += (_, _) => UpdateUiVisibility();
        StateChanged += (_, _) => UpdateUiVisibility();

        // Subtle fade when switching pages.
        foreach (var page in new FrameworkElement[] { DashboardPage, ProcessesPage, SettingsPage })
        {
            page.IsVisibleChanged += (sender, e) =>
            {
                if (e.NewValue is not true) return;
                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
                ((UIElement)sender).BeginAnimation(OpacityProperty, fade);
            };
        }
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Dark title bar that matches the app (Windows 10 20H1+ / Windows 11).
        nint hwnd = new WindowInteropHelper(this).Handle;
        int enabled = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
        int captionColor = 0x00141010; // COLORREF (BGR) of the sidebar color #101014
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (App.IsExiting) return;

        if (ViewModel.Settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            ((App)Application.Current).OnHiddenToTray();
        }
        else
        {
            e.Cancel = true;
            ((App)Application.Current).ExitApp();
        }
    }

    private void UpdateUiVisibility()
    {
        if (DataContext is MainViewModel vm)
            vm.SetUiVisible(IsVisible && WindowState != WindowState.Minimized);
    }

#if DEBUG
    /// <summary>
    /// Development helper: renders each page to a PNG, then exits (run with --screenshot &lt;folder&gt;).
    /// The window is made tall enough to show the whole dashboard, and the dev-only admin banner is hidden.
    /// </summary>
    public async Task CaptureScreenshotsAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        AdminBanner.Visibility = Visibility.Collapsed;
        Width = 1240;
        Height = 1100;

        await Task.Delay(45000); // let the usage history chart fill up
        SaveSnapshot(Path.Combine(folder, "dashboard.png"));

        ViewModel.NavigateCommand.Execute(ViewModel.Processes);
        await Task.Delay(2500);
        SaveSnapshot(Path.Combine(folder, "processes.png"));

        ViewModel.NavigateCommand.Execute(ViewModel.SettingsPage);
        await Task.Delay(800);
        SaveSnapshot(Path.Combine(folder, "settings.png"));

        ((App)Application.Current).ExitApp();
    }

    private void SaveSnapshot(string path)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap(
            (int)(Root.ActualWidth * dpi.DpiScaleX), (int)(Root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(Root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
#endif
}
