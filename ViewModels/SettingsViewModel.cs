using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using RamManager.Services;

namespace RamManager.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        OpenSettingsFolderCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(SettingsService.FolderPath);
            Process.Start("explorer.exe", $"\"{SettingsService.FolderPath}\"");
        });
        ExitCommand = new RelayCommand(() => ((App)Application.Current).ExitApp());
        TestNotificationCommand = new RelayCommand(main.SendTestAlert);
    }

    private AppSettings Settings => _main.Settings;

    public ICommand OpenSettingsFolderCommand { get; }
    public ICommand ExitCommand { get; }

    // Refresh interval as three radio buttons.
    public bool Refresh1 { get => Settings.RefreshSeconds == 1; set { if (value) SetRefresh(1); } }
    public bool Refresh2 { get => Settings.RefreshSeconds == 2; set { if (value) SetRefresh(2); } }
    public bool Refresh5 { get => Settings.RefreshSeconds == 5; set { if (value) SetRefresh(5); } }

    private void SetRefresh(int seconds)
    {
        if (Settings.RefreshSeconds == seconds) return;
        Settings.RefreshSeconds = seconds;
        SettingsService.Save(Settings);
        OnPropertyChanged(nameof(Refresh1));
        OnPropertyChanged(nameof(Refresh2));
        OnPropertyChanged(nameof(Refresh5));
        _main.OnRefreshIntervalChanged();
    }

    public bool CloseToTray
    {
        get => Settings.CloseToTray;
        set
        {
            Settings.CloseToTray = value;
            SettingsService.Save(Settings);
            OnPropertyChanged();
        }
    }

    public bool ConfirmEndProcess
    {
        get => Settings.ConfirmEndProcess;
        set
        {
            Settings.ConfirmEndProcess = value;
            SettingsService.Save(Settings);
            OnPropertyChanged();
        }
    }

    // ----- Alerts -----
    public ICommand TestNotificationCommand { get; }

    public bool HighMemoryAlerts
    {
        get => Settings.HighMemoryAlerts;
        set
        {
            Settings.HighMemoryAlerts = value;
            SettingsService.Save(Settings);
            OnPropertyChanged();
        }
    }

    public bool GrowingAppAlerts
    {
        get => Settings.GrowingAppAlerts;
        set
        {
            Settings.GrowingAppAlerts = value;
            SettingsService.Save(Settings);
            OnPropertyChanged();
        }
    }

    public bool Threshold75 { get => Settings.HighMemoryThreshold == 75; set { if (value) SetThreshold(75); } }
    public bool Threshold80 { get => Settings.HighMemoryThreshold == 80; set { if (value) SetThreshold(80); } }
    public bool Threshold85 { get => Settings.HighMemoryThreshold == 85; set { if (value) SetThreshold(85); } }
    public bool Threshold90 { get => Settings.HighMemoryThreshold == 90; set { if (value) SetThreshold(90); } }
    public bool Threshold95 { get => Settings.HighMemoryThreshold == 95; set { if (value) SetThreshold(95); } }

    private void SetThreshold(int percent)
    {
        if (Settings.HighMemoryThreshold == percent) return;
        Settings.HighMemoryThreshold = percent;
        SettingsService.Save(Settings);
        OnPropertyChanged(nameof(Threshold75));
        OnPropertyChanged(nameof(Threshold80));
        OnPropertyChanged(nameof(Threshold85));
        OnPropertyChanged(nameof(Threshold90));
        OnPropertyChanged(nameof(Threshold95));
    }

    public bool IsAdmin => PrivilegeHelper.IsAdministrator;

    public string AdminText => IsAdmin ? "Running as administrator" : "Limited mode (not elevated)";

    public string VersionText => "Version " + (Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0");

    public string SettingsPath => SettingsService.FilePath;
}
