namespace Shop.Contracts;

/// <summary>One line of a sale: some units of one product at the price the customer paid.</summary>
public sealed record SaleLine(string Sku, string Name, int Quantity, decimal UnitPrice);

/// <summary>A customer paid at a till. Emitted by the till, even while it is offline, and sent to the cloud by sync.</summary>
public sealed record SaleCompleted(Guid SaleId, string Till, IReadOnlyList<SaleLine> Lines, decimal Total, DateTimeOffset SoldAt);
