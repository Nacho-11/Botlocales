using System.Diagnostics;

namespace ParrillitaIA.Agent.Services;

public sealed class DesktopProcessCleanup : IDesktopProcessCleanup
{
    private readonly ILogger<DesktopProcessCleanup> _logger;

    private static readonly string[] TargetProcessNames =
    [
        "EXCEL",
        "softrestaurant"
    ];

    public DesktopProcessCleanup(
        ILogger<DesktopProcessCleanup> logger)
    {
        _logger = logger;
    }

    public async Task CleanupAsync(
        string reason,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[CLEANUP] Iniciando limpieza. Motivo={Reason}",
            reason);

        foreach (var processName in TargetProcessNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Process[] processes;

            try
            {
                processes =
                    Process.GetProcessesByName(processName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "[CLEANUP] No se pudieron enumerar procesos {ProcessName}.",
                    processName);

                continue;
            }

            if (processes.Length == 0)
            {
                _logger.LogInformation(
                    "[CLEANUP] {ProcessName}: no hay procesos abiertos.",
                    processName);

                continue;
            }

            foreach (var process in processes)
            {
                using (process)
                {
                    await CloseProcessAsync(
                        process,
                        processName,
                        cancellationToken);
                }
            }
        }

        _logger.LogInformation(
            "[CLEANUP] Limpieza finalizada.");
    }

    private async Task CloseProcessAsync(
        Process process,
        string processName,
        CancellationToken cancellationToken)
    {
        try
        {
            if (process.HasExited)
                return;

            _logger.LogInformation(
                "[CLEANUP] Cerrando {ProcessName}; PID={Pid}.",
                processName,
                process.Id);

            var closeRequested = false;

            try
            {
                closeRequested =
                    process.CloseMainWindow();
            }
            catch
            {
            }

            if (closeRequested)
            {
                var deadline =
                    DateTime.UtcNow.AddSeconds(6);

                while (DateTime.UtcNow < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (process.HasExited)
                        {
                            _logger.LogInformation(
                                "[CLEANUP] {ProcessName}; PID={Pid} cerró normalmente.",
                                processName,
                                process.Id);

                            return;
                        }
                    }
                    catch
                    {
                        return;
                    }

                    await Task.Delay(
                        250,
                        cancellationToken);
                }
            }

            try
            {
                if (!process.HasExited)
                {
                    _logger.LogWarning(
                        "[CLEANUP] Forzando cierre de {ProcessName}; PID={Pid}.",
                        processName,
                        process.Id);

                    process.Kill(
                        entireProcessTree: true);

                    using var timeout =
                        new CancellationTokenSource(
                            TimeSpan.FromSeconds(5));

                    using var linked =
                        CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken,
                            timeout.Token);

                    try
                    {
                        await process.WaitForExitAsync(
                            linked.Token);
                    }
                    catch (OperationCanceledException)
                        when (!cancellationToken.IsCancellationRequested)
                    {
                    }
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[CLEANUP] Error cerrando {ProcessName}.",
                processName);
        }
    }
}
