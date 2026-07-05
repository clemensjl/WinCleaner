using System.Windows;
using Microsoft.Win32;
using WinCleaner.Gui.ViewModels;

namespace WinCleaner.Gui;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Farb-Theme passend zum Windows-App-Theme wählen und einmischen.
        var themeUri = UsesLightTheme()
            ? new Uri("Themes/Light.xaml", UriKind.Relative)
            : new Uri("Themes/Dark.xaml", UriKind.Relative);
        var theme = (ResourceDictionary)LoadComponent(themeUri);
        Resources.MergedDictionaries.Insert(0, theme);

        var window = new MainWindow { DataContext = new MainWindowViewModel() };
        window.Show();
    }

    /// <summary>Liest, ob Windows das helle App-Theme verwendet (Standard: dunkel).</summary>
    private static bool UsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i != 0;
        }
        catch { return false; }
    }
}
