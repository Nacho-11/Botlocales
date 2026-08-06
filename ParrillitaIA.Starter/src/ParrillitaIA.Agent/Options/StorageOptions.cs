using System.ComponentModel.DataAnnotations;

namespace ParrillitaIA.Agent.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    [Required]
    public string WorkRoot { get; init; } = string.Empty;

    [Required]
    public string ArchiveRoot { get; init; } = string.Empty;

    [Required]
    public string OneDriveSyncRoot { get; init; } = string.Empty;

    [Required]
    public string HistoryFile { get; init; } = string.Empty;
}
