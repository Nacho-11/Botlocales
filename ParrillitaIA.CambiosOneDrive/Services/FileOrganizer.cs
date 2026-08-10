using Microsoft.Extensions.Options;
using ParrillitaIA.Agent.Domain;
using ParrillitaIA.Agent.Options;

namespace ParrillitaIA.Agent.Services;

public sealed class FileOrganizer : IFileOrganizer
{
    private readonly StorageOptions _storage;
    private readonly IReportFileNameService _names;

    public FileOrganizer(
        IOptions<StorageOptions> storage,
        IReportFileNameService names)
    {
        _storage = storage.Value;
        _names = names;
    }

    public Task<string> OrganizeAsync(
        ReportJob job,
        string downloadedFile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var extension = Path.GetExtension(downloadedFile);
        var officialName = _names.Build(job, extension);

        var folder = Path.Combine(
            _storage.ArchiveRoot,
            _names.BuildArchiveRelativeFolder(job));

        Directory.CreateDirectory(folder);

        var target = Path.Combine(folder, officialName);

        if (File.Exists(target))
            throw new IOException($"Ya existe el reporte oficial: {target}");

        File.Move(downloadedFile, target);

        return Task.FromResult(target);
    }
}
