using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using ParrillitaIA.Agent.Domain;
using ParrillitaIA.Agent.Options;

namespace ParrillitaIA.Agent.Services;

public sealed class ReportFileNameService : IReportFileNameService
{
    private readonly LocalOptions _local;

    public ReportFileNameService(IOptions<LocalOptions> local) =>
        _local = local.Value;

    public string Build(ReportJob job, string extension)
    {
        extension = NormalizeExtension(extension);
        var local = Normalize(_local.Code);

        return job.Kind switch
        {
            ReportKind.CashClosure =>
                $"{job.StartDate:yyyy-MM-dd}_{local}_{Normalize(job.Cashier ?? "SIN_CAJERO")}_CIERRE_CAJA{extension}",

            ReportKind.DeliverySales =>
                $"{job.StartDate:yyyy-MM-dd}_{job.EndDate:yyyy-MM-dd}_{local}_DELIVERY_{Normalize(job.Platform ?? "SIN_PLATAFORMA")}{extension}",

            _ => throw new ArgumentOutOfRangeException(nameof(job.Kind))
        };
    }

    public string BuildRelativeFolder(ReportJob job)
    {
        var date = job.EndDate;
        var month = $"{date.Month:D2}_{GetMonthName(date.Month)}";
        var category = job.Kind == ReportKind.CashClosure ? "Cierres" : "Delivery";

        return job.Kind == ReportKind.DeliverySales
            ? Path.Combine(_local.Name, date.Year.ToString(), month, category, Normalize(job.Platform ?? "OTROS"))
            : Path.Combine(_local.Name, date.Year.ToString(), month, category);
    }

    private static string Normalize(string value)
    {
        var normalized = value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var chars = normalized
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();

        return string.Join('_', new string(chars).Split('_', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeExtension(string extension) =>
        extension.StartsWith('.') ? extension.ToLowerInvariant() : $".{extension.ToLowerInvariant()}";

    private static string GetMonthName(int month) => month switch
    {
        1 => "Enero", 2 => "Febrero", 3 => "Marzo", 4 => "Abril",
        5 => "Mayo", 6 => "Junio", 7 => "Julio", 8 => "Agosto",
        9 => "Septiembre", 10 => "Octubre", 11 => "Noviembre", 12 => "Diciembre",
        _ => throw new ArgumentOutOfRangeException(nameof(month))
    };
}
