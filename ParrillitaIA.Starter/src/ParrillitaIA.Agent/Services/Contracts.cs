using ParrillitaIA.Agent.Domain;

namespace ParrillitaIA.Agent.Services;

public interface IClock
{
    DateTimeOffset Now { get; }
}

public interface ISoftRestaurantBot
{
    Task<BotResult> ExecuteAsync(
        ReportJob job,
        string downloadDirectory,
        CancellationToken cancellationToken);
}

public interface IReportFileNameService
{
    string Build(ReportJob job, string extension);
    string BuildArchiveRelativeFolder(ReportJob job);
    string BuildOneDriveRelativeFolder(ReportJob job);
}

public interface IDownloadValidator
{
    Task<bool> WaitUntilReadyAsync(
        string filePath,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public interface IFileOrganizer
{
    Task<string> OrganizeAsync(
        ReportJob job,
        string downloadedFile,
        CancellationToken cancellationToken);
}

public interface ICloudUploader
{
    Task<string> UploadAsync(
        ReportJob job,
        string localFile,
        CancellationToken cancellationToken);
}

public interface IExecutionHistory
{
    Task AppendAsync(
        ExecutionRecord record,
        CancellationToken cancellationToken);
}

public interface ITrainerProcessRunner
{
    Task<TrainerRunResult> RunClosuresAsync(
        DateOnly reportDate,
        CancellationToken cancellationToken);
}

public interface IDesktopProcessCleanup
{
    Task CleanupAsync(
        string reason,
        CancellationToken cancellationToken);
}

public sealed record TrainerRunResult(
    bool Success,
    int ExitCode,
    string LogFile,
    string? ErrorCode,
    string? ErrorMessage);
