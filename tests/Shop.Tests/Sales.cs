using Shop.Contracts;

namespace Shop.Tests;

/// <summary>Builds the sales the tests use.</summary>
public static class Sales
{
    public static string StreamOf(SaleCompleted sale)
    {
        return Streams.Sales(sale.Till, DateOnly.FromDateTime(sale.SoldAt.UtcDateTime));
    }

    public static SaleCompleted Sale(string till, string sku, string name, int quantity, decimal price, DateTimeOffset? soldAt = null)
    {
        return new SaleCompleted(
            Guid.NewGuid(),
            till,
            [new SaleLine(sku, name, quantity, price)],
            quantity * price,
            soldAt ?? DateTimeOffset.UtcNow
        );
    }
}
