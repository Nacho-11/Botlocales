using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using ParrillitaIA.Agent.Options;

namespace ParrillitaIA.Agent.Services;

public sealed class TrainerProcessRunner : ITrainerProcessRunner
{
    private readonly ILogger<TrainerProcessRunner> _logger;
    private readonly TrainerAutomationOptions _options;

    public TrainerProcessRunner(
        ILogger<TrainerProcessRunner> logger,
        IOptions<TrainerAutomationOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task<TrainerRunResult> RunClosuresAsync(
        DateOnly reportDate,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_options.ExecutablePath))
        {
            return new TrainerRunResult(
                false, -1, string.Empty,
                "TRAINER_NOT_FOUND",
                $"No existe el Trainer: {_options.ExecutablePath}");
        }

        Directory.CreateDirectory(_options.LogRoot);

        var logFile = Path.Combine(
            _options.LogRoot,
            $"CIERRES_{_options.Local}_{reportDate:yyyy-MM-dd}_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        var psi = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            Arguments = $"run {_options.Local} {_options.ClosuresWorkflow}",
            WorkingDirectory = Path.GetDirectoryName(_options.ExecutablePath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false
        };

        using var process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true
        };

        var output = new StringBuilder();
        var sync = new object();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (sync) output.AppendLine(e.Data);
            _logger.LogInformation("[TRAINER] {Line}", e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (sync) output.AppendLine("[STDERR] " + e.Data);
            _logger.LogWarning("[TRAINER][STDERR] {Line}", e.Data);
        };

        _logger.LogInformation(
            "Iniciando Trainer: {Exe} run {Local} {Workflow}; FechaReporte={ReportDate}",
            _options.ExecutablePath,
            _options.Local,
            _options.ClosuresWorkflow,
            reportDate);

        if (!process.Start())
        {
            return new TrainerRunResult(
                false, -1, logFile,
                "TRAINER_START_FAILED",
                "Process.Start devolvió false.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts =
            new CancellationTokenSource(TimeSpan.FromMinutes(_options.TimeoutMinutes));

        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
            await Task.Delay(250, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch { }
            }

            var timeoutText =
                $"Trainer excedió {_options.TimeoutMinutes} minutos.";

            await File.WriteAllTextAsync(
                logFile,
                output + Environment.NewLine + timeoutText,
                Encoding.UTF8);

            if (cancellationToken.IsCancellationRequested)
                throw;

            return new TrainerRunResult(
                false, -1, logFile,
                "TRAINER_TIMEOUT",
                timeoutText);
        }

        var text = output.ToString();

        await File.WriteAllTextAsync(
            logFile,
            text,
            Encoding.UTF8);

        if (process.ExitCode != 0)
        {
            return new TrainerRunResult(
                false,
                process.ExitCode,
                logFile,
                "TRAINER_EXIT_CODE",
                $"Trainer terminó con código {process.ExitCode}.");
        }

        var flowFinished =
            text.Contains("Flujo finalizado.", StringComparison.OrdinalIgnoreCase);

        var hasExportError =
            text.Contains("[EXPORT][ERROR]", StringComparison.OrdinalIgnoreCase);

        var hasInconclusive =
            text.Contains("[SCAN][INCONCLUSOS]", StringComparison.OrdinalIgnoreCase);

        var hasWarn =
            text.Contains("[V6.18.80][WARN]", StringComparison.OrdinalIgnoreCase);

        var coverageComplete =
            text.Contains("[SCAN][COVERAGE][OK]", StringComparison.OrdinalIgnoreCase);

        var maxThreeReached =
            text.Contains("[SCAN][MAX]", StringComparison.OrdinalIgnoreCase);

        var successfulProcessing =
            text.Contains("[V6.18.80][OK]", StringComparison.OrdinalIgnoreCase);

        var noClosures =
            text.Contains(
                "[SCAN][STOP] No se encontraron cierres",
                StringComparison.OrdinalIgnoreCase);

        if (!flowFinished ||
            hasExportError ||
            hasInconclusive ||
            hasWarn ||
            !(coverageComplete || maxThreeReached) ||
            !(successfulProcessing || noClosures))
        {
            return new TrainerRunResult(
                false,
                process.ExitCode,
                logFile,
                "TRAINER_OUTPUT_INVALID",
                "El Trainer terminó, pero los marcadores no confirman una ejecución productiva completa.");
        }

        return new TrainerRunResult(
            true,
            process.ExitCode,
            logFile,
            null,
            null);
    }
}
