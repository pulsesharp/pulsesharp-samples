using Avalonia;
using Microsoft.Extensions.Hosting;
using Till.Desktop;
using Till.Desktop.Data;

var options = TillOptions.FromArguments(args);
var host = TillHost.Build(options, new TillDatabase(options.DatabasePath));
await host.StartAsync();

AppBuilder.Configure(() => new App(host.Services))
    .UsePlatformDetect()
    .StartWithClassicDesktopLifetime(args);

await host.StopAsync();
host.Dispose();
