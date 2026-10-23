using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Npgsql;
using Shop.Measure;

// Usage: dotnet run -c Release --project tools/Shop.Measure -- [--tills 5] [--sales 2000] [--idle 30]
var tills = IntArgument("--tills", 5);
var salesPerTill = IntArgument("--sales", 2000);
var idleSeconds = IntArgument("--idle", 30);

const int Port = 5199;
var repo = FindRepoRoot();
var server = Environment.GetEnvironmentVariable("SHOP_MEASURE_POSTGRES") ?? "Host=localhost;Port=55433;Username=shop;Password=shop";
var connectionString = server + ";Database=shop_measure";

Console.WriteLine("Publishing Shop.Api and the baseline app (Release)...");
var shopDll = Publish("src/Shop.Api", "shop-api", "Shop.Api.dll");
var baselineDll = Publish("tools/Baseline.Api", "baseline", "Baseline.Api.dll");

await RecreateDatabase(server);
var settings = new Dictionary<string, string> { ["ConnectionStrings__Shop"] = connectionString };
for (var number = 1; number <= tills; number++)
{
    settings[$"Tills__till-{number}"] = $"demo-key-till-{number}";
}

Console.WriteLine("Baseline: ASP.NET Core with no PulseSharp...");
var baseline = await MeasureStartAndIdle(baselineDll, new Dictionary<string, string>(), warmRuns: 5);
Console.WriteLine($"  start-up {Ms(baseline.Startup)}, idle {baseline.IdleMb} MB");

Console.WriteLine("Shop.Api on PostgreSQL: first start (creates the tables)...");
var firstStart = TimeSpan.Zero;
using (var first = await RunningApp.Start(shopDll, Port, settings))
{
    Console.WriteLine($"  first start {Ms(first.StartupTime)}");
    firstStart = first.StartupTime;
}

await ReleaseLeases();

Console.WriteLine("Shop.Api: warm starts and idle memory...");
var shop = await MeasureStartAndIdle(shopDll, settings, warmRuns: 5, keepRunning: true);
Console.WriteLine($"  start-up {Ms(shop.Startup)}, idle {shop.IdleMb} MB");

shop.App!.Dispose();

var results = new Dictionary<string, LoadResult>();
foreach (var (label, streamPerSale) in new[] { ("one stream per till and day (what Shop.Api's tills do)", false), ("one stream per sale", true) })
{
    Console.WriteLine($"Load, {label}: {tills} tills x {salesPerTill} sales...");
    await RecreateDatabase(server);
    using var cloud = await RunningApp.Start(shopDll, Port, settings);
    results[label] = await Load.Run(cloud, Port, connectionString, tills, salesPerTill, streamPerSale);
}

var sales = tills * salesPerTill;
var pulseDlls = new DirectoryInfo(Path.GetDirectoryName(shopDll)!).GetFiles("PulseSharp*.dll");

var report = new StringBuilder();
report.AppendLine("| What | Result |");
report.AppendLine("|---|---|");
report.AppendLine($"| Machine | {Environment.ProcessorCount} logical cores, {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024 * 1024.0):0.#} GB RAM, {RuntimeInformation.OSDescription}, .NET {Environment.Version} |");
report.AppendLine("| Database | PostgreSQL 17 in Docker on the same machine |");
report.AppendLine($"| Start-up, plain ASP.NET Core (median of 5, to first `/health`) | {Ms(baseline.Startup)} |");
report.AppendLine($"| Start-up, Shop.Api with PulseSharp (median of 5) | {Ms(shop.Startup)} |");
report.AppendLine($"| First start on an empty database (creates the tables) | {Ms(firstStart)} |");
report.AppendLine($"| Memory at idle, plain ASP.NET Core | {baseline.IdleMb} MB |");
report.AppendLine($"| Memory at idle, Shop.Api with PulseSharp ({idleSeconds} s after start) | {shop.IdleMb} MB |");
report.AppendLine($"| Load | {tills} tills x {salesPerTill} sales = {sales:N0} sales (each sale: 2 lines, 3 handlers in the cloud) |");
foreach (var (label, load) in results)
{
    report.AppendLine($"| **{label}** | |");
    report.AppendLine($"| &nbsp;&nbsp;Tills selling (SQLite, one commit per sale) | {load.EmitTime.TotalSeconds:0.0} s, {sales / load.EmitTime.TotalSeconds:N0} sales/s in total |");
    report.AppendLine($"| &nbsp;&nbsp;Sync + cloud handling, start to last sale handled | {load.TotalTime.TotalSeconds:0.0} s, {sales / load.TotalTime.TotalSeconds:N0} sales/s, {sales * 3 / load.TotalTime.TotalSeconds:N0} handler runs/s |");
    report.AppendLine($"| &nbsp;&nbsp;Cloud memory, peak / 10 s after | {load.PeakWorkingSetMb} MB / {load.WorkingSetAfterMb} MB |");
    report.AppendLine($"| &nbsp;&nbsp;Cloud CPU during load (share of one core) | {load.AverageCpuPercent:0}% |");
    report.AppendLine($"| &nbsp;&nbsp;Cursor rows after the load | {load.CursorRows:N0} |");
}

report.AppendLine($"| PulseSharp in the publish folder | {pulseDlls.Length} DLLs, {pulseDlls.Sum(file => file.Length) / 1024.0:0} KB |");

Console.WriteLine();
Console.WriteLine(report);
var resultFile = Path.Combine(repo, "artifacts", "measure-result.md");
Directory.CreateDirectory(Path.GetDirectoryName(resultFile)!);
File.WriteAllText(resultFile, report.ToString());
Console.WriteLine($"Saved to {resultFile}");

return;

string Publish(string project, string folder, string dll)
{
    var output = Path.Combine(repo, "artifacts", "publish", folder);
    var info = new ProcessStartInfo("dotnet", $"publish \"{Path.Combine(repo, project)}\" -c Release -o \"{output}\" --nologo -v q") { RedirectStandardOutput = true };
    using var process = Process.Start(info)!;
    process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"dotnet publish {project} failed.");
    }

    return Path.Combine(output, dll);
}

async Task RecreateDatabase(string serverConnection)
{
    await using var admin = new NpgsqlConnection(serverConnection + ";Database=postgres");
    await admin.OpenAsync();
    foreach (var sql in new[] { "DROP DATABASE IF EXISTS shop_measure WITH (FORCE)", "CREATE DATABASE shop_measure" })
    {
        await using var command = new NpgsqlCommand(sql, admin);
        await command.ExecuteNonQueryAsync();
    }
}

async Task<(TimeSpan Startup, long IdleMb, RunningApp? App)> MeasureStartAndIdle(string dll, Dictionary<string, string> environment, int warmRuns, bool keepRunning = false)
{
    var times = new List<TimeSpan>();
    for (var run = 0; run < warmRuns; run++)
    {
        using (var app = await RunningApp.Start(dll, Port, environment))
        {
            times.Add(app.StartupTime);
        }

        await ReleaseLeases();
    }

    var running = await RunningApp.Start(dll, Port, environment);
    await Task.Delay(TimeSpan.FromSeconds(idleSeconds));
    var idle = running.WorkingSetMb;
    var median = times.Order().ElementAt(times.Count / 2);
    if (keepRunning)
    {
        return (median, idle, running);
    }

    running.Dispose();
    return (median, idle, null);
}

async Task ReleaseLeases()
{
    // The runs above end by killing the app, which can't give back its handler leases (30 s). Clearing them keeps the
    // next run from waiting for the lease to expire. A real app that stops normally releases them itself.
    await using var data = NpgsqlDataSource.Create(connectionString);
    await using var command = data.CreateCommand("UPDATE pulsesharp.handlers SET lease_owner = NULL, lease_until = NULL");
    try
    {
        await command.ExecuteNonQueryAsync();
    }
    catch (PostgresException)
    {
    }
}

string Ms(TimeSpan time)
{
    return $"{time.TotalMilliseconds:N0} ms";
}

int IntArgument(string name, int fallback)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? int.Parse(args[index + 1]) : fallback;
}

static string FindRepoRoot()
{
    var folder = new DirectoryInfo(AppContext.BaseDirectory);
    while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "PulseSharp.Samples.slnx")))
    {
        folder = folder.Parent;
    }

    return folder?.FullName ?? throw new InvalidOperationException("Run this from inside the pulsesharp-samples folder.");
}
