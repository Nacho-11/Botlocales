using System.ComponentModel.DataAnnotations;

namespace ParrillitaIA.Agent.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    [Required]
    public string WorkRoot { get; init; } = string.Empty;

    [Required]
    public string ArchiveRoot { get; init; } = string.Empty;

    // Ejemplo:
    // C:\Users\ig_ca\OneDrive - Empresa\Cierres - Sabana
    [Required]
    public string OneDriveCashClosuresRoot { get; init; } = string.Empty;

    // Ejemplo:
    // C:\Users\ig_ca\OneDrive - Empresa\Reportes Delivery Sabana
    [Required]
    public string OneDriveDeliveryRoot { get; init; } = string.Empty;

    [Required]
    public string HistoryFile { get; init; } = string.Empty;
}
