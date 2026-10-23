using Microsoft.Extensions.DependencyInjection;
using PulseSharp;
using Shop.Api.Receipts;
using Shop.Contracts;

namespace Shop.Tests;

public class ShopApiTests(ShopCloud cloud) : IClassFixture<ShopCloud>
{
    private const string NoPostgres = "PostgreSQL is not running. Start it with: docker compose up -d";

    private async Task<int> StockOf(string sku)
    {
        var menu = (await cloud.Client.GetFromJsonAsync<List<ProductAdded>>("/menu"))!;
        return menu.Single(product => product.Sku == sku).Stock;
    }

    private async Task Receive(SaleCompleted sale)
    {
        await cloud.Pulse.Emit(Sales.StreamOf(sale), sale);
    }

    [Fact]
    public async Task ASaleTakesItemsOutOfStockAndWarnsOnlyWhenStockRunsLow()
    {
        Assert.SkipUnless(cloud.Available, NoPostgres);

        await Receive(Sales.Sale("till-1", "croissant", "Croissant", 7, 2.90m));
        await ShopCloud.Eventually(async () => await StockOf("croissant") == 5, "stock to fall to 5");
        await ShopCloud.Eventually(async () => await cloud.Count("SELECT count(*) FROM pulsesharp.events WHERE event_name = 'StockLow'") == 1, "one warning");

        await Receive(Sales.Sale("till-1", "croissant", "Croissant", 1, 2.90m));
        await ShopCloud.Eventually(async () => await StockOf("croissant") == 4, "stock to fall to 4");

        Assert.Equal(1, await cloud.Count("SELECT count(*) FROM pulsesharp.events WHERE event_name = 'StockLow'"));
    }

    [Fact]
    public async Task TheSameSaleArrivingTwiceIsCountedOnce()
    {
        Assert.SkipUnless(cloud.Available, NoPostgres);
        var sale = Sales.Sale("till-2", "coffee", "Coffee", 1, 3.20m);

        await cloud.Pulse.Emit("Sale-first-delivery", sale);
        await cloud.Pulse.Emit("Sale-second-delivery", sale);
        await ShopCloud.Eventually(async () =>
        {
            var cursors = await cloud.Control.GetCursors("StockKeeper");
            return cursors.Count(cursor => cursor.Stream.EndsWith("-delivery") && cursor.Position >= 1) == 2;
        }, "both deliveries to be handled");

        Assert.Equal(19, await StockOf("coffee"));
        Assert.Equal(1, await cloud.Count($"SELECT count(*) FROM shop.sales WHERE sale_id = '{sale.SaleId}'"));
    }

    [Fact]
    public async Task ABrokenPrinterHoldsBackThatTillsReceiptsInOrderAndLeavesOtherTillsAlone()
    {
        Assert.SkipUnless(cloud.Available, NoPostgres);
        var printer = cloud.Services.GetRequiredService<ReceiptPrinter>();
        var first = Sales.Sale("till-a", "latte", "Latte", 1, 3.80m);
        var second = Sales.Sale("till-a", "latte", "Latte", 2, 3.80m);
        var otherTill = Sales.Sale("till-b", "latte", "Latte", 3, 3.80m);

        printer.IsBroken = true;
        await Receive(first);
        await ShopCloud.Eventually(async () =>
        {
            var cursors = await cloud.Control.GetCursors("ReceiptSender");
            return cursors.Any(cursor => cursor.Stream == Sales.StreamOf(first) && cursor.State == CursorState.Failed);
        }, "the receipt handler to give up on the first sale");

        await Receive(second);
        printer.IsBroken = false;
        await Receive(otherTill);

        await ShopCloud.Eventually(() => Task.FromResult(printer.Printed.Any(receipt => receipt.StartsWith("till-b"))), "the other till's receipt");
        Assert.DoesNotContain(printer.Printed, receipt => receipt.StartsWith("till-a"));
        await ShopCloud.Eventually(async () => await cloud.Count($"SELECT count(*) FROM shop.sales WHERE till = 'till-a'") == 2, "the ledger to record both sales");

        Assert.Equal(1, await cloud.Control.RetryFailed());

        await ShopCloud.Eventually(() => Task.FromResult(printer.Printed.Count(receipt => receipt.StartsWith("till-a")) == 2), "both receipts of till-a");
        var receipts = printer.Printed.Where(receipt => receipt.StartsWith("till-a")).ToList();
        Assert.Equal("till-a: 1 x Latte = 3.80", receipts[0]);
        Assert.Equal("till-a: 2 x Latte = 7.60", receipts[1]);
    }

    [Fact]
    public async Task TheDailyReportAddsUpTheSalesOfThatDay()
    {
        Assert.SkipUnless(cloud.Available, NoPostgres);
        var morning = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

        await Receive(Sales.Sale("till-1", "tea", "Tea", 2, 2.50m, morning));
        await Receive(Sales.Sale("till-2", "water", "Water", 1, 1.50m, morning.AddHours(1)));
        await ShopCloud.Eventually(async () => await cloud.Count("SELECT count(*) FROM shop.sales WHERE sold_at < '2026-01-16'") == 2, "both sales in the ledger");

        var response = await cloud.Client.PostAsync("/reports/run?day=2026-01-15", content: null);
        response.EnsureSuccessStatusCode();

        await ShopCloud.Eventually(async () => await cloud.Count("SELECT count(*) FROM shop.daily_reports WHERE day = '2026-01-15' AND sales = 2 AND revenue = 6.50") == 1, "the report");
    }
}
