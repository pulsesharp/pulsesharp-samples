using System.Text.Json;
using Npgsql;
using PulseSharp;
using Shop.Api.Receipts;
using Shop.Contracts;

namespace Shop.Api.Admin;

/// <summary>What an operator needs: see problems, fix them, run a job. All of it goes through <see cref="IPulseControl"/>.</summary>
public static class AdminEndpoints
{
    public static void MapAdmin(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/handlers", async (IPulseControl control, CancellationToken cancellationToken) =>
        {
            var cursors = await control.GetCursors(cancellationToken: cancellationToken);

            return cursors.Select(cursor => new
            {
                cursor.HandlerName,
                cursor.Stream,
                State = cursor.State.ToString(),
                cursor.Attempts,
                cursor.LastError,
            });
        });

        app.MapPost("/admin/retry-failed", async (IPulseControl control, CancellationToken cancellationToken) =>
        {
            var streams = await control.RetryFailed(cancellationToken: cancellationToken);
            return new { StreamsRetried = streams };
        });

        app.MapGet("/admin/jobs", async (IPulseControl control, CancellationToken cancellationToken) =>
        {
            var jobs = await control.GetJobs(limit: 20, cancellationToken: cancellationToken);
            return jobs.Select(job => new
            {
                job.JobId,
                job.JobName,
                State = job.State.ToString(),
                job.Attempts,
                Progress = job.Total == 0 ? null : $"{job.Done}/{job.Total}",
                job.RunAt,
                job.LastError,
            });
        });

        app.MapPost("/admin/printer", (PrinterState body, ReceiptPrinter printer) =>
        {
            printer.IsBroken = body.Broken;
            return new { printer.IsBroken };
        });

        app.MapPost("/reports/run", async (DateOnly? day, IPulse pulse, CancellationToken cancellationToken) =>
        {
            var reportDay = day ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var jobId = await pulse.Enqueue(new DailyReportJob(reportDay, ScheduleNext: false), cancellationToken);
            return Results.Accepted(value: new { jobId, day = reportDay });
        });

        app.MapGet("/reports", async (NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
        {
            await using var command = dataSource.CreateCommand("SELECT day, sales, revenue, by_till::text FROM shop.daily_reports ORDER BY day DESC LIMIT 14");
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var reports = new List<object>();
            while (await reader.ReadAsync(cancellationToken))
            {
                reports.Add(new
                {
                    Day = reader.GetFieldValue<DateOnly>(0),
                    Sales = reader.GetInt32(1),
                    Revenue = reader.GetDecimal(2),
                    ByTill = JsonSerializer.Deserialize<Dictionary<string, decimal>>(reader.GetString(3)),
                });
            }

            return reports;
        });
    }

    public sealed record PrinterState(bool Broken);
}
