using System.Windows;

namespace WinCleaner.Gui.Services;

/// <summary>Kleine, einheitliche Bestätigungsdialoge (Vorschau-zuerst-Sicherheitsmodell).</summary>
public static class Dialogs
{
    /// <summary>Normale Ja/Nein-Bestätigung vor einer umkehrbaren Aktion.</summary>
    public static bool Confirm(string message, string title = "WinCleaner")
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
           == MessageBoxResult.Yes;

    /// <summary>Warn-Bestätigung für unwiderrufliche Aktionen (shred/wipe).</summary>
    public static bool ConfirmDanger(string message, string title = "Unwiderruflich")
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
           == MessageBoxResult.Yes;

    public static void Info(string message, string title = "WinCleaner")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
