using Npgsql;
using PulseSharp;
using Shop.Contracts;

namespace Shop.Api.Menu;

public static class MenuEndpoints
{
    public static void MapMenu(this IEndpointRouteBuilder app)
    {
        app.MapGet("/menu", async (NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
        {
            await using var command = dataSource.CreateCommand("SELECT sku, name, price, stock FROM shop.products ORDER BY name");
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var products = new List<ProductAdded>();
            while (await reader.ReadAsync(cancellationToken))
            {
                products.Add(new ProductAdded(reader.GetString(0), reader.GetString(1), reader.GetDecimal(2), reader.GetInt32(3)));
            }

            return products;
        });

        // The API only emits an event. The handler (ProductProjection) updates the table a moment later.
        app.MapPost("/menu", async (ProductAdded product, IPulse pulse, CancellationToken cancellationToken) =>
        {
            await pulse.Emit(Streams.Menu, product, cancellationToken);
            return Results.Accepted();
        });

        app.MapPut("/menu/{sku}/price", async (string sku, NewPrice body, IPulse pulse, CancellationToken cancellationToken) =>
        {
            await pulse.Emit(Streams.Menu, new PriceChanged(sku, body.Price), cancellationToken);
            return Results.Accepted();
        });
    }

    public sealed record NewPrice(decimal Price);
}
