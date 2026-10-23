using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PulseSharp;
using Shop.Contracts;
using Till.Desktop.Data;

namespace Till.Desktop;

/// <summary>
/// Everything PulseSharp does for the till, in one place. The till keeps its own SQLite file,
/// so it keeps working with no internet and sends its sales to the cloud when it can.
/// </summary>
public static class TillHost
{
    /// <summary>Raised on a background thread after an event changed what the screen shows.</summary>
    public static event Action? ScreenDataChanged;

    public static IHost Build(TillOptions options, TillDatabase database)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(database);

        builder.Services.AddPulseSharpSqlite(options.DatabasePath, pulse =>
        {
            pulse.Node = options.Name;

            // Sends this till's sales to the cloud and receives the menu and stock warnings. It works offline too.
            pulse.UseSync(sync =>
            {
                sync.Server = new Uri($"{options.CloudAddress}/pulse/sync");
                sync.ApiKey = options.ApiKey;
                sync.Interval = TimeSpan.FromSeconds(3);
            });

            AddScreenHandlers(pulse, database);
        });

        return builder.Build();
    }

    /// <summary>
    /// Each <c>When</c> reacts to one kind of event and writes what the screen shows.
    /// They are a method of their own so tests can run the very same handlers.
    /// </summary>
    public static void AddScreenHandlers(PulseSharpBuilder pulse, TillDatabase database)
    {
        pulse.When<ProductAdded>(product => Changed(() => database.AddProduct(product)));
        pulse.When<PriceChanged>(change => Changed(() => database.ChangePrice(change)));
        pulse.When<StockLow>(warning => Changed(() => database.MarkLow(warning)));
        pulse.When<SaleCompleted>(sale => Changed(() => database.AddSale(sale)));
    }

    private static void Changed(Action change)
    {
        change();
        ScreenDataChanged?.Invoke();
    }
}
