using Microsoft.Data.Sqlite;
using Shop.Contracts;

namespace Till.Desktop.Data;

public sealed record MenuItem(string Sku, string Name, decimal Price, int? LowLeft);

public sealed record RecentSale(string Summary, decimal Total, DateTimeOffset SoldAt);

/// <summary>
/// What the till shows on screen, kept in the same SQLite file as the PulseSharp events.
/// The event handlers fill these tables, so the menu and the recent sales are still there after a restart,
/// even with no internet.
/// </summary>
public sealed class TillDatabase
{
    private readonly string _connectionString;

    public TillDatabase(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        Execute(
            """
            CREATE TABLE IF NOT EXISTS till_products (sku TEXT PRIMARY KEY, name TEXT NOT NULL, price_cents INTEGER NOT NULL, low_left INTEGER NULL);
            CREATE TABLE IF NOT EXISTS till_sales (sale_id TEXT PRIMARY KEY, summary TEXT NOT NULL, total_cents INTEGER NOT NULL, sold_at INTEGER NOT NULL);
            """
        );
    }

    public void AddProduct(ProductAdded product)
    {
        Execute(
            "INSERT INTO till_products (sku, name, price_cents, low_left) VALUES ($sku, $name, $price, NULL) ON CONFLICT (sku) DO NOTHING",
            ("$sku", product.Sku),
            ("$name", product.Name),
            ("$price", ToCents(product.Price))
        );
    }

    public void ChangePrice(PriceChanged change)
    {
        Execute("UPDATE till_products SET price_cents = $price WHERE sku = $sku", ("$sku", change.Sku), ("$price", ToCents(change.Price)));
    }

    public void MarkLow(StockLow warning)
    {
        Execute("UPDATE till_products SET low_left = $left WHERE sku = $sku", ("$sku", warning.Sku), ("$left", warning.Left));
    }

    public void AddSale(SaleCompleted sale)
    {
        var summary = string.Join(", ", sale.Lines.Select(line => line.Quantity == 1 ? line.Name : $"{line.Quantity} x {line.Name}"));
        Execute(
            "INSERT INTO till_sales (sale_id, summary, total_cents, sold_at) VALUES ($id, $summary, $total, $at) ON CONFLICT (sale_id) DO NOTHING",
            ("$id", sale.SaleId.ToString()),
            ("$summary", summary),
            ("$total", ToCents(sale.Total)),
            ("$at", sale.SoldAt.UtcTicks)
        );
    }

    public IReadOnlyList<MenuItem> LoadMenu()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sku, name, price_cents, low_left FROM till_products ORDER BY name";
        using var reader = command.ExecuteReader();
        var items = new List<MenuItem>();
        while (reader.Read())
        {
            items.Add(new MenuItem(reader.GetString(0), reader.GetString(1), reader.GetInt64(2) / 100m, reader.IsDBNull(3) ? null : reader.GetInt32(3)));
        }

        return items;
    }

    public IReadOnlyList<RecentSale> LoadRecentSales(int count)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT summary, total_cents, sold_at FROM till_sales ORDER BY sold_at DESC LIMIT $count";
        command.Parameters.AddWithValue("$count", count);
        using var reader = command.ExecuteReader();
        var sales = new List<RecentSale>();
        while (reader.Read())
        {
            sales.Add(new RecentSale(reader.GetString(0), reader.GetInt64(1) / 100m, new DateTimeOffset(reader.GetInt64(2), TimeSpan.Zero)));
        }

        return sales;
    }

    private static long ToCents(decimal amount)
    {
        return (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var busy = connection.CreateCommand();
        busy.CommandText = "PRAGMA busy_timeout = 5000";
        busy.ExecuteNonQuery();
        return connection;
    }

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }
}
