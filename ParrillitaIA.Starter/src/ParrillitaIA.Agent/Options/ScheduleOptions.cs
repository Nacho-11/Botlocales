using System.ComponentModel.DataAnnotations;

namespace ParrillitaIA.Agent.Options;

public sealed class ScheduleOptions
{
    public const string SectionName = "Schedule";

    [Range(0, 23)]
    public int CashClosuresHour { get; init; } = 5;

    [Range(0, 59)]
    public int CashClosuresMinute { get; init; } = 30;

    public DayOfWeek DeliveryDayOfWeek { get; init; } = DayOfWeek.Monday;

    [Range(0, 23)]
    public int DeliveryHour { get; init; } = 6;

    [Range(0, 59)]
    public int DeliveryMinute { get; init; }

    [Range(10, 3600)]
    public int PollSeconds { get; init; } = 30;
}
