using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace CalmMouse.Win.SettingsUI;

/// <summary>INotifyPropertyChanged with a Set helper.</summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Sets <paramref name="field"/> and raises PropertyChanged, if the value changed. True if it did.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) { return false; }
        field = value;
        Raise(name);
        return true;
    }
}

/// <summary>A command that runs an action. Always enabled.</summary>
public sealed class Command(Action<object?> run) : ICommand
{
    public Command(Action run) : this(_ => run()) { }

    // Always enabled, so nothing ever changes; the accessors keep the interface without a warning.
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => run(parameter);
}
