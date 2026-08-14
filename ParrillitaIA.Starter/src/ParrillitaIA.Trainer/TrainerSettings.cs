using System.Text.Json;

namespace ParrillitaIA.Trainer;

public sealed class TrainerSettings
{
    public SoftRestaurantSettings SoftRestaurant { get; init; } = new();

    public static TrainerSettings Load(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, "trainer.settings.json");

        if (!File.Exists(path))
            throw new FileNotFoundException($"No se encontró: {path}");

        return JsonSerializer.Deserialize<TrainerSettings>(
            File.ReadAllText(path),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? throw new InvalidOperationException(
                "trainer.settings.json no es válido.");
    }
}

public sealed class SoftRestaurantSettings
{
    public string ExecutablePath { get; init; } = string.Empty;
    public string ProcessName { get; init; } = "softrestaurant";
    public string StableWindowTitle { get; init; } = "SOFT RESTAURANT";
    public string LoginWindowTitle { get; init; } = "Inicio de sesión";
    public string Username { get; init; } = string.Empty;

    public int LaunchTimeoutSeconds { get; init; } = 30;
    public int LoginTimeoutSeconds { get; init; } = 20;

    // Coordenadas RELATIVAS a la ventana "Inicio de sesión".
    // Se pueden ajustar sin recompilar.
    public double LoginUsernameX { get; init; } = 0.44;
    public double LoginUsernameY { get; init; } = 0.37;

    public double LoginPasswordX { get; init; } = 0.44;
    public double LoginPasswordY { get; init; } = 0.54;

    public double LoginButtonX { get; init; } = 0.53;
    public double LoginButtonY { get; init; } = 0.80;
}
