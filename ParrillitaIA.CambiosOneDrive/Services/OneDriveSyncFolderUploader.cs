using Microsoft.Extensions.Options;
using ParrillitaIA.Agent.Domain;
using ParrillitaIA.Agent.Options;

namespace ParrillitaIA.Agent.Services;

// Copia los reportes a la carpeta local sincronizada por OneDrive.
// Cierres y Delivery pueden tener raíces distintas.
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
        var root = job.Kind switch
        {
            ReportKind.CashClosure => _storage.OneDriveCashClosuresRoot,
            ReportKind.DeliverySales => _storage.OneDriveDeliveryRoot,
            _ => throw new ArgumentOutOfRangeException(nameof(job.Kind))
        };

        var folder = Path.Combine(
            root,
            _names.BuildOneDriveRelativeFolder(job));

        Directory.CreateDirectory(folder);

        var target = Path.Combine(
            folder,
            Path.GetFileName(localFile));

        if (File.Exists(target))
            throw new IOException($"El archivo ya existe en OneDrive: {target}");

        await using var source = File.OpenRead(localFile);

        await using var destination = new FileStream(
            target,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

        await source.CopyToAsync(destination, cancellationToken);

        return target;
    }
}
