using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Till.Desktop.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>The smallest possible button command: runs a method when clicked.</summary>
public sealed class RelayCommand(Func<Task> run) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public RelayCommand(Action run)
        : this(() =>
        {
            run();
            return Task.CompletedTask;
        })
    {
    }

    public bool CanExecute(object? parameter)
    {
        return true;
    }

    public async void Execute(object? parameter)
    {
        await run();
    }
}
