using Microsoft.Extensions.Logging.Abstractions;
using PulseSharp.Testing;
using Shop.Api.Receipts;

namespace Shop.Tests;

public class ReceiptSenderTests
{
    private readonly ReceiptPrinter _printer = new(NullLogger<ReceiptPrinter>.Instance);

    [Fact]
    public async Task APaidSaleGetsAReceipt()
    {
        var handler = new ReceiptSender(_printer);

        await handler.Handle(Sales.Sale("till-1", "coffee", "Coffee", 2, 3.20m));

        Assert.Equal("till-1: 2 x Coffee = 6.40", Assert.Single(_printer.Printed));
    }

    [Fact]
    public async Task ABrokenPrinterMakesTheHandlerFailSoPulseSharpRetries()
    {
        _printer.IsBroken = true;
        var handler = new ReceiptSender(_printer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(Sales.Sale("till-1", "tea", "Tea", 1, 2.50m)));

        Assert.Empty(_printer.Printed);
    }
}
