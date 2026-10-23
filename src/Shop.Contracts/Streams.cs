namespace Shop.Contracts;

/// <summary>
/// Stream names both sides agree on. Events in one stream are handled in order, one at a time.
/// Different streams are handled side by side.
/// </summary>
public static class Streams
{
    /// <summary>Every menu change (new products and new prices), in order, so a price never arrives before its product.</summary>
    public const string Menu = "Menu";

    /// <summary>The start of every stock stream.</summary>
    public const string StockPrefix = "Stock-";

    /// <summary>The start of every sale stream.</summary>
    public const string SalePrefix = "Sale-";

    /// <summary>The stream of low-stock warnings for one product.</summary>
    public static string Stock(string sku)
    {
        return StockPrefix + sku;
    }

    /// <summary>
    /// The stream of one till's sales for one day. A stream per sale would keep a cursor row per handler for every sale
    /// forever. A stream per till and day keeps that small, and a till's sales still arrive in the order they were made.
    /// If a handler fails on one sale, that till's later sales for the day wait behind it until you retry or skip it.
    /// </summary>
    public static string Sales(string till, DateOnly day)
    {
        return $"{SalePrefix}{till}-{day:yyyy-MM-dd}";
    }
}
