namespace Shop.Contracts;

/// <summary>A product joined the menu. Emitted by the cloud, received by every till.</summary>
public sealed record ProductAdded(string Sku, string Name, decimal Price, int Stock);

/// <summary>The price of a product changed. Emitted by the cloud, received by every till.</summary>
public sealed record PriceChanged(string Sku, decimal Price);

/// <summary>A product is running out. Emitted by the cloud when stock falls to <see cref="Left"/>, received by every till.</summary>
public sealed record StockLow(string Sku, string Name, int Left);
