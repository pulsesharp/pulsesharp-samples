using PulseSharp.Testing;
using Shop.Api.Reports;
using Shop.Contracts;

namespace Shop.Tests;

public class ReportSchedulingTests
{
    [Fact]
    public async Task TheReportOfADayRunsJustAfterThatDayEnds()
    {
        var pulse = new FakePulse();

        await DailyReportHandler.ScheduleReport(pulse, new DateOnly(2026, 10, 23), CancellationToken.None);

        var job = pulse.Enqueued<DailyReportJob>();
        Assert.Equal(new DateOnly(2026, 10, 23), job.Day);
        Assert.True(job.ScheduleNext);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 0, 1, 0, TimeSpan.Zero), pulse.Jobs.Single().RunAt);
    }
}
