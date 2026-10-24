using PulseSharp.Testing;
using Shop.Contracts;
using Till.Desktop;
using Till.Desktop.Data;

namespace Shop.Tests;

/// <summary>Runs the till's real handlers on the real engine (in memory) and looks at what the screen would show.</summary>
public sealed class TillScreenTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "shop-tests-" + Guid.NewGuid().ToString("N"));
    private readonly TillDatabase _database;

    public TillScreenTests()
    {
        _database = new TillDatabase(Path.Combine(_folder, "till.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task TheMenuFromTheCloudShowsUpWithItsNewPrice()
    {
        await using var pulse = await PulseTestHost.Start(builder => TillHost.AddScreenHandlers(builder, _database));

        await pulse.Emit(Streams.Menu, new ProductAdded("tea", "Tea", 2.50m, 20));
        await pulse.Emit(Streams.Menu, new PriceChanged("tea", 2.80m));
        await pulse.WaitUntilIdle();

        var tea = Assert.Single(_database.LoadMenu());
        Assert.Equal(2.80m, tea.Price);
        pulse.NothingFailed();
    }

    [Fact]
    public async Task ALowStockWarningMarksTheProduct()
    {
        await using var pulse = await PulseTestHost.Start(builder => TillHost.AddScreenHandlers(builder, _database));
        await pulse.Given(Streams.Menu, new ProductAdded("croissant", "Croissant", 2.90m, 12));

        await pulse.Emit(Streams.Stock("croissant"), new StockLow("croissant", "Croissant", 5));
        await pulse.WaitUntilIdle();

        Assert.Equal(5, Assert.Single(_database.LoadMenu()).LowLeft);
    }

    [Fact]
    public async Task ASaleAppearsInRecentSales()
    {
        await using var pulse = await PulseTestHost.Start(builder => TillHost.AddScreenHandlers(builder, _database));
        var sale = Sales.Sale("till-1", "coffee", "Coffee", 2, 3.20m);

        await pulse.Emit(Streams.Sales("till-1", DateOnly.FromDateTime(sale.SoldAt.UtcDateTime)), sale);
        await pulse.WaitUntilIdle();

        var recent = Assert.Single(_database.LoadRecentSales(5));
        Assert.Equal("2 x Coffee", recent.Summary);
        Assert.Equal(6.40m, recent.Total);
    }
}
