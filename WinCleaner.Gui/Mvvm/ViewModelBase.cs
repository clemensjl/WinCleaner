using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinCleaner.Gui.Mvvm;

/// <summary>Basis für alle ViewModels: minimales INotifyPropertyChanged.</summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Setzt ein Feld und meldet die Änderung; true, wenn sich der Wert geändert hat.</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
