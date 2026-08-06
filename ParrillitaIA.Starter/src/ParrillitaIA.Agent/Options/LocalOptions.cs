using System.ComponentModel.DataAnnotations;

namespace ParrillitaIA.Agent.Options;

public sealed class LocalOptions
{
    public const string SectionName = "Local";

    [Required]
    public string Code { get; init; } = string.Empty;

    [Required]
    public string Name { get; init; } = string.Empty;
}
