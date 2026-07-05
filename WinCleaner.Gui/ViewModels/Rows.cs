using WinCleaner.Core;
using WinCleaner.Gui.Mvvm;
using WinCleaner.SystemTools;

namespace WinCleaner.Gui.ViewModels;

/// <summary>Eine Junk-Kategorie mit Auswahl-Häkchen.</summary>
public sealed class JunkRow : ViewModelBase
{
    public JunkItem Item { get; }
    public JunkRow(JunkItem item)
    {
        Item = item;
        _isSelected = item.Safety == Safety.Safe; // Safe standardmäßig angehakt
    }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public string Category => Item.Category;
    public string Path => Item.Path;
    public long Bytes => Item.TotalBytes;
    public string SizeText => DiskAnalyzer.FormatSize(Item.TotalBytes);
    public int FileCount => Item.FileCount;
    public bool IsSafe => Item.Safety == Safety.Safe;
    public string SafetyText => Item.Safety switch
    {
        Safety.Safe    => "Sicher",
        Safety.Caution => "Vorsicht",
        _              => "Gefahr"
    };
}

/// <summary>Ein Eintrag der Speicheranalyse.</summary>
public sealed class DiskRow
{
    public DiskRow(DiskEntry e, long total)
    {
        TypeText = e.IsDir ? "Ordner" : "Datei";
        Path = e.Path;
        SizeText = DiskAnalyzer.FormatSize(e.Bytes);
        PercentText = total > 0 ? $"{e.Bytes * 100.0 / total:N1}%" : "-";
        Files = e.Files;
    }
    public string TypeText { get; }
    public string Path { get; }
    public string SizeText { get; }
    public string PercentText { get; }
    public int Files { get; }
}

/// <summary>Eine Duplikatgruppe (Anzeige).</summary>
public sealed class DupRow
{
    public DupRow(DuplicateGroup g)
    {
        Header = $"{g.Files.Count} Kopien · {DiskAnalyzer.FormatSize(g.TotalBytes)}";
        Files = string.Join("\n", g.Files);
    }
    public string Header { get; }
    public string Files { get; }
}

/// <summary>Ein installiertes Programm.</summary>
public sealed class ProgramRow
{
    public ProgramRow(InstalledProgram p)
    {
        Name = p.DisplayName;
        Version = p.DisplayVersion ?? "";
        Publisher = p.Publisher ?? "";
        SizeText = p.EstimatedSizeKb > 0
            ? DiskAnalyzer.FormatSize(p.EstimatedSizeKb * 1024)
            : "";
    }
    public string Name { get; }
    public string Version { get; }
    public string Publisher { get; }
    public string SizeText { get; }
}

/// <summary>Ein Autostart-Eintrag.</summary>
public sealed class StartupRow
{
    public StartupRow(StartupItem i)
    {
        Name = i.Name;
        Source = i.Source;
        Path = i.Path;
        StateText = i.Enabled ? "Aktiv" : "Aus";
        Enabled = i.Enabled;
    }
    public string Name { get; }
    public string Source { get; }
    public string Path { get; }
    public string StateText { get; }
    public bool Enabled { get; }
}

/// <summary>Ein Privacy-Tweak mit aktuellem Status.</summary>
public sealed class PrivacyRow
{
    public PrivacyRow(string description, string profile, string hive, TweakStatus status)
    {
        Description = description;
        Profile = profile;
        Hive = hive;
        Applied = status == TweakStatus.Applied;
        StatusText = status switch
        {
            TweakStatus.Applied    => "aktiv",
            TweakStatus.NotApplied => "nicht aktiv",
            _                      => "unbekannt"
        };
    }
    public string Description { get; }
    public string Profile { get; }
    public string Hive { get; }
    public bool Applied { get; }
    public string StatusText { get; }
}
