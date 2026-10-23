namespace Shop.Contracts;

/// <summary>Adds up the sales of one day (UTC). With <see cref="ScheduleNext"/> it also plans tomorrow's report.</summary>
public sealed record DailyReportJob(DateOnly Day, bool ScheduleNext);
