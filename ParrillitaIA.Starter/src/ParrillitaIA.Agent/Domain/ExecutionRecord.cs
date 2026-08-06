namespace ParrillitaIA.Agent.Domain;

public sealed record ExecutionRecord(
    Guid ExecutionId,
    Guid JobId,
    string LocalCode,
    string ReportKind,
    string Status,
    string? OfficialFileName,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt);
