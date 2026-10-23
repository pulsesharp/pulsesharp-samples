using PulseSharp;
using Shop.Api.Reports;
using Shop.Contracts;

namespace Shop.Api;

/// <summary>
/// Runs once when the app starts, after PulseSharp is ready (hosted services start in the order they are added):
/// puts the first products on the menu and makes sure tonight's report is planned.
/// </summary>
public sealed class ShopStartup(IPulse pulse, IPulseControl control, ILogger<ShopStartup> logger) : IHostedService
{
    private static readonly ProductAdded[] FirstProducts =
    [
        new("coffee", "Coffee", 3.20m, 20),
        new("latte", "Latte", 3.80m, 20),
        new("tea", "Tea", 2.50m, 20),
        new("croissant", "Croissant", 2.90m, 12),
        new("muffin", "Muffin", 2.70m, 12),
        new("water", "Water", 1.50m, 30),
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await PutFirstProductsOnTheMenu(cancellationToken);
        await PlanTonightsReport(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task PutFirstProductsOnTheMenu(CancellationToken cancellationToken)
    {
        try
        {
            // NoStream means "only if the menu has no events yet", so two instances starting together seed it once.
            await pulse.Emit(Streams.Menu, FirstProducts[0], new EmitOptions { ExpectedVersion = ExpectedVersion.NoStream }, cancellationToken);
        }
        catch (ExpectedVersionMismatchException)
        {
            return;
        }

        foreach (var product in FirstProducts.Skip(1))
        {
            await pulse.Emit(Streams.Menu, product, cancellationToken);
        }

        logger.LogInformation("The menu was empty. Added {Count} products.", FirstProducts.Length);
    }

    private async Task PlanTonightsReport(CancellationToken cancellationToken)
    {
        var waiting = await control.GetJobs(JobState.Pending, cancellationToken: cancellationToken);
        var retrying = await control.GetJobs(JobState.Retrying, cancellationToken: cancellationToken);
        if (waiting.Concat(retrying).Any(job => job.JobName == nameof(DailyReportJob)))
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await DailyReportHandler.ScheduleReport(pulse, today, cancellationToken);
        logger.LogInformation("Planned the report for {Day}. It runs just after midnight UTC.", today);
    }
}
