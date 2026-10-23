using Npgsql;
using PulseSharp;
using PulseSharp.Sync.Server;
using Shop.Api;
using Shop.Api.Admin;
using Shop.Api.Database;
using Shop.Api.Menu;
using Shop.Api.Receipts;
using Shop.Api.Reports;
using Shop.Api.Sales;
using Shop.Api.Stock;
using Shop.Contracts;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Shop")
    ?? throw new InvalidOperationException("Set ConnectionStrings:Shop in appsettings.json.");

builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<ReceiptPrinter>();

// This one call adds events and jobs on the PostgreSQL database the shop already uses.
builder.Services.AddPulseSharpPostgreSql(connectionString, pulse =>
{
    pulse.Node = "cloud";
    pulse.MaxAttempts = 3;
    pulse.RetryDelay = TimeSpan.FromMilliseconds(500);

    // Every handler class listens to the events it implements. Each one keeps its own place in the stream.
    pulse.AddHandler<ProductProjection>();
    pulse.AddHandler<SalesLedger>();
    pulse.AddHandler<StockKeeper>();
    pulse.AddHandler<ReceiptSender>();
    pulse.AddJobHandler<DailyReportJob, DailyReportHandler>();

    // The tills may send their own sales, and may receive only the menu and stock warnings.
    pulse.UseSyncServer(server =>
    {
        foreach (var till in builder.Configuration.GetSection("Tills").GetChildren())
        {
            server.AllowNode(till.Key, till.Value!, Streams.Menu, Streams.StockPrefix);
        }
    });
});

builder.Services.AddHostedService<ShopStartup>();

var app = builder.Build();

await ShopSchema.EnsureCreated(app.Services.GetRequiredService<NpgsqlDataSource>());

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapPulseSharpSync();
app.MapMenu();
app.MapStatus();
app.MapAdmin();

app.Run();

public partial class Program;
