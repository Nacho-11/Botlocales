using System.Text.Json;
using Microsoft.Extensions.Options;
using ParrillitaIA.Agent.Domain;
using ParrillitaIA.Agent.Options;

namespace ParrillitaIA.Agent.Services;

public sealed class JsonExecutionHistory : IExecutionHistory
{
    private readonly string _file;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonExecutionHistory(IOptions<StorageOptions> storage)
    {
        _file = storage.Value.HistoryFile;
    }

    public async Task AppendAsync(
        ExecutionRecord record,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var line = JsonSerializer.Serialize(record) + Environment.NewLine;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(_file, line, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }
}
