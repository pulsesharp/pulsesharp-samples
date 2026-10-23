using Npgsql;
using PulseSharp;
using Shop.Api.Receipts;

namespace Shop.Api.Sales;

/// <summary>One call with everything the little console page at <c>/</c> shows.</summary>
public static class StatusEndpoints
{
    public static void MapStatus(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok());

        app.MapGet("/status", async (NpgsqlDataSource dataSource, IPulseControl control, ReceiptPrinter printer, CancellationToken cancellationToken) =>
        {
            var products = new List<object>();
            await using (var command = dataSource.CreateCommand("SELECT sku, name, price, stock FROM shop.products ORDER BY name"))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    products.Add(new { Sku = reader.GetString(0), Name = reader.GetString(1), Price = reader.GetDecimal(2), Stock = reader.GetInt32(3) });
                }
            }

            var recentSales = new List<object>();
            var salesCount = 0L;
            var revenue = 0m;
            await using (var command = dataSource.CreateCommand("SELECT sale_id, till, total, sold_at FROM shop.sales ORDER BY sold_at DESC LIMIT 8"))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    recentSales.Add(new { SaleId = reader.GetGuid(0), Till = reader.GetString(1), Total = reader.GetDecimal(2), SoldAt = reader.GetDateTime(3) });
                }
            }

            await using (var command = dataSource.CreateCommand("SELECT count(*), coalesce(sum(total), 0) FROM shop.sales"))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                salesCount = reader.GetInt64(0);
                revenue = reader.GetDecimal(1);
            }

            var cursors = await control.GetCursors(cancellationToken: cancellationToken);
            var problems = cursors.Where(cursor => cursor.State != CursorState.Active).Select(cursor => new
            {
                cursor.HandlerName,
                cursor.Stream,
                State = cursor.State.ToString(),
                cursor.Attempts,
                cursor.LastError,
            });

            return new
            {
                Products = products,
                SalesCount = salesCount,
                Revenue = revenue,
                RecentSales = recentSales,
                PrinterBroken = printer.IsBroken,
                Receipts = printer.Printed.Reverse().Take(5),
                Problems = problems,
                Streams = cursors.Count,
            };
        });
    }
}
