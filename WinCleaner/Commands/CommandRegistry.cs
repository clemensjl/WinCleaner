namespace WinCleaner.Commands;

/// <summary>
/// Findet alle <see cref="ICommand"/>-Implementierungen dieser Assembly per
/// Reflection (parameterloser Konstruktor erforderlich). Dadurch ist jeder
/// Befehl eine eigenständige Datei ohne zentrale Registrierung – neue Befehle
/// lassen sich konfliktfrei hinzufügen.
/// </summary>
public static class CommandRegistry
{
    private static readonly Lazy<IReadOnlyList<ICommand>> _all = new(Discover);

    public static IReadOnlyList<ICommand> All => _all.Value;

    public static ICommand? Find(string name) =>
        All.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<ICommand> Discover() =>
        typeof(CommandRegistry).Assembly
            .GetTypes()
            .Where(t => typeof(ICommand).IsAssignableFrom(t)
                        && t is { IsAbstract: false, IsInterface: false }
                        && t.GetConstructor(Type.EmptyTypes) != null)
            .Select(t => (ICommand)Activator.CreateInstance(t)!)
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
}
