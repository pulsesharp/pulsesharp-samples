using Npgsql;
using PulseSharp;
using Shop.Contracts;

namespace Shop.Api.Menu;

/// <summary>
/// Keeps the <c>products</c> table up to date from menu events. One handler class can listen to several event types.
/// </summary>
public sealed class ProductProjection(NpgsqlDataSource dataSource) : IEventHandler<ProductAdded>, IEventHandler<PriceChanged>
{
    public async Task Handle(ProductAdded @event, EventContext context, CancellationToken cancellationToken)
    {
        // ON CONFLICT DO NOTHING makes this safe to repeat: PulseSharp delivers each event at least once.
        await using var command = dataSource.CreateCommand(
            "INSERT INTO shop.products (sku, name, price, stock) VALUES ($1, $2, $3, $4) ON CONFLICT (sku) DO NOTHING"
        );
        command.Parameters.AddWithValue(@event.Sku);
        command.Parameters.AddWithValue(@event.Name);
        command.Parameters.AddWithValue(@event.Price);
        command.Parameters.AddWithValue(@event.Stock);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task Handle(PriceChanged @event, EventContext context, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("UPDATE shop.products SET price = $2 WHERE sku = $1");
        command.Parameters.AddWithValue(@event.Sku);
        command.Parameters.AddWithValue(@event.Price);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
