using ParrillitaIA.Agent.Domain;

namespace ParrillitaIA.Agent.Services;

// Permite probar todo el flujo sin tocar SoftRestaurant.
// Ahora tanto cierres como delivery simulan archivos Excel.
public sealed class SimulatedSoftRestaurantBot : ISoftRestaurantBot
{
    private readonly ILogger<SimulatedSoftRestaurantBot> _logger;

    public SimulatedSoftRestaurantBot(
        ILogger<SimulatedSoftRestaurantBot> logger) =>
        _logger = logger;

    public async Task<BotResult> ExecuteAsync(
        ReportJob job,
        string downloadDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(downloadDirectory);
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        // CAMBIO: cierres también se guardan como Excel.
        var extension = ".xlsx";

        var file = Path.Combine(
            downloadDirectory,
            $"SoftRestaurant_{Guid.NewGuid():N}{extension}");

        // Solo es una simulación de flujo.
        // El archivo real será generado por SoftRestaurant cuando activemos FlaUI.
        await File.WriteAllTextAsync(
            file,
            $"SIMULACION PARRILLITA IA{Environment.NewLine}{job}",
            cancellationToken);

        _logger.LogInformation(
            "Simulación creó {File}",
            file);

        return BotResult.Ok(file);
    }
}
