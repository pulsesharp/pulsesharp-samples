using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using PulseSharp;
using Shop.Contracts;

namespace Shop.Measure;

public sealed record LoadResult(
    int Tills,
    int SalesPerTill,
    TimeSpan EmitTime,
    TimeSpan TotalTime,
    long PeakWorkingSetMb,
    long WorkingSetAfterMb,
    double AverageCpuPercent,
    long CursorRows
);

/// <summary>
/// Several tills (each one a real PulseSharp app on its own SQLite file) sell as fast as they can and sync to the cloud.
/// The load is over when the cloud's three sale handlers have handled every sale.
/// </summary>
public static class Load
{
    public static async Task<LoadResult> Run(RunningApp cloud, int port, string connectionString, int tills, int salesPerTill, bool streamPerSale)
    {
        var folder = Path.Combine(Path.GetTempPath(), "shop-measure-" + Guid.NewGuid().ToString("N"));
        var hosts = new List<IHost>();
        for (var number = 1; number <= tills; number++)
        {
            hosts.Add(await StartTill(folder, $"till-{number}", port));
        }

        var peak = cloud.WorkingSetMb;
        var cpuBefore = cloud.CpuTime;
        var clock = Stopwatch.StartNew();
        using var sampling = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            while (!sampling.IsCancellationRequested)
            {
                peak = Math.Max(peak, cloud.WorkingSetMb);
                await Task.Delay(250);
            }
        });

        await Task.WhenAll(hosts.Select(host => Sell(host, host.Services.GetRequiredService<NodeName>().Value, salesPerTill, streamPerSale)));
        var emitTime = clock.Elapsed;

        var expected = (long)tills * salesPerTill;
        await using var data = NpgsqlDataSource.Create(connectionString);
        while (!await AllHandled(data, expected))
        {
            if (clock.Elapsed > TimeSpan.FromMinutes(10))
            {
                throw new TimeoutException("The cloud did not handle every sale within 10 minutes.");
            }

            await Task.Delay(200);
        }

        var total = clock.Elapsed;
        var cpu = cloud.CpuTime - cpuBefore;
        sampling.Cancel();
        await sampler;

        await Task.Delay(TimeSpan.FromSeconds(10));
        var after = cloud.WorkingSetMb;

        foreach (var host in hosts)
        {
            await host.StopAsync();
            host.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }

        await using var count = data.CreateCommand("SELECT count(*) FROM pulsesharp.cursors");
        var cursorRows = Convert.ToInt64(await count.ExecuteScalarAsync());

        return new LoadResult(tills, salesPerTill, emitTime, total, peak, after, cpu.TotalSeconds / total.TotalSeconds * 100, cursorRows);
    }

    private static async Task<IHost> StartTill(string folder, string name, int port)
    {
        Directory.CreateDirectory(folder);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton(new NodeName(name));
        builder.Services.AddPulseSharpSqlite(Path.Combine(folder, name + ".db"), pulse =>
        {
            pulse.Node = name;
            pulse.UseSync(sync =>
            {
                sync.Server = new Uri($"http://localhost:{port}/pulse/sync");
                sync.ApiKey = "demo-key-" + name;
                sync.Interval = TimeSpan.FromSeconds(1);
                sync.BatchSize = 500;
            });
            pulse.When<ProductAdded>(_ => { });
        });

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private static async Task Sell(IHost till, string name, int count, bool streamPerSale)
    {
        var pulse = till.Services.GetRequiredService<IPulse>();
        for (var number = 0; number < count; number++)
        {
            var sale = new SaleCompleted(
                Guid.NewGuid(),
                name,
                [new SaleLine("coffee", "Coffee", 1, 3.20m), new SaleLine("croissant", "Croissant", 1, 2.90m)],
                6.10m,
                DateTimeOffset.UtcNow
            );
            var stream = streamPerSale
                ? Streams.SalePrefix + sale.SaleId.ToString("N")
                : Streams.Sales(name, DateOnly.FromDateTime(sale.SoldAt.UtcDateTime));
            await pulse.Emit(stream, sale);
        }
    }

    private static async Task<bool> AllHandled(NpgsqlDataSource data, long expected)
    {
        await using var command = data.CreateCommand(
            "SELECT handler_name, sum(position) FROM pulsesharp.cursors WHERE state = 0 AND stream LIKE 'Sale-%' GROUP BY handler_name"
        );
        await using var reader = await command.ExecuteReaderAsync();
        var handled = new Dictionary<string, long>();
        while (await reader.ReadAsync())
        {
            handled[reader.GetString(0)] = Convert.ToInt64(reader.GetValue(1));
        }

        return new[] { "SalesLedger", "StockKeeper", "ReceiptSender" }.All(handler => handled.GetValueOrDefault(handler) >= expected);
    }

    private sealed record NodeName(string Value);
}
