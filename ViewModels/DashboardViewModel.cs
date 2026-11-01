using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using RamManager.Helpers;
using RamManager.Services;

namespace RamManager.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    public const int HistoryCapacity = 60;

    private readonly Queue<double> _history = new(HistoryCapacity);
    private MemorySnapshot _memory;

    public DashboardViewModel(MainViewModel main)
    {
        Main = main;

        Actions =
        [
            new CleanActionViewModel(main, CleanAction.PurgeStandby, "",
                "Clears cached file data so it becomes free memory. Useful after closing large apps or games.",
                "StandbyBrush", "Safe", "SuccessBrush"),
            new CleanActionViewModel(main, CleanAction.FlushModified, "",
                "Writes pending changed pages to disk so that memory can be reused.",
                "ModifiedBrush", "Safe", "SuccessBrush"),
            new CleanActionViewModel(main, CleanAction.CombinePages, "",
                "Merges identical memory pages to save space. Windows 10 and later.",
                "FreeBrush", "Safe", "SuccessBrush"),
            new CleanActionViewModel(main, CleanAction.TrimWorkingSets, "",
                "Forces every process to release memory. Apps may feel slower until they reload it.",
                "InUseBrush", "Use sparingly", "WarningBrush"),
        ];

        RefreshHistoryCaption();
    }

    public MainViewModel Main { get; }

    public IReadOnlyList<CleanActionViewModel> Actions { get; }

    public ObservableCollection<AppUsageViewModel> TopApps { get; } = [];

    // ----- Load -----
    public double LoadPercent => _memory.LoadPercent;
    public string LoadNumber => _memory.LoadPercent.ToString("0");
    public string LoadText => Format.Percent(_memory.LoadPercent);

    public string StatusText => LoadPercent switch
    {
        < 60 => "Healthy",
        < 80 => "Moderate",
        _ => "High",
    };

    private string StatusKey => LoadPercent switch
    {
        < 60 => "SuccessBrush",
        < 80 => "WarningBrush",
        _ => "DangerBrush",
    };

    public Brush StatusBrush => Palette.Get(StatusKey);
    public Brush StatusSoftBrush => Palette.Soft(StatusKey);

    // ----- Totals -----
    public string TotalText => Format.Gb(_memory.Total);
    public string UsedText => Format.Gb(_memory.Used);
    public string AvailableText => Format.Gb(_memory.Available);
    public string CommitText => Format.Gb(_memory.CommitTotal);
    public string CommitLimitText => "of " + Format.Gb(_memory.CommitLimit);

    // ----- Composition -----
    public bool HasBreakdown => _memory.HasBreakdown;
    public double InUse => _memory.InUse;
    public double Modified => _memory.Modified;
    public double Standby => _memory.Standby;
    public double Free => _memory.HasBreakdown ? _memory.Free : _memory.Available;
    public string InUseText => Format.Gb(_memory.InUse);
    public string ModifiedText => HasBreakdown ? Format.Bytes(_memory.Modified) : "—";
    public string StandbyText => HasBreakdown ? Format.Gb(_memory.Standby) : "—";
    public string FreeText => HasBreakdown ? Format.Gb(_memory.Free) : "—";

    // ----- History -----
    public double[] History { get; private set; } = [];

    public int Capacity => HistoryCapacity;

    private string _historyCaption = "";
    public string HistoryCaption { get => _historyCaption; private set => SetProperty(ref _historyCaption, value); }

    public void RefreshHistoryCaption()
    {
        int seconds = HistoryCapacity * Main.Settings.RefreshSeconds;
        HistoryCaption = seconds >= 120 ? $"Last {seconds / 60} minutes" : $"Last {seconds} seconds";
    }

    public void UpdateMemory(MemorySnapshot memory)
    {
        _memory = memory;

        if (_history.Count == HistoryCapacity) _history.Dequeue();
        _history.Enqueue(memory.LoadPercent);
        History = _history.ToArray();

        // Refresh every binding on this view model in one go.
        OnPropertyChanged(string.Empty);
    }

    public void UpdateTopApps(IEnumerable<ProcessItemViewModel> processes)
    {
        var groups = processes
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Items: g.ToList(), Memory: g.Sum(p => (double)p.PrivateWorkingSet)))
            .OrderByDescending(g => g.Memory)
            .Take(6)
            .ToList();

        double max = groups.Count > 0 ? Math.Max(groups[0].Memory, 1) : 1;

        for (int i = 0; i < groups.Count; i++)
        {
            if (i >= TopApps.Count) TopApps.Add(new AppUsageViewModel());
            var (items, memory) = groups[i];
            var representative = items.FirstOrDefault(p => p.Icon is not null) ?? items[0];
            TopApps[i].Update(
                representative.DisplayName,
                items.Count,
                memory,
                memory / max,
                representative.Icon);
        }

        while (TopApps.Count > groups.Count) TopApps.RemoveAt(TopApps.Count - 1);
    }
}

public sealed class AppUsageViewModel : ObservableObject
{
    private string _name = "";
    public string Name { get => _name; private set => SetProperty(ref _name, value); }

    private string _countText = "";
    public string CountText { get => _countText; private set => SetProperty(ref _countText, value); }

    private string _memoryText = "";
    public string MemoryText { get => _memoryText; private set => SetProperty(ref _memoryText, value); }

    private double _fraction;
    public double Fraction { get => _fraction; private set => SetProperty(ref _fraction, value); }

    private ImageSource? _icon;
    public ImageSource? Icon { get => _icon; private set => SetProperty(ref _icon, value); }

    public void Update(string name, int count, double memory, double fraction, ImageSource? icon)
    {
        Name = name;
        CountText = count > 1 ? $"{count} processes" : "1 process";
        MemoryText = Format.Bytes(memory);
        Fraction = fraction;
        Icon = icon;
    }
}

public sealed class CleanActionViewModel
{
    public CleanActionViewModel(MainViewModel main, CleanAction action, string glyph, string description,
        string accentKey, string tagText, string tagKey)
    {
        Title = action.DisplayName();
        Glyph = glyph;
        Description = description;
        AccentBrush = Palette.Get(accentKey);
        AccentSoftBrush = Palette.Soft(accentKey);
        TagText = tagText;
        TagBrush = Palette.Get(tagKey);
        TagSoftBrush = Palette.Soft(tagKey);
        RunCommand = new AsyncRelayCommand(() => main.CleanAsync(Title, action), () => !main.IsCleaning);
    }

    public string Title { get; }
    public string Glyph { get; }
    public string Description { get; }
    public Brush AccentBrush { get; }
    public Brush AccentSoftBrush { get; }
    public string TagText { get; }
    public Brush TagBrush { get; }
    public Brush TagSoftBrush { get; }
    public ICommand RunCommand { get; }
}
