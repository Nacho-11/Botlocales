namespace ParrillitaIA.Agent.Domain;

public sealed record BotResult(
    bool Success,
    string? DownloadedFile,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static BotResult Ok(string file) => new(true, file, null, null);
    public static BotResult Fail(string code, string message) => new(false, null, code, message);
}
