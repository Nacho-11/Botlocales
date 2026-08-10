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

    // Ruta usada para conservar una copia local ordenada.
    string BuildArchiveRelativeFolder(ReportJob job);

    // Ruta relativa dentro de la raíz de OneDrive configurada.
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
    Task AppendAsync(ExecutionRecord record, CancellationToken cancellationToken);
}
