using ParrillitaIA.Agent.Domain;

namespace ParrillitaIA.Agent.Services;

// Permite probar todo el flujo sin tocar SoftRestaurant.
// Genera un archivo de muestra en la carpeta de descarga del trabajo.
public sealed class SimulatedSoftRestaurantBot : ISoftRestaurantBot
{
    private readonly ILogger<SimulatedSoftRestaurantBot> _logger;

    public SimulatedSoftRestaurantBot(ILogger<SimulatedSoftRestaurantBot> logger) =>
        _logger = logger;

    public async Task<BotResult> ExecuteAsync(
        ReportJob job,
        string downloadDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(downloadDirectory);
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        var extension = job.Kind == ReportKind.CashClosure ? ".pdf" : ".xlsx";
        var file = Path.Combine(downloadDirectory, $"SoftRestaurant_{Guid.NewGuid():N}{extension}");

        await File.WriteAllTextAsync(
            file,
            $"SIMULACION PARRILLITA IA{Environment.NewLine}{job}",
            cancellationToken);

        _logger.LogInformation("Simulación creó {File}", file);
        return BotResult.Ok(file);
    }
}
