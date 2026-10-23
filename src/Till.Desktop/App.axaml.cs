using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PulseSharp;
using PulseSharp.Sync;
using Till.Desktop.Data;
using Till.Desktop.ViewModels;
using Till.Desktop.Views;

namespace Till.Desktop;

public partial class App(IServiceProvider services) : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(
                    services.GetRequiredService<TillOptions>(),
                    services.GetRequiredService<TillDatabase>(),
                    services.GetRequiredService<IPulse>(),
                    services.GetRequiredService<IPulseSync>()
                ),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
