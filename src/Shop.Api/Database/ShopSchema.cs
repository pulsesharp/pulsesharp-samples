using Npgsql;

namespace Shop.Api.Database;

/// <summary>
/// The shop's own tables, next to the tables PulseSharp keeps in its <c>pulsesharp</c> schema.
/// They are read models: they can always be rebuilt by replaying the events.
/// </summary>
public static class ShopSchema
{
    private const string Sql = """
        CREATE SCHEMA IF NOT EXISTS shop;

        CREATE TABLE IF NOT EXISTS shop.products (
            sku   text PRIMARY KEY,
            name  text NOT NULL,
            price numeric(10, 2) NOT NULL,
            stock integer NOT NULL
        );

        CREATE TABLE IF NOT EXISTS shop.sales (
            sale_id uuid PRIMARY KEY,
            till    text NOT NULL,
            total   numeric(10, 2) NOT NULL,
            sold_at timestamptz NOT NULL,
            lines   jsonb NOT NULL
        );

        CREATE TABLE IF NOT EXISTS shop.stock_applied (
            sale_id uuid PRIMARY KEY
        );

        CREATE TABLE IF NOT EXISTS shop.daily_reports (
            day        date PRIMARY KEY,
            sales      integer NOT NULL,
            revenue    numeric(12, 2) NOT NULL,
            by_till    jsonb NOT NULL,
            created_at timestamptz NOT NULL
        );
        """;

    /// <summary>Creates the tables when they are missing. Safe to run from several instances at once.</summary>
    public static async Task EnsureCreated(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext('shop.schema'));" + Sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
