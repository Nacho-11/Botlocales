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
            // Formato de cierres solicitado:
            // 1-2 SAMBRANO.xlsx
            // 2-2 SANCHEZ.xlsx
            //
            // Día-Mes + nombre real del usuario de SoftRestaurant.
            ReportKind.CashClosure =>
                $"{job.StartDate.Day}-{job.StartDate.Month} " +
                $"{NormalizeDisplay(job.Cashier ?? "SIN USUARIO")}{extension}",

            // Formato actual de delivery:
            // ALAJUELA DIDI 3-8 AL 9-8.xlsx
            ReportKind.DeliverySales =>
                $"{local} {GetPlatformFileLabel(job.Platform)} " +
                $"{job.StartDate.Day}-{job.StartDate.Month} AL " +
                $"{job.EndDate.Day}-{job.EndDate.Month}{extension}",

            _ => throw new ArgumentOutOfRangeException(nameof(job.Kind))
        };
    }

    public string BuildArchiveRelativeFolder(ReportJob job)
    {
        var date = job.EndDate;
        var month = BuildMonthFolder(date);

        return job.Kind switch
        {
            ReportKind.CashClosure => Path.Combine(
                _local.Name,
                date.Year.ToString(),
                month,
                "Cierres"),

            ReportKind.DeliverySales => Path.Combine(
                _local.Name,
                date.Year.ToString(),
                month,
                "Delivery",
                GetPlatformFolderName(job.Platform)),

            _ => throw new ArgumentOutOfRangeException(nameof(job.Kind))
        };
    }

    public string BuildOneDriveRelativeFolder(ReportJob job)
    {
        var date = job.EndDate;
        var month = BuildMonthFolder(date);

        return job.Kind switch
        {
            // La raíz de OneDrive ya identifica el local:
            // Cierres - Sabana\2026\8.Agosto
            ReportKind.CashClosure => Path.Combine(
                date.Year.ToString(),
                month),

            // Reportes Delivery Sabana\2026\8.Agosto\Didi
            ReportKind.DeliverySales => Path.Combine(
                date.Year.ToString(),
                month,
                GetPlatformFolderName(job.Platform)),

            _ => throw new ArgumentOutOfRangeException(nameof(job.Kind))
        };
    }

    private static string BuildMonthFolder(DateOnly date) =>
        $"{date.Month}.{GetMonthName(date.Month)}";

    private static string GetPlatformFolderName(string? platform)
    {
        return Normalize(platform ?? "OTROS") switch
        {
            "DIDI" => "Didi",
            "PEDIDOSYA" => "Pedidos ya",
            "PEDIDOS_YA" => "Pedidos ya",
            "UBER" => "Uber",
            var other => ToDisplayName(other)
        };
    }

    private static string GetPlatformFileLabel(string? platform)
    {
        return Normalize(platform ?? "OTROS") switch
        {
            "DIDI" => "DIDI",
            "PEDIDOSYA" => "PEDIDOS YA",
            "PEDIDOS_YA" => "PEDIDOS YA",
            "UBER" => "UBER",
            var other => other.Replace('_', ' ')
        };
    }

    // Para archivos de cierre conservamos espacios y hacemos el nombre legible.
    // También elimina caracteres que Windows no permite en nombres de archivo.
    private static string NormalizeDisplay(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();

        var normalized = value
            .Trim()
            .ToUpperInvariant()
            .Normalize(NormalizationForm.FormD);

        var chars = normalized
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Where(c => !invalidChars.Contains(c))
            .ToArray();

        return string.Join(
            ' ',
            new string(chars)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Normalize(string value)
    {
        var normalized = value
            .Trim()
            .ToUpperInvariant()
            .Normalize(NormalizationForm.FormD);

        var chars = normalized
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();

        return string.Join(
            '_',
            new string(chars).Split(
                '_',
                StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeExtension(string extension) =>
        extension.StartsWith('.')
            ? extension.ToLowerInvariant()
            : $".{extension.ToLowerInvariant()}";

    private static string ToDisplayName(string value)
    {
        var lower = value.Replace('_', ' ').ToLowerInvariant();
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(lower);
    }

    private static string GetMonthName(int month) => month switch
    {
        1 => "Enero",
        2 => "Febrero",
        3 => "Marzo",
        4 => "Abril",
        5 => "Mayo",
        6 => "Junio",
        7 => "Julio",
        8 => "Agosto",
        9 => "Septiembre",
        10 => "Octubre",
        11 => "Noviembre",
        12 => "Diciembre",
        _ => throw new ArgumentOutOfRangeException(nameof(month))
    };
}
