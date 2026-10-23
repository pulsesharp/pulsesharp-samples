using Npgsql;
using PulseSharp;
using Shop.Contracts;

namespace Shop.Api.Stock;

/// <summary>
/// Takes sold items out of stock and warns every till when a product runs low.
/// The stock change and the warning are saved in one database transaction, so you never get one without the other.
/// </summary>
public sealed class StockKeeper(NpgsqlDataSource dataSource, IPulse pulse) : IEventHandler<SaleCompleted>
{
    private const int LowStockLevel = 5;

    public async Task Handle(SaleCompleted @event, EventContext context, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // A sale that was already counted (for example after a crash) is skipped, so stock is never taken out twice.
        await using (var applied = new NpgsqlCommand("INSERT INTO shop.stock_applied (sale_id) VALUES ($1) ON CONFLICT DO NOTHING", connection, transaction))
        {
            applied.Parameters.AddWithValue(@event.SaleId);
            if (await applied.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                return;
            }
        }

        foreach (var line in @event.Lines)
        {
            await TakeFromStock(connection, transaction, line, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task TakeFromStock(NpgsqlConnection connection, NpgsqlTransaction transaction, SaleLine line, CancellationToken cancellationToken)
    {
        string name;
        int left;
        await using (var command = new NpgsqlCommand("UPDATE shop.products SET stock = stock - $2 WHERE sku = $1 RETURNING name, stock", connection, transaction))
        {
            command.Parameters.AddWithValue(line.Sku);
            command.Parameters.AddWithValue(line.Quantity);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return;
            }

            name = reader.GetString(0);
            left = reader.GetInt32(1);
        }

        var crossedTheLevel = left <= LowStockLevel && left + line.Quantity > LowStockLevel;
        if (crossedTheLevel)
        {
            // Passing the transaction makes the event part of the same commit (the "outbox" pattern).
            await pulse.Emit(Streams.Stock(line.Sku), new StockLow(line.Sku, name, left), new EmitOptions { Transaction = transaction }, cancellationToken);
        }
    }
}
