using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PulseSharp;
using Shop.Contracts;

namespace Shop.Api.Sales;

/// <summary>Writes every sale from every till into the <c>sales</c> table, which the daily report reads.</summary>
public sealed class SalesLedger(NpgsqlDataSource dataSource) : IEventHandler<SaleCompleted>
{
    public async Task Handle(SaleCompleted @event, EventContext context, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "INSERT INTO shop.sales (sale_id, till, total, sold_at, lines) VALUES ($1, $2, $3, $4, $5) ON CONFLICT (sale_id) DO NOTHING"
        );
        command.Parameters.AddWithValue(@event.SaleId);
        command.Parameters.AddWithValue(@event.Till);
        command.Parameters.AddWithValue(@event.Total);
        command.Parameters.AddWithValue(@event.SoldAt.UtcDateTime);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = JsonSerializer.Serialize(@event.Lines) });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
