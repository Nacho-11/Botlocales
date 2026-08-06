namespace ParrillitaIA.Agent.Domain;

public enum ReportKind
{
    CashClosure,
    DeliverySales
}

public sealed record ReportJob(
    Guid Id,
    ReportKind Kind,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Cashier,
    string? Platform)
{
    public static ReportJob CashClosure(DateOnly date, string cashier) =>
        new(Guid.NewGuid(), ReportKind.CashClosure, date, date, cashier, null);

    public static ReportJob Delivery(DateOnly start, DateOnly end, string platform) =>
        new(Guid.NewGuid(), ReportKind.DeliverySales, start, end, null, platform);
}
