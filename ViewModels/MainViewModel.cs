using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RamManager.Helpers;
using RamManager.Services;

namespace RamManager.ViewModels;

public enum ToastKind { Success, Warning, Error, Info }

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ProcessService _processService = new();
    private readonly AlertService _alerts;
    private readonly DispatcherTimer _toastTimer;
    private TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _uiVisible = true;

    public MainViewModel(AppSettings settings)
    {
        Settings = settings;
        _alerts = new AlertService(settings);
        Dashboard = new DashboardViewModel(this);
        Processes = new ProcessesViewModel(this);
        SettingsPage = new SettingsViewModel(this);
        _currentPage = Dashboard;

        NavigateCommand = new RelayCommand(page => { if (page is not null) CurrentPage = page; });
        QuickCleanCommand = new AsyncRelayCommand(QuickCleanAsync, () => !IsCleaning);

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };
    }

    public AppSettings Settings { get; }
    public DashboardViewModel Dashboard { get; }
    public ProcessesViewModel Processes { get; }
    public SettingsViewModel SettingsPage { get; }

    public bool IsAdmin => PrivilegeHelper.IsAdministrator;

    public ICommand NavigateCommand { get; }
    public ICommand QuickCleanCommand { get; }

    /// <summary>Raised on every refresh with the latest memory numbers (used by the tray icon).</summary>
    public event Action<MemorySnapshot>? MemoryUpdated;

    /// <summary>Raised whenever a toast is shown (so the tray can show it as a balloon when hidden).</summary>
    public event Action<string>? ToastShown;

    /// <summary>Raised when a high-memory or growing-app alert should be shown as a Windows notification.</summary>
    public event Action<Alert>? AlertRaised;

    public void SendTestAlert() => AlertRaised?.Invoke(new Alert(AlertKind.Test,
        "RAM Manager notifications are working",
        $"You'll be notified when memory stays above {Settings.HighMemoryThreshold}% or an app keeps growing."));

    /// <summary>Opens the page that is relevant to an alert the user clicked.</summary>
    public void OpenAlert(Alert alert)
    {
        if (alert.Kind == AlertKind.GrowingApp && alert.ProcessName is { } name)
        {
            CurrentPage = Processes;
            Processes.SearchText = name;
        }
        else if (alert.Kind == AlertKind.HighMemory)
        {
            CurrentPage = Dashboard;
        }
    }

    // ----- Navigation -----
    private object _currentPage;
    public object CurrentPage
    {
        get => _currentPage;
        set
        {
            if (!SetProperty(ref _currentPage, value)) return;
            OnPropertyChanged(nameof(IsDashboardActive));
            OnPropertyChanged(nameof(IsProcessesActive));
            OnPropertyChanged(nameof(IsSettingsActive));
        }
    }

    public bool IsDashboardActive => CurrentPage == Dashboard;
    public bool IsProcessesActive => CurrentPage == Processes;
    public bool IsSettingsActive => CurrentPage == SettingsPage;

    // ----- Cleaning state -----
    private bool _isCleaning;
    public bool IsCleaning
    {
        get => _isCleaning;
        private set
        {
            if (!SetProperty(ref _isCleaning, value)) return;
            OnPropertyChanged(nameof(QuickCleanLabel));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string QuickCleanLabel => IsCleaning ? "Cleaning…" : "Quick clean";

    private string _lastCleanText = "Not run yet this session";
    public string LastCleanText
    {
        get => _lastCleanText;
        private set => SetProperty(ref _lastCleanText, value);
    }

    // ----- Toast -----
    private string _toastMessage = "";
    public string ToastMessage { get => _toastMessage; private set => SetProperty(ref _toastMessage, value); }

    private string _toastGlyph = "";
    public string ToastGlyph { get => _toastGlyph; private set => SetProperty(ref _toastGlyph, value); }

    private Brush? _toastBrush;
    public Brush? ToastBrush { get => _toastBrush; private set => SetProperty(ref _toastBrush, value); }

    private bool _isToastVisible;
    public bool IsToastVisible { get => _isToastVisible; private set => SetProperty(ref _isToastVisible, value); }

    public void ShowToast(string message, ToastKind kind = ToastKind.Success)
    {
        ToastMessage = message;
        ToastGlyph = kind switch
        {
            ToastKind.Success => "",
            ToastKind.Warning => "",
            ToastKind.Error => "",
            _ => "",
        };
        ToastBrush = Palette.Get(kind switch
        {
            ToastKind.Success => "SuccessBrush",
            ToastKind.Warning => "WarningBrush",
            ToastKind.Error => "DangerBrush",
            _ => "AccentBrush",
        });

        IsToastVisible = false;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
        ToastShown?.Invoke(message);
    }

    // ----- Monitoring loop -----
    public async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Refresh failed");
            }

            var wake = _wake.Task;
            try
            {
                await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(Settings.RefreshSeconds), token), wake);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            if (wake.IsCompleted) _wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void Tick()
    {
        var memory = MemoryService.GetSnapshot();
        Dashboard.UpdateMemory(memory);
        MemoryUpdated?.Invoke(memory);
        if (_alerts.CheckMemory(memory) is { } alert) AlertRaised?.Invoke(alert);

        // Processes are scanned every tick while the window is visible. In the tray they are only
        // scanned every 30 seconds for the growing-app alert. Scans run in the background so they
        // never delay the memory numbers.
        if (!_collecting && (_uiVisible || _alerts.WantsProcessSamples)) _ = CollectProcessesAsync(_uiVisible);
    }

    private bool _collecting;

    private async Task CollectProcessesAsync(bool updateUi)
    {
        _collecting = true;
        try
        {
            var samples = await Task.Run(_processService.Collect);
            if (_alerts.RecordProcesses(samples) is { } alert) AlertRaised?.Invoke(alert);
            if (!updateUi) return;
            Processes.Apply(samples);
            Dashboard.UpdateTopApps(Processes.Items);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Process refresh failed");
        }
        finally
        {
            _collecting = false;
        }
    }

    /// <summary>Wake the refresh loop immediately.</summary>
    public void RequestRefresh() => _wake.TrySetResult();

    public void SetUiVisible(bool visible)
    {
        if (_uiVisible == visible) return;
        _uiVisible = visible;

        if (visible)
        {
            RequestRefresh();
        }
        else
        {
            // Hidden in the tray: give back our own memory.
            _ = Task.Delay(1500).ContinueWith(_ =>
            {
                if (_uiVisible) return;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                MemoryCleaner.TrimSelf();
            }, TaskScheduler.Default);
        }
    }

    public void OnRefreshIntervalChanged()
    {
        Dashboard.RefreshHistoryCaption();
        RequestRefresh();
    }

    // ----- Cleaning -----
    public Task QuickCleanAsync() =>
        CleanAsync("Quick clean", CleanAction.FlushModified, CleanAction.PurgeStandby, CleanAction.CombinePages);

    public async Task CleanAsync(string title, params CleanAction[] actions)
    {
        if (IsCleaning) return;
        if (!IsAdmin)
        {
            ShowToast("Administrator rights are required to clean memory.", ToastKind.Warning);
            return;
        }

        IsCleaning = true;
        try
        {
            var before = MemoryService.GetSnapshot();
            var errors = await Task.Run(() =>
            {
                var failures = new List<string>();
                foreach (var action in actions)
                {
                    try
                    {
                        MemoryCleaner.Run(action);
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{action.DisplayName()}: {ex.Message}");
                    }
                }
                return failures;
            });

            await Task.Delay(400); // let the memory counters settle
            var after = MemoryService.GetSnapshot();

            long freeGain = (long)after.Free - (long)before.Free;
            long availableGain = (long)after.Available - (long)before.Available;
            long freed = Math.Max(freeGain, availableGain);
            string freedText = freed > 1024 * 1024 ? Format.Bytes(freed) : "";

            if (errors.Count == actions.Length)
            {
                ShowToast($"{title} failed — {errors[0]}", ToastKind.Error);
            }
            else
            {
                string message = freedText.Length > 0 ? $"{title} complete · {freedText} freed" : $"{title} complete";
                if (errors.Count > 0) message += $" ({errors.Count} step failed)";
                ShowToast(message, errors.Count > 0 ? ToastKind.Warning : ToastKind.Success);
            }

            LastCleanText = $"Last run: {title} at {DateTime.Now:t}" + (freedText.Length > 0 ? $" · {freedText} freed" : "");
            RequestRefresh();
        }
        finally
        {
            IsCleaning = false;
        }
    }

    public void Dispose() => _processService.Dispose();
}
