using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PulseSharp;
using Shop.Contracts;

namespace Shop.Api.Reports;

/// <summary>Adds up the sales of one day and saves the result. Running it twice for a day gives the same report.</summary>
public sealed class DailyReportHandler(NpgsqlDataSource dataSource, IPulse pulse) : IJobHandler<DailyReportJob>
{
    public async Task Handle(DailyReportJob job, JobContext context, CancellationToken cancellationToken)
    {
        var from = new DateTimeOffset(job.Day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var perTill = new Dictionary<string, decimal>();
        var sales = 0;

        await using (var query = dataSource.CreateCommand("SELECT till, count(*), sum(total) FROM shop.sales WHERE sold_at >= $1 AND sold_at < $2 GROUP BY till"))
        {
            query.Parameters.AddWithValue(from.UtcDateTime);
            query.Parameters.AddWithValue(from.AddDays(1).UtcDateTime);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                perTill[reader.GetString(0)] = reader.GetDecimal(2);
                sales += (int)reader.GetInt64(1);
            }
        }

        await context.Report(1, 2, cancellationToken);

        const string upsert = """
            INSERT INTO shop.daily_reports (day, sales, revenue, by_till, created_at) VALUES ($1, $2, $3, $4, now())
            ON CONFLICT (day) DO UPDATE SET sales = $2, revenue = $3, by_till = $4, created_at = now()
            """;
        await using (var save = dataSource.CreateCommand(upsert))
        {
            save.Parameters.AddWithValue(NpgsqlDbType.Date, job.Day);
            save.Parameters.AddWithValue(sales);
            save.Parameters.AddWithValue(perTill.Values.Sum());
            save.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = JsonSerializer.Serialize(perTill) });
            await save.ExecuteNonQueryAsync(cancellationToken);
        }

        await context.Report(2, 2, cancellationToken);

        if (job.ScheduleNext)
        {
            await ScheduleReport(pulse, job.Day.AddDays(1), cancellationToken);
        }
    }

    /// <summary>Plans the report of a day to run just after that day ends.</summary>
    public static Task ScheduleReport(IPulse pulse, DateOnly day, CancellationToken cancellationToken)
    {
        var endOfDay = new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        return pulse.EnqueueAt(new DailyReportJob(day, ScheduleNext: true), endOfDay.AddMinutes(1), cancellationToken);
    }
}
