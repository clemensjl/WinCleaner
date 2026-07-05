using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using WinCleaner.Gui.Mvvm;
using WinCleaner.Gui.Services;

namespace WinCleaner.Gui.ViewModels;

/// <summary>
/// Fensterschale: hält die Navigationsseiten, die aktuelle Seite, Statuszeile
/// und Busy-Anzeige. Die <see cref="ShellContext"/> reicht Status/Busy/Logger an
/// die Seiten weiter (UI-Thread-sicher).
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly ShellContext _shell;

    public ObservableCollection<PageViewModelBase> Pages { get; }

    private PageViewModelBase _current = null!;
    public PageViewModelBase Current
    {
        get => _current;
        set { if (SetProperty(ref _current, value)) value?.OnActivated(); }
    }

    private string _status = "Bereit.";
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public string VersionText
    {
        get
        {
            var v = typeof(MainWindowViewModel).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            return "WinCleaner " + v.Split('+')[0];
        }
    }

    public MainWindowViewModel()
    {
        _shell = new ShellContext(
            Application.Current.Dispatcher,
            s => Status = s,
            b => IsBusy = b);

        Pages = new ObservableCollection<PageViewModelBase>
        {
            new DashboardViewModel(_shell),
            new CleanupViewModel(_shell),
            new StorageViewModel(_shell),
            new ProgramsViewModel(_shell),
            new StartupServicesViewModel(_shell),
            new PrivacyViewModel(_shell),
            new SecureDeleteViewModel(_shell),
            new SystemViewModel(_shell),
        };
        Current = Pages[0];
    }
}
