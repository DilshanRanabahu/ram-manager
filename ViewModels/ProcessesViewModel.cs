using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using RamManager.Helpers;
using RamManager.Services;
using RamManager.Views;

namespace RamManager.ViewModels;

public sealed class ProcessesViewModel : ObservableObject
{
    private static readonly HashSet<string> CriticalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "Secure System", "Memory Compression", "smss.exe", "csrss.exe",
        "wininit.exe", "services.exe", "lsass.exe", "winlogon.exe", "LsaIso.exe", "fontdrvhost.exe",
    };

    private readonly MainViewModel _main;
    private readonly Dictionary<int, ProcessItemViewModel> _byPid = new();
    private readonly ListCollectionView _view;

    public ProcessesViewModel(MainViewModel main)
    {
        _main = main;
        _view = new ListCollectionView(Items) { Filter = Matches, IsLiveSorting = true };
        _view.SortDescriptions.Add(new SortDescription(nameof(ProcessItemViewModel.PrivateWorkingSet), ListSortDirection.Descending));

        TrimSelectedCommand = new RelayCommand(TrimSelected, () => SelectedItem is not null);
        EndSelectedCommand = new RelayCommand(EndSelected, () => SelectedItem is not null);
        OpenLocationCommand = new RelayCommand(OpenLocation, () => SelectedItem?.Path is not null);
        CopyNameCommand = new RelayCommand(CopyName, () => SelectedItem is not null);
        TrimAllCommand = new AsyncRelayCommand(
            () => _main.CleanAsync(CleanAction.TrimWorkingSets.DisplayName(), CleanAction.TrimWorkingSets),
            () => !_main.IsCleaning);
    }

    public ObservableCollection<ProcessItemViewModel> Items { get; } = [];

    public ICollectionView View => _view;

    public ICommand TrimSelectedCommand { get; }
    public ICommand EndSelectedCommand { get; }
    public ICommand OpenLocationCommand { get; }
    public ICommand CopyNameCommand { get; }
    public ICommand TrimAllCommand { get; }

    private ProcessItemViewModel? _selectedItem;
    public ProcessItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value)) return;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectionText));
        }
    }

    public bool HasSelection => SelectedItem is not null;

    public string SelectionText => SelectedItem is { } item
        ? $"{item.Name}  ·  PID {item.Pid}"
        : "Select a process to manage it";

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            _view.Refresh();
            OnPropertyChanged(nameof(HasNoResults));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public bool HasNoResults => Items.Count > 0 && _view.Count == 0;

    private ulong _totalPrivate;
    public string SummaryText
    {
        get
        {
            string summary = $"{Items.Count} processes  ·  {Format.Gb(_totalPrivate)} private memory";
            return string.IsNullOrWhiteSpace(SearchText) ? summary : $"{summary}  ·  {_view.Count} shown";
        }
    }

    private bool Matches(object obj)
    {
        if (string.IsNullOrWhiteSpace(_searchText)) return true;
        var item = (ProcessItemViewModel)obj;
        string query = _searchText.Trim();
        return item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
               || (item.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
               || item.Pid.ToString().StartsWith(query, StringComparison.Ordinal);
    }

    public void Apply(List<ProcessSample> samples)
    {
        var seen = new HashSet<int>(samples.Count);
        ulong max = 1, total = 0;
        foreach (var s in samples)
        {
            max = Math.Max(max, s.PrivateWorkingSet);
            total += s.PrivateWorkingSet;
        }

        foreach (var sample in samples)
        {
            seen.Add(sample.Pid);
            if (_byPid.TryGetValue(sample.Pid, out var item) && item.CreateTime == sample.CreateTime)
            {
                item.Update(sample, max);
                continue;
            }

            if (item is not null) Items.Remove(item); // PID was reused by a new process
            item = new ProcessItemViewModel(sample, CriticalProcesses.Contains(sample.Name) || sample.Pid <= 4);
            item.Update(sample, max);
            _byPid[sample.Pid] = item;
            Items.Add(item);
        }

        foreach (var pid in _byPid.Keys.Where(pid => !seen.Contains(pid)).ToList())
        {
            Items.Remove(_byPid[pid]);
            _byPid.Remove(pid);
        }

        if (SelectedItem is not null && !seen.Contains(SelectedItem.Pid)) SelectedItem = null;

        _totalPrivate = total;
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasNoResults));
    }

    // ----- Actions -----
    private void TrimSelected()
    {
        if (SelectedItem is not { } item) return;
        try
        {
            ulong before = item.WorkingSet;
            MemoryCleaner.TrimProcess(item.Pid);
            _main.ShowToast($"Trimmed {item.Name}" + (before > 0 ? $" (was {Format.Bytes(before)} working set)" : ""));
            _main.RequestRefresh();
        }
        catch (Exception ex)
        {
            _main.ShowToast($"Couldn't trim {item.Name}: {ex.Message}", ToastKind.Error);
        }
    }

    private void EndSelected()
    {
        if (SelectedItem is not { } item) return;

        if (item.IsCritical || item.Pid == Environment.ProcessId)
        {
            _main.ShowToast($"{item.Name} is a critical system process and can't be ended.", ToastKind.Warning);
            return;
        }

        if (_main.Settings.ConfirmEndProcess &&
            !ConfirmDialog.Ask(
                $"End {item.Name}?",
                "Any unsaved data in this program will be lost. If it is a system process, Windows may become unstable.",
                "End process"))
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(item.Pid);
            if (process.StartTime.ToFileTimeUtc() != item.CreateTime)
            {
                _main.ShowToast($"{item.Name} has already exited.", ToastKind.Info);
                return;
            }
            process.Kill();
            _main.ShowToast($"Ended {item.Name}");
            _main.RequestRefresh();
        }
        catch (ArgumentException)
        {
            _main.ShowToast($"{item.Name} has already exited.", ToastKind.Info);
        }
        catch (Exception ex)
        {
            _main.ShowToast($"Couldn't end {item.Name}: {ex.Message}", ToastKind.Error);
        }
    }

    private void OpenLocation()
    {
        if (SelectedItem?.Path is not { } path) return;
        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex)
        {
            _main.ShowToast($"Couldn't open location: {ex.Message}", ToastKind.Error);
        }
    }

    private void CopyName()
    {
        if (SelectedItem is not { } item) return;
        try
        {
            Clipboard.SetText(item.Name);
            _main.ShowToast($"Copied \"{item.Name}\"", ToastKind.Info);
        }
        catch
        {
            // Clipboard can be locked by another app; not worth an error.
        }
    }
}

public sealed class ProcessItemViewModel : ObservableObject
{
    public ProcessItemViewModel(ProcessSample sample, bool isCritical)
    {
        Pid = sample.Pid;
        CreateTime = sample.CreateTime;
        Name = sample.Name;
        Path = sample.Meta.Path;
        Description = sample.Meta.Description;
        Icon = sample.Meta.Icon;
        IsCritical = isCritical;
    }

    public int Pid { get; }
    public long CreateTime { get; }
    public string Name { get; }
    public string? Path { get; }
    public string? Description { get; }
    public ImageSource? Icon { get; }
    public bool IsCritical { get; }

    public string DisplayName => Description ?? (Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Name[..^4] : Name);
    public string ToolTipText => Path ?? Name;

    private ulong _privateWorkingSet;
    public ulong PrivateWorkingSet { get => _privateWorkingSet; private set => SetProperty(ref _privateWorkingSet, value); }

    private ulong _workingSet;
    public ulong WorkingSet { get => _workingSet; private set => SetProperty(ref _workingSet, value); }

    private ulong _commit;
    public ulong Commit { get => _commit; private set => SetProperty(ref _commit, value); }

    private double _cpu;
    public double Cpu { get => _cpu; private set => SetProperty(ref _cpu, value); }

    private double _memoryFraction;
    public double MemoryFraction { get => _memoryFraction; private set => SetProperty(ref _memoryFraction, value); }

    public string PrivateWorkingSetText => Format.Mb(PrivateWorkingSet);
    public string WorkingSetText => Format.Mb(WorkingSet);
    public string CommitText => Format.Mb(Commit);
    public string CpuText => Cpu < 0.05 ? "0%" : Cpu.ToString("0.0") + "%";

    public void Update(ProcessSample sample, ulong maxPrivate)
    {
        if (SetProperty(ref _privateWorkingSet, sample.PrivateWorkingSet, nameof(PrivateWorkingSet)))
            OnPropertyChanged(nameof(PrivateWorkingSetText));
        if (SetProperty(ref _workingSet, sample.WorkingSet, nameof(WorkingSet)))
            OnPropertyChanged(nameof(WorkingSetText));
        if (SetProperty(ref _commit, sample.Commit, nameof(Commit)))
            OnPropertyChanged(nameof(CommitText));

        double cpu = Math.Round(sample.Cpu, 1);
        if (Math.Abs(cpu - _cpu) > 0.01)
        {
            Cpu = cpu;
            OnPropertyChanged(nameof(CpuText));
        }

        MemoryFraction = Math.Round((double)sample.PrivateWorkingSet / maxPrivate, 3);
    }
}
