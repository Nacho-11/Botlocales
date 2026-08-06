using Microsoft.Extensions.Options;
using ParrillitaIA.Agent.Domain;
using ParrillitaIA.Agent.Options;

namespace ParrillitaIA.Agent.Services;

// Primera implementación: copia el archivo a una carpeta sincronizada por OneDrive.
// Posteriormente puede sustituirse por Microsoft Graph sin cambiar el coordinador.
public sealed class OneDriveSyncFolderUploader : ICloudUploader
{
    private readonly StorageOptions _storage;
    private readonly IReportFileNameService _names;

    public OneDriveSyncFolderUploader(
        IOptions<StorageOptions> storage,
        IReportFileNameService names)
    {
        _storage = storage.Value;
        _names = names;
    }

    public async Task<string> UploadAsync(
        ReportJob job,
        string localFile,
        CancellationToken cancellationToken)
    {
        var folder = Path.Combine(_storage.OneDriveSyncRoot, _names.BuildRelativeFolder(job));
        Directory.CreateDirectory(folder);

        var target = Path.Combine(folder, Path.GetFileName(localFile));

        if (File.Exists(target))
            throw new IOException($"El archivo ya existe en OneDrive: {target}");

        await using var source = File.OpenRead(localFile);
        await using var destination = new FileStream(
            target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        await source.CopyToAsync(destination, cancellationToken);
        return target;
    }
}
