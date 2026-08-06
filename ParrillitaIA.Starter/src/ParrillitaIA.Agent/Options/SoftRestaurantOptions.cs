using System.ComponentModel.DataAnnotations;

namespace ParrillitaIA.Agent.Options;

public sealed class SoftRestaurantOptions
{
    public const string SectionName = "SoftRestaurant";

    [Required]
    public string ExecutablePath { get; init; } = string.Empty;

    [Required]
    public string Username { get; init; } = string.Empty;

    public string[] Cashiers { get; init; } = [];
    public string[] DeliveryPlatforms { get; init; } = [ "UBER", "DIDI", "PEDIDOSYA" ];
    public bool SimulationMode { get; init; } = true;
}
