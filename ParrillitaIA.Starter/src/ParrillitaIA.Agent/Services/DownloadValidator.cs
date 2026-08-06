namespace ParrillitaIA.Agent.Services;

public sealed class DownloadValidator : IDownloadValidator
{
    public async Task<bool> WaitUntilReadyAsync(
        string filePath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        long previousLength = -1;
        var stableChecks = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(filePath))
            {
                var length = new FileInfo(filePath).Length;

                if (length > 0 && length == previousLength)
                    stableChecks++;
                else
                    stableChecks = 0;

                previousLength = length;

                if (stableChecks >= 2 && CanOpenExclusively(filePath))
                    return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        return false;
    }

    private static bool CanOpenExclusively(string path)
    {
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.None);
            return stream.Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
