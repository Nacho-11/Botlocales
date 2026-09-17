using System.ComponentModel.DataAnnotations;

namespace ParrillitaIA.Agent.Options;

public sealed class TrainerAutomationOptions
{
    public const string SectionName = "TrainerAutomation";

    [Required]
    public string ExecutablePath { get; init; } = string.Empty;

    [Required]
    public string Local { get; init; } = "SAN_PEDRO";

    [Required]
    public string ClosuresWorkflow { get; init; } = "CIERRES";

    [Range(5, 120)]
    public int TimeoutMinutes { get; init; } = 30;

    [Required]
    public string LogRoot { get; init; } = @"C:\ParrillitaIA\Logs\Trainer";
}
